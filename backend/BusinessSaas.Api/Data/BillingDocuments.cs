namespace BusinessSaas.Api.Data;

// Drafts are internal review records, never statutory documents.
public sealed class BillingTaxProfile
{
    public long Id { get; set; }
    public long BusinessId { get; set; }
    public int Revision { get; set; }
    public string IssuerName { get; set; } = "";
    public string IssuerAddress { get; set; } = "";
    public string IssuerGstin { get; set; } = "";
    public string IssuerStateCode { get; set; } = "";
    public string RecipientName { get; set; } = "";
    public string RecipientAddress { get; set; } = "";
    public string? RecipientGstin { get; set; }
    public string RecipientStateCode { get; set; } = "";
    public string ServiceCode { get; set; } = "";
    public string ServiceDescription { get; set; } = "";
    public string InvoiceSeries { get; set; } = "";
    public decimal GstRate { get; set; }
    public bool PricesIncludeTax { get; set; }
    public long UpdatedByUserId { get; set; }
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed class BillingDocumentDraft
{
    public long Id { get; set; }
    public long BusinessId { get; set; }
    public long SubscriptionPaymentId { get; set; }
    public long? SubscriptionRefundId { get; set; }
    public long? OriginalDocumentId { get; set; }
    public long BillingTaxProfileId { get; set; }
    public int TaxProfileRevision { get; set; }
    public string DocumentType { get; set; } = "";
    public string Status { get; set; } = "";
    public string? DocumentNumber { get; set; }
    public string TaxSnapshotJson { get; set; } = "";
    public string CurrencyCode { get; set; } = "";
    public int FinancialYearStart { get; set; }
    public decimal GrossAmount { get; set; }
    public decimal TaxableAmount { get; set; }
    public decimal CgstAmount { get; set; }
    public decimal SgstAmount { get; set; }
    public decimal IgstAmount { get; set; }
    public long CreatedByUserId { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
