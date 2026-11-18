namespace BusinessSaas.Api.Data;

// A posted single-product cash receipt. Amounts and descriptions are snapshots.
public sealed class RetailCashSale
{
    public long Id { get; set; }
    public long BusinessId { get; set; }
    public long LocationId { get; set; }
    public long CashierId { get; set; }
    public long ProductId { get; set; }
    public string RequestKey { get; set; } = "";
    public string ProductName { get; set; } = "";
    public string CurrencyCode { get; set; } = "";
    public decimal UnitPrice { get; set; }
    public int Quantity { get; set; }
    public decimal Total { get; set; }
    public decimal Tendered { get; set; }
    public DateTime PostedAtUtc { get; set; }
}
