using System.Data;
using System.Globalization;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using BusinessSaas.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace BusinessSaas.Api.Features;

public static class RetailContactEndpoints
{
    private const int PageSize = 20;

    public static void MapRetailContactEndpoints(this WebApplication app)
    {
        var suppliers = app.MapGroup("/api/businesses/{businessId:long}/suppliers")
            .RequireAuthorization();

        suppliers.MapGet("", async (long businessId, string? search, int? page, bool? activeOnly,
            ClaimsPrincipal user, AppDbContext db, CancellationToken cancellationToken) =>
        {
            var denied = await RetailMasterEndpoints.CheckAccess(db, user, businessId, false, cancellationToken);
            if (denied is not null) return denied;
            var currentPage = page ?? 1;
            var term = search?.Trim().ToUpperInvariant() ?? "";
            if (currentPage is < 1 or > 100000 || term.Length > 100)
                return Results.BadRequest(new { message = "Invalid search or page." });

            var query = db.RetailSuppliers.AsNoTracking().Where(x => x.BusinessId == businessId);
            if (activeOnly == true) query = query.Where(x => x.IsActive);
            if (term.Length > 0) query = query.Where(x =>
                x.NameKey.Contains(term) || x.Phone.Contains(term) || x.Email.ToUpper().Contains(term));

            var total = await query.CountAsync(cancellationToken);
            var items = await query.OrderBy(x => x.NameKey).ThenBy(x => x.Id)
                .Skip((currentPage - 1) * PageSize).Take(PageSize)
                .Select(x => new { x.Id, x.Name, x.Phone, x.Email, x.IsActive })
                .ToListAsync(cancellationToken);
            return Results.Ok(new { items, total, page = currentPage, pageSize = PageSize });
        });

        suppliers.MapPost("", async (long businessId, ContactRequest request,
            ClaimsPrincipal user, AppDbContext db) =>
        {
            var denied = await RetailMasterEndpoints.CheckAccess(db, user, businessId, true);
            if (denied is not null) return denied;
            var error = Validate(request, true);
            if (error is not null) return Results.BadRequest(new { message = error });

            var name = request.Name!.Trim();
            var key = name.ToUpperInvariant();
            if (await db.RetailSuppliers.AnyAsync(x => x.BusinessId == businessId && x.NameKey == key))
                return Results.Conflict(new { message = "Supplier name already exists." });

            var supplier = new RetailSupplier
            {
                BusinessId = businessId, Name = name, NameKey = key,
                Phone = Clean(request.Phone), Email = Clean(request.Email)
            };
            db.RetailSuppliers.Add(supplier);
            try { await db.SaveChangesAsync(); }
            catch (DbUpdateException ex) when (RetailModule.IsDuplicateKey(ex))
            {
                return Results.Conflict(new { message = "Supplier name already exists." });
            }
            return Results.Created($"/api/businesses/{businessId}/suppliers/{supplier.Id}",
                new { supplier.Id, supplier.Name, supplier.Phone, supplier.Email, supplier.IsActive });
        });

        suppliers.MapPut("/{supplierId:long}", async (long businessId, long supplierId,
            ContactRequest request, ClaimsPrincipal user, AppDbContext db) =>
        {
            var denied = await RetailMasterEndpoints.CheckAccess(db, user, businessId, true);
            if (denied is not null) return denied;
            var supplier = await db.RetailSuppliers.SingleOrDefaultAsync(x =>
                x.BusinessId == businessId && x.Id == supplierId);
            if (supplier is null) return Results.NotFound();

            var error = Validate(request, true);
            if (error is not null || request.IsActive is null)
                return Results.BadRequest(new { message = error ?? "Select a supplier status." });

            var name = request.Name!.Trim();
            var key = name.ToUpperInvariant();
            if (await db.RetailSuppliers.AnyAsync(x => x.BusinessId == businessId &&
                x.Id != supplierId && x.NameKey == key))
                return Results.Conflict(new { message = "Supplier name already exists." });

            supplier.Name = name;
            supplier.NameKey = key;
            supplier.Phone = Clean(request.Phone);
            supplier.Email = Clean(request.Email);
            supplier.IsActive = request.IsActive.Value;
            try { await db.SaveChangesAsync(); }
            catch (DbUpdateException ex) when (RetailModule.IsDuplicateKey(ex))
            {
                return Results.Conflict(new { message = "Supplier name already exists." });
            }
            return Results.Ok(new { supplier.Id, supplier.Name, supplier.Phone, supplier.Email, supplier.IsActive });
        });

        var customers = app.MapGroup("/api/businesses/{businessId:long}/customers")
            .RequireAuthorization();

        customers.MapGet("", async (long businessId, string? search, int? page,
            ClaimsPrincipal user, AppDbContext db, CancellationToken cancellationToken) =>
        {
            var denied = await RetailMasterEndpoints.CheckAccess(db, user, businessId, false, cancellationToken);
            if (denied is not null) return denied;
            var currentPage = page ?? 1;
            var term = search?.Trim().ToUpperInvariant() ?? "";
            if (currentPage is < 1 or > 100000 || term.Length > 100)
                return Results.BadRequest(new { message = "Invalid search or page." });

            var isOwner = await RetailBillingSettingsEndpoints.OwnerId(db, businessId, user, cancellationToken) is not null;
            var query = db.RetailCustomers.AsNoTracking().Where(x => x.BusinessId == businessId);
            if (term.Length > 0) query = query.Where(x =>
                x.NameKey.Contains(term) || x.Phone.Contains(term) || x.Email.ToUpper().Contains(term));

            var total = await query.CountAsync(cancellationToken);
            var items = await query.OrderBy(x => x.NameKey).ThenBy(x => x.Id)
                .Skip((currentPage - 1) * PageSize).Take(PageSize)
                .Select(x => new { x.Id, x.Name, x.Phone, x.Email, x.Address, x.IsActive,
                    CreditLimit = isOwner ? (decimal?)x.CreditLimit : null })
                .ToListAsync(cancellationToken);
            return Results.Ok(new { items, total, page = currentPage, pageSize = PageSize });
        });

        customers.MapPost("", async (long businessId, ContactRequest request,
            ClaimsPrincipal user, AppDbContext db) =>
        {
            var denied = await RetailMasterEndpoints.CheckAccess(db, user, businessId, true);
            if (denied is not null) return denied;
            var error = Validate(request, false);
            if (error is not null) return Results.BadRequest(new { message = error });

            var customer = new RetailCustomer
            {
                BusinessId = businessId,
                Name = request.Name!.Trim(),
                NameKey = request.Name!.Trim().ToUpperInvariant(),
                Phone = Clean(request.Phone),
                Email = Clean(request.Email),
                Address = Clean(request.Address)
            };
            db.RetailCustomers.Add(customer);
            await db.SaveChangesAsync();
            return Results.Created($"/api/businesses/{businessId}/customers/{customer.Id}",
                new { customer.Id, customer.Name, customer.Phone, customer.Email, customer.Address, customer.IsActive });
        });

        customers.MapPut("/{customerId:long}", async (long businessId, long customerId,
            ContactRequest request, ClaimsPrincipal user, AppDbContext db) =>
        {
            var denied = await RetailMasterEndpoints.CheckAccess(db, user, businessId, true);
            if (denied is not null) return denied;
            var customer = await db.RetailCustomers.SingleOrDefaultAsync(x =>
                x.BusinessId == businessId && x.Id == customerId);
            if (customer is null) return Results.NotFound();

            var error = Validate(request, false);
            if (error is not null || request.IsActive is null)
                return Results.BadRequest(new { message = error ?? "Select a customer status." });

            customer.Name = request.Name!.Trim();
            customer.NameKey = customer.Name.ToUpperInvariant();
            customer.Phone = Clean(request.Phone);
            customer.Email = Clean(request.Email);
            if (request.Address is not null) customer.Address = Clean(request.Address);
            customer.IsActive = request.IsActive.Value;
            await db.SaveChangesAsync();
            return Results.Ok(new { customer.Id, customer.Name, customer.Phone, customer.Email, customer.Address, customer.IsActive });
        });
        CreditCollections.MapCreditCollections(customers);
        customers.MapGet("/{customerId:long}/credit-ledger", async (long businessId, long customerId,
            ClaimsPrincipal user, AppDbContext db, IConfiguration config, IWebHostEnvironment environment, CancellationToken cancellationToken, int page = 1) =>
        {
            if (await RetailBillingSettingsEndpoints.OwnerId(db, businessId, user, cancellationToken) is null)
                return Results.NotFound();
            if (page is < 1 or > 100000) return Results.BadRequest(new { message = "Choose a valid ledger page." });
            var customer = await db.RetailCustomers.AsNoTracking().SingleOrDefaultAsync(x =>
                x.BusinessId == businessId && x.Id == customerId, cancellationToken);
            if (customer is null) return Results.NotFound();

            const int size = 20;
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
            var invoices = CreditCollections.Invoices(db, businessId, customerId);
            var billed = await invoices.GroupBy(x => x.CurrencyCode).Select(x => new {
                CurrencyCode = x.Key, Debit = x.Sum(s => s.Total), InvoiceCount = x.Count()
            }).ToListAsync(cancellationToken);
            var collections = await (from payment in CreditCollections.CustomerPayments(db, businessId, customerId)
                                     join sale in invoices on payment.SaleId equals sale.Id
                                     group payment by sale.CurrencyCode into amounts
                                     select new { CurrencyCode = amounts.Key, Paid = amounts.Sum(p => p.Amount) })
                .ToDictionaryAsync(x => x.CurrencyCode, x => x.Paid, cancellationToken);
            var items = await invoices.OrderByDescending(x => x.Id).Skip((page - 1) * size)
                .Take(size + 1).Select(x => new { x.Id, x.LocationId, x.LocationName, x.CurrencyCode,
                    Debit = x.Total, x.CreditDueDate, PostedAtUtc = DateTime.SpecifyKind(x.PostedAtUtc, DateTimeKind.Utc),
                    Paid = db.RetailCreditPayments.Where(p => p.BusinessId == businessId && p.SaleId == x.Id)
                        .Sum(p => (decimal?)p.Amount) ?? 0m
                }).ToListAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);
            return Results.Ok(new { customerId, customer.Name, customer.CreditLimit,
                canCollect = environment.IsDevelopment() && config.GetValue<bool>("RetailCredit:CollectionsEnabled") &&
                    await RetailMasterEndpoints.CheckAccess(db, user, businessId, true, cancellationToken) is null,
                totals = billed.Select(x => new { x.CurrencyCode, x.Debit, x.InvoiceCount,
                    Paid = collections.GetValueOrDefault(x.CurrencyCode),
                    Outstanding = x.Debit - collections.GetValueOrDefault(x.CurrencyCode) }),
                items = items.Take(size).Select(x => new { x.Id, x.LocationId, x.LocationName, x.CurrencyCode,
                    x.Debit, x.Paid, Outstanding = x.Debit - x.Paid, x.CreditDueDate, x.PostedAtUtc }),
                hasMore = items.Count > size });
        });

