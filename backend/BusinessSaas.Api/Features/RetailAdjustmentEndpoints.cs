using System.Data;
using System.Security.Claims;
using BusinessSaas.Api.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace BusinessSaas.Api.Features;

public static class RetailAdjustmentEndpoints
{
    private const int PageSize = 20;

    public static void MapRetailAdjustmentEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/businesses/{businessId:long}/stock/locations/{locationId:long}/adjustments")
            .RequireAuthorization();

        group.MapGet("", async (long businessId, long locationId, int? page,
            ClaimsPrincipal user, AppDbContext db) =>
        {
            var denied = await RetailStockEndpoints.CheckLocation(db, user, businessId, locationId, false);
            if (denied is not null) return denied;

            var currentPage = page ?? 1;
            if (currentPage is < 1 or > 100000)
                return Results.BadRequest(new { message = "Invalid page." });

            var query = from adjustment in db.RetailStockAdjustments.AsNoTracking()
                        join product in db.RetailProducts.AsNoTracking()
                            on adjustment.ProductId equals product.Id
                        where adjustment.BusinessId == businessId
                              && adjustment.LocationId == locationId
                              && product.BusinessId == businessId
                        select new
                        {
                            adjustment.Id,
                            adjustment.ProductId,
                            product.Name,
                            product.Sku,
                            adjustment.SystemQuantity,
                            adjustment.CountedQuantity,
                            Difference = adjustment.CountedQuantity - adjustment.SystemQuantity,
                            adjustment.Reason,
                            adjustment.Remarks,
                            adjustment.Status,
                            adjustment.RequestedByUserId,
                            adjustment.RequestedAtUtc,
                            adjustment.ReviewedByUserId,
                            adjustment.ReviewedAtUtc,
                            adjustment.ReviewRemarks,
                            adjustment.PostedByUserId,
                            adjustment.PostedAtUtc
                        };

            var total = await query.CountAsync();
            var items = await query.OrderByDescending(x => x.Id)
                .Skip((currentPage - 1) * PageSize).Take(PageSize).ToListAsync();
            return Results.Ok(new { items, total, page = currentPage, pageSize = PageSize });
        });

        group.MapPost("", async (long businessId, long locationId, CreateAdjustment request,
            ClaimsPrincipal user, AppDbContext db) =>
        {
            var denied = await RetailStockEndpoints.CheckLocation(db, user, businessId, locationId, true);
            if (denied is not null) return denied;

            var reason = request.Reason?.Trim() ?? "";
            var remarks = request.Remarks?.Trim();
            if (request.ProductId <= 0 || !IsQuantity(request.SystemQuantity) ||
                !IsQuantity(request.CountedQuantity) ||
                reason is not ("Physical Count" or "Damage" or "Expiry" or "Loss" or "Other") ||
                remarks?.Length > 500)
                return Results.BadRequest(new { message = "Enter valid quantities, reason and remarks." });

            if (request.SystemQuantity == request.CountedQuantity)
                return Results.BadRequest(new { message = "No difference found in the stock count." });

            if (!await db.RetailProducts.AnyAsync(x => x.Id == request.ProductId &&
                x.BusinessId == businessId && x.IsActive))
                return Results.NotFound();

            var current = await db.RetailStockBalances.AsNoTracking()
                .Where(x => x.BusinessId == businessId && x.LocationId == locationId
                    && x.ProductId == request.ProductId)
                .Select(x => (decimal?)x.Quantity).SingleOrDefaultAsync();

            if (current is null)
                return Results.Conflict(new { message = "Post opening stock before creating an adjustment." });
            if (current.Value != request.SystemQuantity)
                return Results.Conflict(new { message = "Stock changed. Reload and recount before submitting." });

            var adjustment = new RetailStockAdjustment
            {
                BusinessId = businessId,
                LocationId = locationId,
                ProductId = request.ProductId,
                SystemQuantity = current.Value,
                CountedQuantity = request.CountedQuantity,
                Reason = reason,
                Remarks = remarks,
                RequestedByUserId = UserId(user)
            };
            db.RetailStockAdjustments.Add(adjustment);
            await db.SaveChangesAsync();
            return Results.Created(
                $"/api/businesses/{businessId}/stock/locations/{locationId}/adjustments/{adjustment.Id}",
                new { adjustment.Id, adjustment.Status, adjustment.SystemQuantity,
                    adjustment.CountedQuantity });
        });

