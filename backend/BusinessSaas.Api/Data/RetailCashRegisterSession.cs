namespace BusinessSaas.Api.Data;

public sealed class RetailCashRegisterSession
{
    public long Id { get; set; }
    public long BusinessId { get; set; }
    public long LocationId { get; set; }
    public long ActorUserId { get; set; }
    public string CurrencyCode { get; set; } = "";
    public string RequestKey { get; set; } = "";
    public decimal OpeningCash { get; set; }
    public DateTime OpenedAtUtc { get; set; }
    public DateTime? ClosedAtUtc { get; set; }
    public decimal? ClosingExpectedCash { get; set; }
    public decimal? ClosingActualCash { get; set; }
    public long? ClosedByUserId { get; set; }
    public string? ClosingRequestKey { get; set; }
    public string? ClosingReason { get; set; }
}

// Immutable non-sale cash movement; the register supplies tenant, location and currency.
public sealed class RetailCashRegisterMovement
{
    public long Id { get; set; }
    public long RegisterSessionId { get; set; }
    public long ActorUserId { get; set; }
    public string RequestKey { get; set; } = "";
    public bool IsCashIn { get; set; }
    public decimal Amount { get; set; }
    public string Reason { get; set; } = "";
    public DateTime PostedAtUtc { get; set; }
}
