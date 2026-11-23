using System.Security.Claims;
using System.Text.Json;
using BusinessSaas.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.SqlClient;

namespace BusinessSaas.Api.Features;

public sealed record BusinessMarket(string Code, string Name, string[] Currencies, string[] TimeZones);

// These numbers cover recorded cash receipts only, not all future payment methods.
public sealed record CashReceiptSummary(long Id, long LocationId, decimal Total, DateTime PostedAtUtc);
public sealed record OwnerCashSalesToday(decimal Total, int BillCount, DateOnly BusinessDate,
    IReadOnlyList<CashReceiptSummary> RecentReceipts);
public sealed record LowStockItem(long ProductId, string ProductName, string Sku,
    long LocationId, string LocationName, decimal Quantity, decimal ReorderLevel);
public sealed record OwnerStockAlerts(int Count, int OutOfStockCount, IReadOnlyList<LowStockItem> Items);

public static class BusinessEndpoints
{
    private static readonly BusinessMarket[] Markets = LoadMarkets();

    internal static bool ValidMarket(string country, string currency, string timeZone) =>
        Markets.Any(x => x.Code == country && x.Currencies.Contains(currency) && x.TimeZones.Contains(timeZone));

    public static void MapBusinessEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api").RequireAuthorization();

        group.MapGet("/business-types", async (AppDbContext db) =>
            await db.BusinessTypes.AsNoTracking()
                .Where(x => x.IsSupported)
                .OrderBy(x => x.Name)
                .Select(x => new { x.Id, x.Code, x.Name })
                .ToListAsync());

        group.MapGet("/business-markets", () => Results.Ok(Markets));

        group.MapGet("/businesses", async (ClaimsPrincipal principal, AppDbContext db) =>
        {
            var userId = CurrentUserId(principal);
            return await (from member in db.BusinessUsers.AsNoTracking()
                          join business in db.Businesses.AsNoTracking()
                            on member.BusinessId equals business.Id
                          join type in db.BusinessTypes.AsNoTracking()
                            on business.BusinessTypeId equals type.Id
                          where member.UserId == userId
                          orderby business.Name
                          select new
                          {
                              business.Id,
                              business.Name,
                              business.CountryCode,
                              business.CurrencyCode,
                              business.TimeZoneId,
                              business.BusinessTypeId,
                              TypeName = type.Name,
                              TypeCode = type.Code,
                              member.RoleCode
                          }).ToListAsync();
        });

        // The former owner self-service endpoint is retired for all real deployments.
        // Only legacy in-process integration fixtures may opt in while tests migrate
        // to /api/platform/businesses. Never enable from appsettings or production.
        group.MapPost("/businesses", async (CreateBusinessRequest request,
            ClaimsPrincipal principal, AppDbContext db, IConfiguration configuration,
            IWebHostEnvironment environment) =>
        {
            if (!environment.IsDevelopment() ||
                !configuration.GetValue<bool>("Tests:AllowLegacyBusinessCreation"))
                return Results.Forbid();

            var userId = CurrentUserId(principal);
            var name = request.Name?.Trim() ?? "";
            var country = request.CountryCode?.Trim().ToUpperInvariant() ?? "";
            var currency = request.CurrencyCode?.Trim().ToUpperInvariant() ?? "";
            var timeZone = request.TimeZoneId?.Trim() ?? "";
            var requestKey = request.RequestKey?.Trim() ?? "";

            if (name.Length is < 2 or > 150 || requestKey.Length is < 12 or > 80)
                return Results.BadRequest(new { message = "Enter a valid business name and request key." });

            var market = Markets.FirstOrDefault(x => x.Code == country);
            if (market is null || !market.Currencies.Contains(currency) || !market.TimeZones.Contains(timeZone))
                return Results.BadRequest(new { message = "Choose a supported country, currency and timezone." });

            var existing = await db.Businesses.AsNoTracking()
                .FirstOrDefaultAsync(x => x.CreatedByUserId == userId && x.CreationKey == requestKey);
            if (existing is not null)
                return Matches(existing, request, name, country, currency, timeZone)
                    ? Results.Ok(new { existing.Id })
                    : Results.Conflict(new { message = "Request key was already used for another business." });

            var trialDays = configuration.GetValue<int>("Saas:TrialDays");
            if (trialDays is < 1 or > 90)
                return Results.Problem("Trial duration must be between 1 and 90 days.", statusCode: 503);

            var businessType = await db.BusinessTypes.AsNoTracking()
                .Where(x => x.Id == request.BusinessTypeId && x.IsSupported)
                .Select(x => x.Code).SingleOrDefaultAsync();
            if (businessType is null)
                return Results.BadRequest(new { message = "This business type is not available yet." });

            await using var transaction = await db.Database.BeginTransactionAsync();

            try
            {
                var business = new Business
                {
                    Name = name,
                    BusinessTypeId = request.BusinessTypeId,
                    CreatedByUserId = userId,
                    CreationKey = requestKey,
                    CountryCode = country,
                    CurrencyCode = currency,
                    TimeZoneId = timeZone
                };

                db.Businesses.Add(business);
                await db.SaveChangesAsync();

                db.BusinessUsers.Add(new BusinessUser
                {
                    BusinessId = business.Id,
                    UserId = userId,
                    RoleCode = "Owner"
                });
                db.BusinessLocations.Add(new BusinessLocation
                {
                    BusinessId = business.Id,
                    Code = "MAIN",
                    Name = "Main Shop"
                });
                db.BusinessSubscriptions.Add(new BusinessSubscription
                {
                    BusinessId = business.Id,
                    TrialEndsAtUtc = DateTime.UtcNow.AddDays(trialDays)
                });

                if (businessType == "RETAIL")
                    RetailModule.AddBusinessDefaults(db, business.Id);

                await db.SaveChangesAsync();
                await transaction.CommitAsync();

                return Results.Created($"/api/businesses/{business.Id}/dashboard", new { business.Id });
            }
            catch (DbUpdateException exception) when (
                exception.InnerException is SqlException { Number: 2601 or 2627 })
            {
                await transaction.RollbackAsync();
                db.ChangeTracker.Clear();

                var created = await db.Businesses.AsNoTracking().FirstOrDefaultAsync(
                    x => x.CreatedByUserId == userId && x.CreationKey == requestKey);
                if (created is null)
                    throw;

                return Matches(created, request, name, country, currency, timeZone)
                    ? Results.Ok(new { created.Id })
                    : Results.Conflict(new { message = "Request key was already used for another business." });
            }
        });

