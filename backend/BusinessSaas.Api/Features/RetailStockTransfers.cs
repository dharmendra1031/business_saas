using System.Data;
using System.Security.Claims;
using BusinessSaas.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace BusinessSaas.Api.Features;

internal static class RetailStockTransfers
{
    internal sealed record Item(long ProductId, decimal Quantity);
    internal sealed record CreateRequest(long FromLocationId, long ToLocationId, string? Reason, string? RequestKey, List<Item>? Lines, bool DispatchNow = false, bool SaveDraft = false);
    internal sealed record ReceiveRequest(long LineId, decimal Quantity, string? Kind, string? Reason, string? RequestKey, decimal ExpectedRemaining);

    internal static void Map(RouteGroupBuilder group)
    {
        var transfers = group.MapGroup("/transfers");
        transfers.MapGet("/options", Options);
        transfers.MapGet("/in-transit", InTransit);
        transfers.MapGet("", List);
        transfers.MapGet("/{id:long}", Read);
        transfers.MapPost("", Create);
        transfers.MapPost("/{id:long}/submit", Submit);
        transfers.MapPost("/{id:long}/approve", Approve);
        transfers.MapPost("/{id:long}/dispatch", Dispatch);
        transfers.MapPost("/{id:long}/cancel", Cancel);
        transfers.MapPost("/{id:long}/receive", Receive);
    }

    private static IQueryable<RetailStockTransfer> Visible(AppDbContext db, long businessId, string role) =>
        db.RetailStockTransfers.Where(x => x.BusinessId == businessId &&
            db.BusinessLocations.Any(l => l.Id == x.FromLocationId && l.BusinessId == businessId) &&
            db.BusinessLocations.Any(l => l.Id == x.ToLocationId && l.BusinessId == businessId) &&
            (role == "Owner" || db.BusinessLocations.Any(l => l.BusinessId == businessId && l.IsPrimary &&
                (l.Id == x.FromLocationId || l.Id == x.ToLocationId))));

    private static async Task<IResult?> WriteAccess(AppDbContext db, ClaimsPrincipal user, long businessId,
        IWebHostEnvironment environment, CancellationToken ct, long? locationId = null, bool ownerOnly = false)
    {
        var denied = await RetailMasterEndpoints.CheckAccess(db, user, businessId, true, ct, locationId, ownerOnly);
        if (denied is not null) return denied;
        if (!environment.IsDevelopment() || !await PlatformModuleReleaseEndpoints.Available(db, environment, "INVENTORY"))
            return Results.Conflict(new { message = "Stock transfer posting is not released." });
        if (await PlatformStatusEndpoints.ActiveMaintenance(db) is not null)
            return Results.Json(new { message = "Stock transfer is unavailable during maintenance." }, statusCode: 503);
        return null;
    }

    private static async Task<IResult> Options(long businessId, ClaimsPrincipal user, AppDbContext db,
        IWebHostEnvironment environment, CancellationToken ct)
    {
        var role = await RetailMasterEndpoints.ReadRole(db, user, businessId, ct);
        if (role is null) return Results.NotFound();
        var locations = await db.BusinessLocations.AsNoTracking().Where(x => x.BusinessId == businessId)
            .OrderBy(x => x.Name).Select(x => new { x.Id, x.Name, canAccess = role == "Owner" || x.IsPrimary }).ToListAsync(ct);
        var denied = await WriteAccess(db, user, businessId, environment, ct);
        return Results.Ok(new { locations, canWrite = denied is null, isOwner = role == "Owner" });
    }

    private static async Task<IResult> List(long businessId, int? page, bool? pending, ClaimsPrincipal user, AppDbContext db, CancellationToken ct)
    {
        var role = await RetailMasterEndpoints.ReadRole(db, user, businessId, ct);
        if (role is null) return Results.NotFound();
        if (page is < 1 or > 100000) return Results.BadRequest();
        var query = Visible(db, businessId, role).AsNoTracking();
        if (pending == true) query = query.Where(x => x.Status != "Completed" && x.Status != "Cancelled");
        var rows = await query.OrderByDescending(x => x.Id).Skip(((page ?? 1) - 1) * 20).Take(21).ToListAsync(ct);
        return Results.Ok(new { items = rows.Take(20), hasMore = rows.Count > 20 });
    }

