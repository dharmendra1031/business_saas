using System.Data.Common;
using System.Security.Claims;
using BusinessSaas.Api.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace BusinessSaas.Api.Features;

public static class RetailStockEndpoints
{
    private const int PageSize = 20;

    public static void MapRetailStockEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/businesses/{businessId:long}/stock")
            .RequireAuthorization();

        group.MapGet("/locations/{locationId:long}", async (long businessId, long locationId,
            string? search, int? page, ClaimsPrincipal user, AppDbContext db) =>
        {
            var denied = await CheckLocation(db, user, businessId, locationId, false);
            if (denied is not null) return denied;

            var currentPage = page ?? 1;
            var term = search?.Trim().ToUpperInvariant() ?? "";
            if (currentPage is < 1 or > 100000 || term.Length > 100)
                return Results.BadRequest(new { message = "Invalid page or search." });

            var products = db.RetailProducts.AsNoTracking()
                .Where(x => x.BusinessId == businessId);
            if (term.Length > 0)
                products = products.Where(x => x.Name.ToUpper().Contains(term) ||
                    x.Sku.Contains(term) || x.Barcode != null && x.Barcode.Contains(term));

            var total = await products.CountAsync();
            var pageQuery = products.OrderBy(x => x.Name).ThenBy(x => x.Id)
                .Skip((currentPage - 1) * PageSize).Take(PageSize);

            var items = await (from product in pageQuery
                               join balance in db.RetailStockBalances.AsNoTracking()
                                       .Where(x => x.BusinessId == businessId && x.LocationId == locationId)
                                   on product.Id equals balance.ProductId into balances
                               from balance in balances.DefaultIfEmpty()
                               select new
                               {
                                   product.Id,
                                   product.Name,
                                   product.Sku,
                                   product.IsActive,
                                   Quantity = balance == null ? 0m : balance.Quantity,
                                   HasStockRecord = balance != null
                               }).ToListAsync();

            return Results.Ok(new { items, total, page = currentPage, pageSize = PageSize });
        });

        group.MapGet("/locations/{locationId:long}/movements", async (long businessId,
            long locationId, long? productId, int? page, ClaimsPrincipal user, AppDbContext db) =>
        {
            var denied = await CheckLocation(db, user, businessId, locationId, false);
            if (denied is not null) return denied;
            var currentPage = page ?? 1;
            if (currentPage is < 1 or > 100000 || productId is <= 0)
                return Results.BadRequest(new { message = "Invalid page or product." });

            var query = from movement in db.RetailStockMovements.AsNoTracking()
                        join product in db.RetailProducts.AsNoTracking()
                            on movement.ProductId equals product.Id
                        join actor in db.Users.AsNoTracking()
                            on movement.ActorUserId equals actor.Id
                        where movement.BusinessId == businessId &&
                              movement.LocationId == locationId &&
                              product.BusinessId == businessId
                        select new
                        {
                            movement.Id,
                            movement.ProductId,
                            product.Name,
                            product.Sku,
                            movement.QuantityChange,
                            movement.BalanceAfter,
                            movement.SourceType,
                            movement.SourceId,
                            movement.PostedAtUtc,
                            Actor = actor.Email
                        };
            if (productId is > 0) query = query.Where(x => x.ProductId == productId);

            var total = await query.CountAsync();
            var items = await query.OrderByDescending(x => x.Id)
                .Skip((currentPage - 1) * PageSize).Take(PageSize).ToListAsync();
            return Results.Ok(new { items, total, page = currentPage, pageSize = PageSize });
        });

        group.MapPost("/locations/{locationId:long}/opening", async (long businessId,
            long locationId, OpeningStockRequest request, ClaimsPrincipal user, AppDbContext db) =>
        {
            var denied = await CheckLocation(db, user, businessId, locationId, true);
            if (denied is not null) return denied;

            if (request.ProductId <= 0 || request.Quantity is < 0 or > 9999999999.999m ||
                decimal.Round(request.Quantity, 3) != request.Quantity)
                return Results.BadRequest(new { message = "Quantity must be 0 or more, with at most 3 decimal places." });

            if (!await db.RetailProducts.AnyAsync(x =>
                    x.BusinessId == businessId && x.Id == request.ProductId && x.IsActive))
                return Results.NotFound();

            if (await db.RetailStockBalances.AnyAsync(x => x.BusinessId == businessId &&
                x.LocationId == locationId && x.ProductId == request.ProductId))
                return Results.Conflict(new { message = "Stock already exists. Use a reviewed adjustment workflow." });

            var actorUserId = long.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var balance = new RetailStockBalance
            {
                BusinessId = businessId, LocationId = locationId,
                ProductId = request.ProductId, Quantity = request.Quantity
            };
            var movement = new RetailStockMovement
            {
                BusinessId = businessId, LocationId = locationId,
                ProductId = request.ProductId, ActorUserId = actorUserId,
                SourceType = "Opening", QuantityChange = request.Quantity,
                BalanceAfter = request.Quantity
            };

            await using var transaction = await db.Database.BeginTransactionAsync();
            db.RetailStockBalances.Add(balance);
            db.RetailStockMovements.Add(movement);
            try
            {
                await db.SaveChangesAsync();
                await transaction.CommitAsync();
            }
            catch (DbUpdateException ex) when (IsDuplicate(ex))
            {
                await transaction.RollbackAsync();
                return Results.Conflict(new { message = "Opening stock already posted." });
            }

            return Results.Created(
                $"/api/businesses/{businessId}/stock/locations/{locationId}/movements?productId={request.ProductId}",
                new { movement.Id, movement.ProductId, movement.QuantityChange, movement.BalanceAfter });
        });
    }

    internal static async Task<IResult?> CheckLocation(AppDbContext db,
        ClaimsPrincipal user, long businessId, long locationId, bool write)
    {
        var denied = await RetailMasterEndpoints.CheckAccess(db, user, businessId, write);
        if (denied is not null) return denied;

        var userId = long.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var role = await db.BusinessUsers.AsNoTracking()
            .Where(x => x.BusinessId == businessId && x.UserId == userId)
            .Select(x => x.RoleCode).SingleOrDefaultAsync();

        var allowed = await db.BusinessLocations.AsNoTracking().AnyAsync(x =>
            x.BusinessId == businessId && x.Id == locationId &&
            (role == "Owner" || x.IsPrimary));
        return allowed ? null : Results.NotFound();
    }

    private static bool IsDuplicate(DbUpdateException ex) =>
        ex.InnerException is SqlException { Number: 2601 or 2627 } ||
        ex.InnerException is DbException { ErrorCode: 19 };

    private sealed record OpeningStockRequest(long ProductId, decimal Quantity);
}
