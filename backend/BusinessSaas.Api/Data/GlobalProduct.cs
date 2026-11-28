namespace BusinessSaas.Api.Data;

public sealed class GlobalProduct
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public string NameKey { get; set; } = "";
    public string? Barcode { get; set; }
    public string Brand { get; set; } = "";
    public string Category { get; set; } = "";
    public string PackSize { get; set; } = "";
    public string Unit { get; set; } = "";
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; }
}
