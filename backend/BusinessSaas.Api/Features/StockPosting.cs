using BusinessSaas.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace BusinessSaas.Api.Features;

public static class StockPosting
{
    private const decimal MaxQuantity = 9999999999.999m;

    // The caller owns the transaction, so the business document and stock post commit together.
    // A failed update leaves the balance and ledger untouched.
    public static async Task<decimal?> ApplyAsync(AppDbContext db, long businessId,
        long locationId, long productId, decimal change, string sourceType,
        long sourceId, long actorUserId, decimal? expectedQuantity = null)
    {
        if (db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Stock posting requires a database transaction.");

        if (businessId <= 0 || locationId <= 0 || productId <= 0 ||
            sourceId <= 0 || actorUserId <= 0 ||
            string.IsNullOrWhiteSpace(sourceType) || sourceType.Length > 30 ||
            change == 0 || change is < -MaxQuantity or > MaxQuantity ||
            decimal.Round(change, 3) != change)
            throw new ArgumentException("Invalid stock movement.");

        if (await db.RetailStockMovements.AsNoTracking().AnyAsync(x =>
                x.BusinessId == businessId && x.SourceType == sourceType &&
                x.SourceId == sourceId))
            return null;

        var balances = db.RetailStockBalances.Where(x => x.BusinessId == businessId &&
            x.LocationId == locationId && x.ProductId == productId);
        if (expectedQuantity.HasValue)
            balances = balances.Where(x => x.Quantity == expectedQuantity.Value);

        var updated = await balances.Where(x =>
                x.Quantity + change >= 0 && x.Quantity + change <= MaxQuantity)
            .ExecuteUpdateAsync(set => set.SetProperty(x => x.Quantity, x => x.Quantity + change));

        if (updated == 0)
        {
            // Only incoming stock can establish an uninitialized balance.
            if (change < 0 || expectedQuantity.HasValue ||
                await db.RetailStockBalances.AnyAsync(x => x.BusinessId == businessId &&
                    x.LocationId == locationId && x.ProductId == productId))
                return null;

            db.RetailStockBalances.Add(new RetailStockBalance
            {
                BusinessId = businessId,
                LocationId = locationId,
                ProductId = productId,
                Quantity = change
            });
            await db.SaveChangesAsync();
        }

        var balanceAfter = await db.RetailStockBalances.AsNoTracking()
            .Where(x => x.BusinessId == businessId && x.LocationId == locationId &&
                x.ProductId == productId)
            .Select(x => x.Quantity).SingleAsync();

        db.RetailStockMovements.Add(new RetailStockMovement
        {
            BusinessId = businessId,
            LocationId = locationId,
            ProductId = productId,
            ActorUserId = actorUserId,
            SourceType = sourceType,
            SourceId = sourceId,
            QuantityChange = change,
            BalanceAfter = balanceAfter,
            PostedAtUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        return balanceAfter;
    }
}
