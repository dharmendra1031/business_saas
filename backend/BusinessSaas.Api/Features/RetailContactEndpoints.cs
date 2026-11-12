using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using BusinessSaas.Api.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace BusinessSaas.Api.Features;

public static class RetailContactEndpoints
{
    private const int PageSize = 20;

    public static void MapRetailContactEndpoints(this WebApplication app)
    {
        var suppliers = app.MapGroup("/api/businesses/{businessId:long}/suppliers")
            .RequireAuthorization();

        suppliers.MapGet("", async (long businessId, string? search, int? page,
            ClaimsPrincipal user, AppDbContext db) =>
        {
            var denied = await RetailMasterEndpoints.CheckAccess(db, user, businessId, false);
            if (denied is not null) return denied;
            var currentPage = page ?? 1;
            var term = search?.Trim().ToUpperInvariant() ?? "";
            if (currentPage is < 1 or > 100000 || term.Length > 100)
                return Results.BadRequest(new { message = "Invalid search or page." });

            var query = db.RetailSuppliers.AsNoTracking().Where(x => x.BusinessId == businessId);
            if (term.Length > 0) query = query.Where(x =>
                x.NameKey.Contains(term) || x.Phone.Contains(term) || x.Email.ToUpper().Contains(term));

            var total = await query.CountAsync();
            var items = await query.OrderBy(x => x.NameKey).ThenBy(x => x.Id)
                .Skip((currentPage - 1) * PageSize).Take(PageSize)
                .Select(x => new { x.Id, x.Name, x.Phone, x.Email, x.IsActive })
                .ToListAsync();
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
            catch (DbUpdateException ex) when (Duplicate(ex))
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
            catch (DbUpdateException ex) when (Duplicate(ex))
            {
                return Results.Conflict(new { message = "Supplier name already exists." });
            }
            return Results.Ok(new { supplier.Id, supplier.Name, supplier.Phone, supplier.Email, supplier.IsActive });
        });

        var customers = app.MapGroup("/api/businesses/{businessId:long}/customers")
            .RequireAuthorization();

        customers.MapGet("", async (long businessId, string? search, int? page,
            ClaimsPrincipal user, AppDbContext db) =>
        {
            var denied = await RetailMasterEndpoints.CheckAccess(db, user, businessId, false);
            if (denied is not null) return denied;
            var currentPage = page ?? 1;
            var term = search?.Trim().ToUpperInvariant() ?? "";
            if (currentPage is < 1 or > 100000 || term.Length > 100)
                return Results.BadRequest(new { message = "Invalid search or page." });

            var query = db.RetailCustomers.AsNoTracking().Where(x => x.BusinessId == businessId);
            if (term.Length > 0) query = query.Where(x =>
                x.NameKey.Contains(term) || x.Phone.Contains(term) || x.Email.ToUpper().Contains(term));

            var total = await query.CountAsync();
            var items = await query.OrderBy(x => x.NameKey).ThenBy(x => x.Id)
                .Skip((currentPage - 1) * PageSize).Take(PageSize)
                .Select(x => new { x.Id, x.Name, x.Phone, x.Email, x.IsActive })
                .ToListAsync();
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
                Email = Clean(request.Email)
            };
            db.RetailCustomers.Add(customer);
            await db.SaveChangesAsync();
            return Results.Created($"/api/businesses/{businessId}/customers/{customer.Id}",
                new { customer.Id, customer.Name, customer.Phone, customer.Email, customer.IsActive });
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
            customer.IsActive = request.IsActive.Value;
            await db.SaveChangesAsync();
            return Results.Ok(new { customer.Id, customer.Name, customer.Phone, customer.Email, customer.IsActive });
        });
    }

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
        if (supplier && phone.Length == 0 && email.Length == 0)
            return "Enter at least a phone number or email for this supplier.";
        return null;
    }

    private static string Clean(string? text) => text?.Trim() ?? "";

    private static bool Duplicate(DbUpdateException ex) =>
        ex.InnerException is SqlException { Number: 2601 or 2627 };

    private sealed record ContactRequest(string? Name, string? Phone, string? Email, bool? IsActive);
}