        group.MapPost("/{adjustmentId:long}/review", async (long businessId, long locationId,
            long adjustmentId, ReviewAdjustment request, ClaimsPrincipal user, AppDbContext db) =>
        {
            var denied = await CheckOwner(db, user, businessId, locationId);
            if (denied is not null) return denied;

            var remarks = request.Remarks?.Trim();
            if (remarks?.Length > 500 || (!request.Approve && string.IsNullOrWhiteSpace(remarks)))
                return Results.BadRequest(new { message = "Provide a reason when rejecting an adjustment." });

            var updated = await db.RetailStockAdjustments
                .Where(x => x.BusinessId == businessId && x.LocationId == locationId
                    && x.Id == adjustmentId && x.Status == "Pending Approval")
                .ExecuteUpdateAsync(set => set
                    .SetProperty(x => x.Status, request.Approve ? "Approved" : "Rejected")
                    .SetProperty(x => x.ReviewedByUserId, UserId(user))
                    .SetProperty(x => x.ReviewedAtUtc, DateTime.UtcNow)
                    .SetProperty(x => x.ReviewRemarks, remarks));
            if (updated != 0)
                return Results.Ok(new { id = adjustmentId,
                    status = request.Approve ? "Approved" : "Rejected" });

            if (!await db.RetailStockAdjustments.AsNoTracking().AnyAsync(x =>
                    x.Id == adjustmentId && x.BusinessId == businessId && x.LocationId == locationId))
                return Results.NotFound();
            return Results.Conflict(new { message = "This adjustment has already been reviewed." });
        });

        group.MapPost("/{adjustmentId:long}/post", async (long businessId, long locationId,
            long adjustmentId, ClaimsPrincipal user, AppDbContext db) =>
        {
            var denied = await CheckOwner(db, user, businessId, locationId);
            if (denied is not null) return denied;

            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            try
            {
                var adjustment = await db.RetailStockAdjustments.AsNoTracking()
                    .SingleOrDefaultAsync(x => x.Id == adjustmentId &&
                        x.BusinessId == businessId && x.LocationId == locationId);
                if (adjustment is null) return Results.NotFound();
                if (adjustment.Status == "Posted")
                    return Results.Ok(new { adjustment.Id, adjustment.Status });
                if (adjustment.Status != "Approved")
                    return Results.Conflict(new { message = "Approve this adjustment before posting." });

                if (!await db.RetailProducts.AsNoTracking().AnyAsync(x =>
                    x.Id == adjustment.ProductId && x.BusinessId == businessId && x.IsActive))
                    return Results.Conflict(new { message = "This product is no longer active." });

                var postedAt = DateTime.UtcNow;
                var balanceAfter = await StockPosting.ApplyAsync(db, businessId, locationId,
                    adjustment.ProductId, adjustment.CountedQuantity - adjustment.SystemQuantity,
                    "Adjustment", adjustment.Id, UserId(user), adjustment.SystemQuantity);
                if (balanceAfter is null)
                    return Results.Conflict(new { message = "Stock changed since the count. Create a new adjustment." });

                var posted = await db.RetailStockAdjustments
                    .Where(x => x.Id == adjustmentId && x.BusinessId == businessId &&
                        x.LocationId == locationId && x.Status == "Approved")
                    .ExecuteUpdateAsync(set => set
                        .SetProperty(x => x.Status, "Posted")
                        .SetProperty(x => x.PostedByUserId, UserId(user))
                        .SetProperty(x => x.PostedAtUtc, postedAt));
                if (posted != 1)
                    return Results.Conflict(new { message = "This adjustment was already processed." });

                await transaction.CommitAsync();
                return Results.Ok(new { adjustment.Id, status = "Posted",
                    adjustment.SystemQuantity, adjustment.CountedQuantity });
            }
            catch (SqlException ex) when (ex.Number is 1205 or 1222 or 3960)
            {
                return Results.Conflict(new { message = "Stock is being updated. Refresh and retry." });
            }
            catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
            {
                return Results.Conflict(new { message = "This stock movement was already posted." });
            }
        });
    }

    private static bool IsQuantity(decimal value) =>
        value is >= 0m and <= 9999999999.999m &&
        decimal.Round(value, 3) == value;

    private static long UserId(ClaimsPrincipal user) =>
        long.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);

    private static async Task<IResult?> CheckOwner(
        AppDbContext db, ClaimsPrincipal user, long businessId, long locationId)
    {
        var denied = await RetailStockEndpoints.CheckLocation(db, user, businessId, locationId, true);
        if (denied is not null) return denied;
        var owner = await db.BusinessUsers.AsNoTracking()
            .AnyAsync(x => x.BusinessId == businessId && x.UserId == UserId(user) && x.RoleCode == "Owner");
        return owner ? null : Results.NotFound();
    }

    private sealed record CreateAdjustment(long ProductId, decimal SystemQuantity,
        decimal CountedQuantity, string? Reason, string? Remarks);
    private sealed record ReviewAdjustment(bool Approve, string? Remarks);
}