    private static async Task<IResult> InTransit(long businessId, int? page, ClaimsPrincipal user,
        AppDbContext db, CancellationToken ct)
    {
        var role = await RetailMasterEndpoints.ReadRole(db, user, businessId, ct);
        if (role is null) return Results.NotFound();
        if (page is < 1 or > 100000) return Results.BadRequest();
        var transfers = Visible(db, businessId, role).AsNoTracking().Where(x => x.Status == "InTransit");
        var query = from transfer in transfers
                    join line in db.RetailStockTransferLines.AsNoTracking() on transfer.Id equals line.TransferId
                    join product in db.RetailProducts.AsNoTracking() on line.ProductId equals product.Id
                    where product.BusinessId == businessId
                    let settled = db.RetailStockTransferReceipts.Where(x => x.BusinessId == businessId &&
                        x.TransferLineId == line.Id).Sum(x => (decimal?)x.Quantity) ?? 0m
                    where line.Quantity > settled
                    orderby line.Id descending
                    select new { line.Id, line.TransferId, line.ProductId, product.Name, line.UnitCode,
                        transfer.FromLocationId, transfer.ToLocationId, transfer.DispatchedAtUtc,
                        dispatched = line.Quantity, remaining = line.Quantity - settled };
        var items = await query.Skip(((page ?? 1) - 1) * 20).Take(21).ToListAsync(ct);
        return Results.Ok(new { items = items.Take(20), hasMore = items.Count > 20 });
    }

    private static async Task<IResult> Read(long businessId, long id, ClaimsPrincipal user, AppDbContext db, CancellationToken ct)
    {
        var role = await RetailMasterEndpoints.ReadRole(db, user, businessId, ct);
        if (role is null) return Results.NotFound();
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var transfer = await Visible(db, businessId, role).AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
        if (transfer is null) return Results.NotFound();
        var lines = await (from l in db.RetailStockTransferLines.AsNoTracking()
                           join p in db.RetailProducts.AsNoTracking() on l.ProductId equals p.Id
                           where l.TransferId == id && p.BusinessId == businessId
                           orderby l.Id
                           select new { l.Id, l.ProductId, p.Name, l.UnitCode, l.Quantity }).ToListAsync(ct);
        if (lines.Count == 0 || lines.Count != await db.RetailStockTransferLines.CountAsync(x => x.TransferId == id, ct))
            return Results.Conflict(new { message = "Transfer lines require reconciliation." });
        var totals = await (from e in db.RetailStockTransferReceipts.AsNoTracking()
                            join l in db.RetailStockTransferLines on e.TransferLineId equals l.Id
                            where l.TransferId == id && e.BusinessId == businessId
                            group e by new { e.TransferLineId, e.Kind } into g
                            select new { g.Key.TransferLineId, g.Key.Kind, Quantity = g.Sum(x => x.Quantity) }).ToListAsync(ct);
        var history = await (from e in db.RetailStockTransferReceipts.AsNoTracking()
                             join l in db.RetailStockTransferLines on e.TransferLineId equals l.Id
                             where l.TransferId == id && e.BusinessId == businessId
                             orderby e.Id descending select e).Take(50).ToListAsync(ct);
        if (lines.Any(l => totals.Where(x => x.TransferLineId == l.Id).Sum(x => x.Quantity) > l.Quantity))
            return Results.Conflict(new { message = "Transfer receipts require reconciliation." });
        await tx.CommitAsync(ct);
        return Results.Ok(new { transfer, lines = lines.Select(l => new
        {
            l.Id, l.ProductId, l.Name, l.UnitCode, l.Quantity,
            received = totals.Where(x => x.TransferLineId == l.Id && x.Kind == "Received").Sum(x => x.Quantity),
            damaged = totals.Where(x => x.TransferLineId == l.Id && x.Kind == "Damaged").Sum(x => x.Quantity),
            missing = totals.Where(x => x.TransferLineId == l.Id && x.Kind == "Missing").Sum(x => x.Quantity),
            returned = totals.Where(x => x.TransferLineId == l.Id && x.Kind == "Returned").Sum(x => x.Quantity),
            remaining = transfer.Status is "InTransit" or "Completed" ?
                l.Quantity - totals.Where(x => x.TransferLineId == l.Id).Sum(x => x.Quantity) : 0m
        }), history });
    }

