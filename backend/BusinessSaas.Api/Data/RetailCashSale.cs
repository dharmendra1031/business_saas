namespace BusinessSaas.Api.Data;

// The receipt and its lines preserve the amounts recorded at checkout.
public sealed class RetailCashSale
{
    public long Id { get; set; }
    public long BusinessId { get; set; }
    public long LocationId { get; set; }
    public long CashierId { get; set; }
    public long? CustomerId { get; set; }
    public string? CustomerName { get; set; }
    public string? CustomerPhone { get; set; }
    public string? CustomerAddress { get; set; }
    public string RequestKey { get; set; } = "";
    public string? MerchantLegalName { get; set; }
    public string? MerchantAddress { get; set; }
    public string? MerchantPhone { get; set; }
    public string? MerchantEmail { get; set; }
    public byte[]? MerchantLogo { get; set; }
    public string? BusinessName { get; set; }
    public string? LocationName { get; set; }
    public string? TimeZoneId { get; set; }
    public string CurrencyCode { get; set; } = "";
    public decimal Total { get; set; }
    public decimal Tendered { get; set; }
    // Null identifies an immediately settled cash sale; credit is a separate receivable.
    public DateOnly? CreditDueDate { get; set; }
    public DateTime PostedAtUtc { get; set; }
    public List<RetailCashSaleLine> Lines { get; set; } = [];
}

public sealed class RetailCashSaleLine
{
    public long Id { get; set; }
    public long SaleId { get; set; }
    public long ProductId { get; set; }
    public string ProductName { get; set; } = "";
    public string? ProductSku { get; set; }
    public string? UnitCode { get; set; }
    public long? SelectedUnitId { get; set; }
    public decimal? ConversionFactor { get; set; }
    public string? BaseUnitCode { get; set; }
    public decimal? LineTotal { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal Quantity { get; set; }
}

public sealed class RetailCashReturn
{
    public long Id { get; set; }
    public long BusinessId { get; set; }
    public long SaleId { get; set; }
    public long ActorUserId { get; set; }
    public string RequestKey { get; set; } = "";
    public string Reason { get; set; } = "";
    public decimal Total { get; set; }
    public DateTime PostedAtUtc { get; set; }
    public List<RetailCashReturnLine> Lines { get; set; } = [];
}

public sealed class RetailCashReturnLine
{
    public long Id { get; set; }
    public long ReturnId { get; set; }
    public long SaleLineId { get; set; }
    public decimal Quantity { get; set; }
    public decimal Amount { get; set; }
    public bool Restock { get; set; }
}

// Immutable cash collection against one posted credit invoice.
public sealed class RetailCreditPayment
{
    public long Id { get; set; }
    public long BusinessId { get; set; }
    public long SaleId { get; set; }
    public long ActorUserId { get; set; }
    public string RequestKey { get; set; } = "";
    public decimal Amount { get; set; }
    public DateTime PostedAtUtc { get; set; }
}