        customers.MapPut("/{customerId:long}/credit-limit", async (long businessId, long customerId,
            CreditLimitRequest request, ClaimsPrincipal user, AppDbContext db) =>
        {
            var actor = await RetailBillingSettingsEndpoints.OwnerId(db, businessId, user);
            if (actor is null) return Results.NotFound();
            var denied = await RetailMasterEndpoints.CheckAccess(db, user, businessId, true);
            if (denied is not null) return denied;
            if (!ValidCreditLimit(request.Limit) || !ValidCreditLimit(request.ExpectedLimit) ||
                request.Reason?.Trim().Length is not (>= 10 and <= 200))
                return Results.BadRequest(new { message = "Enter a non-negative credit limit with up to two decimals and a 10–200 character reason." });

            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            try
            {
                var customer = await db.RetailCustomers.SingleOrDefaultAsync(x =>
                    x.BusinessId == businessId && x.Id == customerId);
                if (customer is null) return Results.NotFound();
                if (customer.CreditLimit != request.ExpectedLimit)
                    return Results.Conflict(new { message = "Credit limit changed. Reload the customer before saving." });
                if (!customer.IsActive && request.Limit > 0)
                    return Results.Conflict(new { message = "Activate this customer before setting a positive credit limit." });
                if (customer.CreditLimit == request.Limit)
                    return Results.Ok(new { customer.Id, customer.CreditLimit });

                var previous = customer.CreditLimit;
                customer.CreditLimit = request.Limit;
                db.PlatformAuditEvents.Add(new PlatformAuditEvent {
                    ActorUserId = actor.Value, Action = "Customer.CreditLimitChanged",
                    TargetType = "RetailCustomer", TargetId = customer.Id,
                    PreviousValue = previous.ToString("0.00", CultureInfo.InvariantCulture),
                    NewValue = request.Limit.ToString("0.00", CultureInfo.InvariantCulture),
                    Reason = request.Reason.Trim()
                });
                await db.SaveChangesAsync();
                await tx.CommitAsync();
                return Results.Ok(new { customer.Id, customer.CreditLimit });
            }
            catch (Exception error) when (StockPosting.IsSqlConflict(error))
            {
                return Results.Conflict(new { message = "Customer is being updated. Reload and retry." });
            }
        });
    }

    private static bool ValidCreditLimit(decimal value) =>
        value is >= 0 and <= 9999999999.99m && decimal.Round(value, 2) == value;

    private sealed record CreditLimitRequest(decimal Limit, decimal ExpectedLimit, string? Reason);

    private static string? Validate(ContactRequest request, bool supplier)
    {
        var name = Clean(request.Name);
        var phone = Clean(request.Phone);
        var email = Clean(request.Email);
        if (name.Length is < 2 or > 120) return "Name must be 2–120 characters.";
        if (phone.Length > 25 || phone.Any(c =>
            !char.IsAsciiDigit(c) && c is not ('+' or '-' or ' ' or '(' or ')')))
            return "Phone must contain at most 25 digits or phone punctuation.";
        if (email.Length > 160 || (email.Length > 0 && !new EmailAddressAttribute().IsValid(email)))
            return "Enter a valid email address.";
        if (!supplier && request.Address is not null &&
            (request.Address.Length > 300 || request.Address.Any(char.IsControl)))
            return "Address must be at most 300 characters and contain no control characters.";
        if (supplier && phone.Length == 0 && email.Length == 0)
            return "Enter at least a phone number or email for this supplier.";
        return null;
    }

    private static string Clean(string? text) => text?.Trim() ?? "";

    private sealed record ContactRequest(string? Name, string? Phone, string? Email, bool? IsActive, string? Address = null);
}