    private static async Task<IResult> Create(long businessId, CreateRequest request, ClaimsPrincipal user,
        AppDbContext db, IWebHostEnvironment environment, CancellationToken ct)
    {
        var reason = request.Reason?.Trim() ?? "";
        var key = request.RequestKey?.Trim() ?? "";
        if (request.SaveDraft && request.DispatchNow || request.FromLocationId <= 0 || request.ToLocationId <= 0 || request.FromLocationId == request.ToLocationId ||
            reason.Length is < 3 or > 200 || key.Length is < 16 or > 80 || request.Lines is not { Count: > 0 and <= 50 } ||
            request.Lines.Any(x => x.ProductId <= 0 || !StockPosting.ValidBaseQuantity(x.Quantity, 1m)) ||
            request.Lines.Select(x => x.ProductId).Distinct().Count() != request.Lines.Count)
            return Results.BadRequest(new { message = "Select two different locations and unique products with valid base quantities." });
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var role = await RetailMasterEndpoints.ReadRole(db, user, businessId, ct);
            if (role is null) return Results.NotFound();
            if (request.DispatchNow && role != "Owner") return Results.NotFound();
            var locations = await db.BusinessLocations.AsNoTracking().Where(x => x.BusinessId == businessId &&
                (x.Id == request.FromLocationId || x.Id == request.ToLocationId)).ToListAsync(ct);
            if (locations.Count != 2 || (role != "Owner" && !locations.Any(x => x.IsPrimary))) return Results.NotFound();
            var actor = long.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var existing = await Visible(db, businessId, role).AsNoTracking().SingleOrDefaultAsync(x => x.RequestKey == key, ct);
            if (existing is not null)
            {
                var lines = await db.RetailStockTransferLines.AsNoTracking().Where(x => x.TransferId == existing.Id).ToListAsync(ct);
                return existing.FromLocationId == request.FromLocationId && existing.ToLocationId == request.ToLocationId &&
                    existing.Reason == reason && existing.RequestedByUserId == actor && lines.Count == request.Lines.Count &&
                    request.Lines.All(x => lines.Any(l => l.ProductId == x.ProductId && l.Quantity == x.Quantity))
                    ? Results.Ok(new { existing.Id, existing.Status }) : Results.Conflict(new { message = "Transfer request key already used." });
            }
            var denied = await WriteAccess(db, user, businessId, environment, ct);
            if (denied is not null) return denied;
            var ids = request.Lines.Select(x => x.ProductId).ToArray();
            var products = await (from product in db.RetailProducts.AsNoTracking()
                                  join unit in db.RetailUnits.AsNoTracking() on product.UnitId equals unit.Id
                                  where product.BusinessId == businessId && unit.BusinessId == businessId &&
                                    product.IsActive && ids.Contains(product.Id)
                                  select new { product.Id, unit.Code }).ToDictionaryAsync(x => x.Id, ct);
            if (products.Count != ids.Length) return Results.NotFound();
            var transfer = new RetailStockTransfer { BusinessId = businessId, FromLocationId = request.FromLocationId,
                ToLocationId = request.ToLocationId, RequestedByUserId = actor, Reason = reason, RequestKey = key,
                Status = request.SaveDraft ? "Draft" : "Requested", RequestedAtUtc = DateTime.UtcNow };
            db.RetailStockTransfers.Add(transfer);
            await db.SaveChangesAsync(ct);
            var linesToSave = request.Lines.OrderBy(x => x.ProductId).Select(x => new RetailStockTransferLine
            { TransferId = transfer.Id, ProductId = x.ProductId, Quantity = x.Quantity, UnitCode = products[x.ProductId].Code }).ToList();
            db.RetailStockTransferLines.AddRange(linesToSave);
            await db.SaveChangesAsync(ct);
            if (request.DispatchNow)
            {
                transfer.ApprovedByUserId = actor; transfer.ApprovedAtUtc = DateTime.UtcNow;
                var failure = await PostDispatch(db, transfer, linesToSave, actor, ct);
                if (failure is not null) return failure;
            }
            await tx.CommitAsync(ct);
            return Results.Ok(new { transfer.Id, transfer.Status });
        }
        catch (Exception error) when (StockPosting.IsSqlConflict(error) || error is DbUpdateException update && RetailModule.IsDuplicateKey(update))
        {
            return Results.Conflict(new { message = "Stock or transfer changed. Check history and retry the original request." });
        }
    }

    private static async Task<IResult?> PostDispatch(AppDbContext db, RetailStockTransfer transfer, List<RetailStockTransferLine> lines, long actor, CancellationToken ct)
    {
        if (lines.Count == 0) return Results.Conflict(new { message = "Transfer has no lines." });
        foreach (var line in lines.OrderBy(x => x.ProductId))
        {
            if (!await (from p in db.RetailProducts join u in db.RetailUnits on p.UnitId equals u.Id
                where p.BusinessId == transfer.BusinessId && p.Id == line.ProductId && p.IsActive &&
                      u.BusinessId == transfer.BusinessId && u.Code == line.UnitCode select p.Id).AnyAsync(ct))
                return Results.Conflict(new { message = "Product or base unit changed. Review transfer." });
            if (await StockPosting.ApplyAsync(db, transfer.BusinessId, transfer.FromLocationId, line.ProductId,
                -line.Quantity, "TransferDispatch", line.Id, actor, reason: transfer.Reason) is null)
                return Results.Conflict(new { message = "Insufficient source stock. No transfer was dispatched." });
        }
        transfer.DispatchedByUserId = actor; transfer.DispatchedAtUtc = DateTime.UtcNow; transfer.Status = "InTransit";
        await db.SaveChangesAsync(ct);
        return null;
    }

    private static Task<IResult> Submit(long businessId, long id, ClaimsPrincipal user, AppDbContext db, IWebHostEnvironment environment, CancellationToken ct)
        => Transition(businessId, id, "submit", user, db, environment, ct);
    private static Task<IResult> Approve(long businessId, long id, ClaimsPrincipal user, AppDbContext db, IWebHostEnvironment environment, CancellationToken ct)
        => Transition(businessId, id, "approve", user, db, environment, ct);
    private static Task<IResult> Dispatch(long businessId, long id, ClaimsPrincipal user, AppDbContext db, IWebHostEnvironment environment, CancellationToken ct)
        => Transition(businessId, id, "dispatch", user, db, environment, ct);
    private static Task<IResult> Cancel(long businessId, long id, ClaimsPrincipal user, AppDbContext db, IWebHostEnvironment environment, CancellationToken ct)
        => Transition(businessId, id, "cancel", user, db, environment, ct);

    private static async Task<IResult> Transition(long businessId, long id, string action, ClaimsPrincipal user,
        AppDbContext db, IWebHostEnvironment environment, CancellationToken ct)
    {
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var role = await RetailMasterEndpoints.ReadRole(db, user, businessId, ct);
            if (role is null) return Results.NotFound();
            var transfer = await Visible(db, businessId, role).SingleOrDefaultAsync(x => x.Id == id, ct);
            if (transfer is null) return Results.NotFound();
            var denied = await WriteAccess(db, user, businessId, environment, ct,
                action == "dispatch" ? transfer.FromLocationId : null, ownerOnly: action is not ("dispatch" or "submit"));
            if (denied is not null) return denied;
            var actor = long.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
            if (action == "submit")
            {
                if (role != "Owner" && transfer.RequestedByUserId != actor) return Results.NotFound();
                if (transfer.Status == "Draft") transfer.Status = "Requested";
                else if (transfer.Status != "Requested")
                    return Results.Conflict(new { message = "Draft has already progressed. Review transfer history." });
            }
            else if (action == "approve" && transfer.Status == "Requested")
            {
                transfer.ApprovedByUserId = actor; transfer.ApprovedAtUtc = DateTime.UtcNow; transfer.Status = "Approved";
            }
            else if (action == "dispatch" && transfer.Status == "Approved")
            {
                var lines = await db.RetailStockTransferLines.Where(x => x.TransferId == id).ToListAsync(ct);
                var failure = await PostDispatch(db, transfer, lines, actor, ct);
                if (failure is not null) return failure;
            }
            else if (action == "cancel" && transfer.Status is "Draft" or "Requested" or "Approved")
                transfer.Status = "Cancelled";
            else if (!(action == "approve" && transfer.ApprovedAtUtc != null ||
                action == "dispatch" && transfer.DispatchedAtUtc != null || action == "cancel" && transfer.Status == "Cancelled"))
                return Results.Conflict(new { message = "Transfer state does not permit this operation." });
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return Results.Ok(new { transfer.Id, transfer.Status });
        }
        catch (Exception error) when (StockPosting.IsSqlConflict(error))
        { return Results.Conflict(new { message = "Transfer changed. Refresh its status before retrying." }); }
    }

    private static async Task<IResult> Receive(long businessId, long id, ReceiveRequest request, ClaimsPrincipal user,
        AppDbContext db, IWebHostEnvironment environment, CancellationToken ct)
    {
        var key = request.RequestKey?.Trim() ?? "";
        var reason = request.Reason?.Trim() ?? "";
        if (request.Kind is not ("Received" or "Damaged" or "Missing" or "Returned") || !StockPosting.ValidBaseQuantity(request.Quantity, 1m) ||
            key.Length is < 16 or > 80 || reason.Length > 200 || (request.Kind != "Received" && reason.Length < 5))
            return Results.BadRequest(new { message = "Enter valid receiving quantity and a reason for exceptions." });
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var role = await RetailMasterEndpoints.ReadRole(db, user, businessId, ct);
            if (role is null) return Results.NotFound();
            var transfer = await Visible(db, businessId, role).SingleOrDefaultAsync(x => x.Id == id, ct);
            if (transfer is null) return Results.NotFound();
            var returnedToSource = request.Kind == "Returned";
            var postingLocation = returnedToSource ? transfer.FromLocationId : transfer.ToLocationId;
            var denied = await WriteAccess(db, user, businessId, environment, ct, postingLocation, request.Kind != "Received");
            if (denied is not null) return denied;
            var line = await db.RetailStockTransferLines.SingleOrDefaultAsync(x => x.TransferId == id && x.Id == request.LineId, ct);
            if (line is null) return Results.NotFound();
            var actor = long.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var previous = await db.RetailStockTransferReceipts.AsNoTracking().SingleOrDefaultAsync(x => x.BusinessId == businessId && x.RequestKey == key, ct);
            if (previous is not null)
                return previous.ActorUserId == actor && previous.TransferLineId == line.Id &&
                    previous.Quantity == request.Quantity && previous.Kind == request.Kind && previous.Reason == reason
                    ? Results.Ok(new { previous.Id, transfer.Status }) : Results.Conflict(new { message = "Receiving request key already used." });
            if (transfer.Status != "InTransit") return Results.Conflict(new { message = "Transfer is not in transit." });
            // Receiving must be backed by the original, exact source stock deduction.
            if (transfer.DispatchedByUserId is null || transfer.DispatchedAtUtc is null ||
                !await db.RetailStockMovements.AsNoTracking().AnyAsync(x =>
                    x.BusinessId == businessId && x.LocationId == transfer.FromLocationId &&
                    x.ProductId == line.ProductId && x.SourceType == "TransferDispatch" &&
                    x.SourceId == line.Id && x.QuantityChange == -line.Quantity &&
                    x.ActorUserId == transfer.DispatchedByUserId, ct))
                return Results.Conflict(new { code = "TRANSFER_RECONCILIATION_REQUIRED",
                    message = "Original stock dispatch requires reconciliation. No receiving was posted." });
            var settled = await db.RetailStockTransferReceipts.Where(x => x.BusinessId == businessId && x.TransferLineId == line.Id)
                .SumAsync(x => x.Quantity, ct);
            if (line.Quantity - settled != request.ExpectedRemaining || request.Quantity > line.Quantity - settled)
                return Results.Conflict(new { message = "Receiving quantity exceeds remaining stock or changed. Refresh." });
            if (!await (from p in db.RetailProducts join u in db.RetailUnits on p.UnitId equals u.Id
                where p.BusinessId == businessId && p.Id == line.ProductId && u.BusinessId == businessId &&
                    u.Code == line.UnitCode select p.Id).AnyAsync(ct))
                return Results.Conflict(new { message = "Product base unit requires reconciliation." });
            var entry = new RetailStockTransferReceipt { BusinessId = businessId, TransferLineId = line.Id,
                ActorUserId = actor, Quantity = request.Quantity, Kind = request.Kind!, Reason = reason,
                RequestKey = key, PostedAtUtc = DateTime.UtcNow };
            db.RetailStockTransferReceipts.Add(entry);
            await db.SaveChangesAsync(ct);
            if ((entry.Kind is "Received" or "Returned") && await StockPosting.ApplyAsync(db, businessId, postingLocation,
                line.ProductId, entry.Quantity, returnedToSource ? "TransferReturn" : "TransferReceive", entry.Id, actor, reason: reason) is null)
                return Results.Conflict(new { message = "Receiving location stock could not be posted." });
            var lines = await db.RetailStockTransferLines.Where(x => x.TransferId == id).ToListAsync(ct);
            var lineIds = lines.Select(x => x.Id).ToArray();
            var totals = await db.RetailStockTransferReceipts.Where(x => x.BusinessId == businessId && lineIds.Contains(x.TransferLineId))
                .GroupBy(x => x.TransferLineId).Select(g => new { Id = g.Key, Quantity = g.Sum(x => x.Quantity) }).ToDictionaryAsync(x => x.Id, x => x.Quantity, ct);
            if (lines.All(x => totals.GetValueOrDefault(x.Id) == x.Quantity)) transfer.Status = "Completed";
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return Results.Ok(new { entry.Id, transfer.Status });
        }
        catch (Exception error) when (StockPosting.IsSqlConflict(error) || error is DbUpdateException update && RetailModule.IsDuplicateKey(update))
        { return Results.Conflict(new { message = "Receiving changed. Check history and retry the original request." }); }
    }
}
