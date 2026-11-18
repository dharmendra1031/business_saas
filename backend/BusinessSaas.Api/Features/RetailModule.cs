using BusinessSaas.Api.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace BusinessSaas.Api.Features;

// Keep Retail routes and its initial data together. Other business types add their
// own module without changing Retail endpoints or Retail tables.
public static class RetailModule
{
    public static void MapRetailModule(this WebApplication app)
    {
        app.MapRetailMasterEndpoints();
        app.MapRetailProductEndpoints();
        app.MapRetailContactEndpoints();
        app.MapRetailStockEndpoints();
        app.MapRetailAdjustmentEndpoints();
        app.MapRetailPurchaseEndpoints();
        if (app.Environment.IsDevelopment()) app.MapRetailCashSales();
    }

    internal static bool IsDuplicateKey(DbUpdateException error) =>
        error.InnerException is SqlException { Number: 2601 or 2627 };

    public static void AddBusinessDefaults(AppDbContext db, long businessId)
    {
        db.RetailCategories.Add(new RetailCategory
        {
            BusinessId = businessId,
            Name = "General",
            NameKey = "GENERAL"
        });
        db.RetailUnits.Add(new RetailUnit
        {
            BusinessId = businessId,
            Name = "Item",
            NameKey = "ITEM",
            Code = "ITEM"
        });
    }
}
