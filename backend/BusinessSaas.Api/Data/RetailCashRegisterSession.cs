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
}