        group.MapGet("/businesses/{businessId:long}/dashboard", async (
            long businessId, ClaimsPrincipal principal, AppDbContext db, IWebHostEnvironment environment) =>
        {
            var userId = CurrentUserId(principal);
            var roleCode = await db.BusinessUsers.AsNoTracking()
                .Where(x => x.BusinessId == businessId && x.UserId == userId)
                .Select(x => x.RoleCode)
                .SingleOrDefaultAsync();
            if (roleCode is not ("Owner" or "Manager" or "Cashier"))
                return Results.NotFound();

            var details = await (from business in db.Businesses.AsNoTracking()
                                 join type in db.BusinessTypes.AsNoTracking()
                                     on business.BusinessTypeId equals type.Id
                                 where business.Id == businessId
                                 select new
                                 {
                                     business.Id,
                                     business.Name,
                                     business.CurrencyCode,
                                     business.CountryCode,
                                     business.TimeZoneId,
                                     TypeCode = type.Code,
                                     TypeName = type.Name
                                 }).SingleAsync();

            var locations = await db.BusinessLocations.AsNoTracking()
                .Where(x => x.BusinessId == businessId && (roleCode == "Owner" || x.IsPrimary))
                .OrderBy(x => x.Id)
                .Select(x => new { x.Id, x.Name, x.Code })
                .ToListAsync();

            var subscription = await db.BusinessSubscriptions.AsNoTracking()
                .Where(x => x.BusinessId == businessId)
                .Select(x => new { x.PlanCode, x.Status, x.TrialEndsAtUtc,
                    x.PaidStartsAtUtc, x.PaidEndsAtUtc, x.PaidPlanVersionId })
                .SingleOrDefaultAsync();
            var accessStatus = subscription?.Status switch
            {
                "Active" when subscription.PaidStartsAtUtc <= DateTime.UtcNow &&
                    subscription.PaidEndsAtUtc > DateTime.UtcNow &&
                    subscription.PaidPlanVersionId.HasValue => "Active",
                "Active" => "Inactive",
                "Trial" when subscription.TrialEndsAtUtc > DateTime.UtcNow => "Trial",
                "Trial" => "TrialExpired",
                "Suspended" => "Suspended",
                _ => "Inactive"
            };
            var canOperate = accessStatus is "Active" or "Trial";

            var hasProducts = roleCode != "Cashier" && await db.RetailProducts.AnyAsync(x => x.BusinessId == businessId);
            var pendingApprovals = roleCode == "Owner" ? await db.RetailPurchaseOrders.CountAsync(x =>
                x.BusinessId == businessId && x.Status == "Draft") : 0;

            OwnerCashSalesToday? cashSalesToday = null;
            // Cash posting is currently development-only; never present these as
            // all-channel production sales or disclose financial data to cashiers.
            if (roleCode == "Owner" && environment.IsDevelopment() && details.TypeCode == "RETAIL")
            {
                var nowUtc = DateTime.UtcNow;
                var zone = TimeZoneInfo.FindSystemTimeZoneById(details.TimeZoneId);
                var localDate = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, zone).Date;
                var startUtc = TimeZoneInfo.ConvertTimeToUtc(localDate, zone);
                var endUtc = TimeZoneInfo.ConvertTimeToUtc(localDate.AddDays(1), zone);
                var today = db.RetailCashSales.AsNoTracking().Where(x =>
                    x.BusinessId == businessId && x.PostedAtUtc >= startUtc &&
                    x.PostedAtUtc < endUtc && x.PostedAtUtc <= nowUtc);
                var total = await today.SumAsync(x => (decimal?)x.Total) ?? 0m;
                var bills = await today.CountAsync();
                var recent = await db.RetailCashSales.AsNoTracking()
                    .Where(x => x.BusinessId == businessId && x.PostedAtUtc <= nowUtc)
                    .OrderByDescending(x => x.PostedAtUtc).ThenByDescending(x => x.Id)
                    .Take(5)
                    .Select(x => new CashReceiptSummary(x.Id, x.LocationId, x.Total, x.PostedAtUtc))
                    .ToListAsync();
                cashSalesToday = new OwnerCashSalesToday(total, bills, DateOnly.FromDateTime(localDate), recent);
            }

            OwnerStockAlerts? stockAlerts = null;
            if (roleCode == "Owner" && details.TypeCode == "RETAIL" &&
                await db.RetailProducts.AsNoTracking().AnyAsync(x =>
                    x.BusinessId == businessId && x.IsActive && x.ReorderLevel.HasValue))
            {
                // A threshold is explicit and stock must have been initialized.
                // An absent balance or unconfigured threshold never implies zero stock.
                var atRisk = from balance in db.RetailStockBalances.AsNoTracking()
                             join product in db.RetailProducts.AsNoTracking()
                                 on balance.ProductId equals product.Id
                             join location in db.BusinessLocations.AsNoTracking()
                                 on balance.LocationId equals location.Id
                             where balance.BusinessId == businessId &&
                                   product.BusinessId == businessId &&
                                   location.BusinessId == businessId &&
                                   product.IsActive && product.ReorderLevel.HasValue &&
                                   balance.Quantity <= product.ReorderLevel.Value
                             select new
                             {
                                 ProductId = product.Id,
                                 ProductName = product.Name,
                                 product.Sku,
                                 LocationId = location.Id,
                                 LocationName = location.Name,
                                 balance.Quantity,
                                 ReorderLevel = product.ReorderLevel.Value
                             };
                var alertCount = await atRisk.CountAsync();
                var zeroCount = await atRisk.CountAsync(x => x.Quantity == 0m);
                var firstFive = await atRisk
                    .OrderBy(x => x.Quantity != 0m)
                    .ThenBy(x => x.ProductName)
                    .ThenBy(x => x.LocationName)
                    .ThenBy(x => x.ProductId)
                    .Take(5).ToListAsync();
                var alertItems = firstFive.Select(x => new LowStockItem(x.ProductId,
                    x.ProductName, x.Sku, x.LocationId, x.LocationName,
                    x.Quantity, x.ReorderLevel)).ToList();
                stockAlerts = new OwnerStockAlerts(alertCount, zeroCount, alertItems);
            }

            return Results.Ok(new { hasProducts, pendingApprovals, cashSalesToday, stockAlerts,
                business = details, roleCode, locations, subscription, accessStatus,
                canOperate, cashSaleEnabled = environment.IsDevelopment() });
        });
    }

    private static BusinessMarket[] LoadMarkets()
    {
        using var stream = typeof(BusinessEndpoints).Assembly
            .GetManifestResourceStream("BusinessSaas.Api.Data.countries.json")
            ?? throw new InvalidOperationException("Country catalog is missing.");

        return JsonSerializer.Deserialize<BusinessMarket[]>(stream,
            new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new InvalidOperationException("Country catalog is invalid.");
    }

    private static bool Matches(Business business, CreateBusinessRequest request,
        string name, string country, string currency, string timeZone) =>
        business.BusinessTypeId == request.BusinessTypeId &&
        business.Name == name &&
        business.CountryCode == country &&
        business.CurrencyCode == currency &&
        business.TimeZoneId == timeZone;

    private static long CurrentUserId(ClaimsPrincipal principal) =>
        long.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier)!);

    private sealed record CreateBusinessRequest(
        string? Name, long BusinessTypeId, string? CountryCode,
        string? CurrencyCode, string? TimeZoneId, string? RequestKey);
}
