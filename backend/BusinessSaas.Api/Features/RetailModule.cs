using BusinessSaas.Api.Data;

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
