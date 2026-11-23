namespace BusinessSaas.Api.Data;

// The receipt and its lines preserve the amounts recorded at checkout.
public sealed class RetailCashSale
{
    public long Id { get; set; }
    public long BusinessId { get; set; }
    public long LocationId { get; set; }
    public long CashierId { get; set; }
    public string RequestKey { get; set; } = "";
    public string CurrencyCode { get; set; } = "";
    public decimal Total { get; set; }
    public decimal Tendered { get; set; }
    public DateTime PostedAtUtc { get; set; }
    public List<RetailCashSaleLine> Lines { get; set; } = [];
}

public sealed class RetailCashSaleLine
{
    public long Id { get; set; }
    public long SaleId { get; set; }
    public long ProductId { get; set; }
    public string ProductName { get; set; } = "";
    public decimal UnitPrice { get; set; }
    public decimal Quantity { get; set; }
}
