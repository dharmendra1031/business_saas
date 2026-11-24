using System.Data;
using BusinessSaas.Api.Data;
using System.Drawing;
using System.Globalization;
using System.Runtime.Versioning;
using System.Text;
using Microsoft.Reporting.NETCore;

namespace BusinessSaas.Api.Reports;

public static class RetailReceiptRenderer
{
    // Only synthetic, in-memory data: no tenant access, persisted sale or printer job.
    [SupportedOSPlatform("windows")]
    public static void WarmUp()
    {
        var business = new Business { Id = 1, Name = "Renderer warm-up" };
        var sale = new RetailCashSale
        {
            Id = 1, BusinessId = 1, CurrencyCode = "INR",
            Total = 1m, Tendered = 1m, PostedAtUtc = DateTime.UtcNow,
            Lines = [new RetailCashSaleLine
            {
                Id = 1, ProductName = "Sample", Quantity = 1m,
                UnitPrice = 1m, LineTotal = 1m, ProductSku = "SAMPLE", UnitCode = "PCS"
            }]
        };
        var data = RetailReceiptReportData.FromCashSale(sale, business, TimeZoneInfo.Utc);
        foreach (var paper in new[] { "A4", "Thermal80" })
        {
            var pdf = Render(data, paper);
            if (!pdf.AsSpan().StartsWith("%PDF-"u8))
                throw new InvalidDataException("Receipt renderer warm-up did not produce a PDF.");
        }
    }

    [SupportedOSPlatform("windows")]
    public static byte[] Render(RetailReceiptReportData data, string paperSize)
    {
        var template = paperSize switch
        {
            "A4" => "RetailCashReceiptA4.rdlc",
            "Thermal80" => "RetailCashReceiptThermal80.rdlc",
            _ => throw new ArgumentException("Unknown receipt paper size.", nameof(paperSize))
        };
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        using var schema = Open("RetailReceiptDataSet.xsd");
        using var dataset = new DataSet();
        dataset.ReadXmlSchema(schema);
        var h = data.Header;
        byte[]? logo = null;
        if (data.Parameters.ShowLogo && h.MerchantLogo is { } bytes)
        {
            if (!ReceiptLogo.IsValid(bytes)) throw new InvalidDataException("Invalid receipt logo.");
            using var stream = new MemoryStream(bytes);
            using var decoded = Image.FromStream(stream, useEmbeddedColorManagement: false, validateImageData: true);
            logo = bytes;
        }
        var header = dataset.Tables["InvoiceHeader"]!;
        header.Rows.Add(h.BusinessName, h.ReceiptNumber, h.DateText, h.TimeText, h.CurrencyCode,
            h.PaymentMethod, h.TotalAmount, h.CashReceived, h.ChangeAmount, h.FooterText,
            h.MerchantLegalName, h.MerchantAddress, h.MerchantPhone, h.MerchantEmail,
            h.CustomerName, h.CustomerPhone, h.CustomerAddress, h.LocationName, (object?)logo ?? DBNull.Value);
        var lines = dataset.Tables["InvoiceLines"]!;
        foreach (var line in data.Lines)
            lines.Rows.Add(line.LineNumber, line.ProductName, line.Quantity, line.UnitPrice,
                line.LineTotal, line.ProductSku, line.UnitCode);

        using var definition = Open(template);
        using var report = new LocalReport();
        report.LoadReportDefinition(definition);
        report.DataSources.Add(new ReportDataSource("InvoiceHeader", header));
        report.DataSources.Add(new ReportDataSource("InvoiceLines", lines));
        var p = data.Parameters;
        report.SetParameters(new[] {
            Flag(nameof(p.ShowTime), p.ShowTime), Flag(nameof(p.ShowUnitPrice), p.ShowUnitPrice),
            Flag(nameof(p.ShowCashReceived), p.ShowCashReceived), Flag(nameof(p.ShowChange), p.ShowChange),
            Flag(nameof(p.ShowFooter), p.ShowFooter), Flag(nameof(p.ShowBranch), p.ShowBranch),
            Flag(nameof(p.ShowSku), p.ShowSku), Flag(nameof(p.ShowUnit), p.ShowUnit),
            Flag(nameof(p.ShowLegalName), p.ShowLegalName), Flag(nameof(p.ShowAddress), p.ShowAddress),
            Flag(nameof(p.ShowPhone), p.ShowPhone), Flag(nameof(p.ShowEmail), p.ShowEmail),
            Flag(nameof(p.ShowCustomerName), p.ShowCustomerName), Flag(nameof(p.ShowCustomerPhone), p.ShowCustomerPhone),
            Flag(nameof(p.ShowCustomerAddress), p.ShowCustomerAddress), Flag(nameof(p.ShowLogo), p.ShowLogo)
        });
        // DeviceInfo changes only the physical output page size, never the RDLC.
        return paperSize == "Thermal80"
            ? report.Render("PDF", ThermalDeviceInfo(data))
            : report.Render("PDF");
    }

    [SupportedOSPlatform("windows")]
    private static string ThermalDeviceInfo(RetailReceiptReportData data)
    {
        // Dimensions follow RetailCashReceiptThermal80.rdlc (inches).
        // Use measured wrapping plus a small safety allowance; RDLC remains
        // responsible for pagination when content exceeds the selected size.
        using var bitmap = new Bitmap(1, 1);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.PageUnit = GraphicsUnit.Point;
        using var regular = new Font("Arial", 8.5f);
        using var title = new Font("Arial", 12f, FontStyle.Bold);
        using var total = new Font("Arial", 10f, FontStyle.Bold);
        var h = data.Header;
        var p = data.Parameters;
        double Row(string text, Font font, double width, double minimum) =>
            Math.Max(minimum, (graphics.MeasureString(text, font,
                new SizeF((float)(width * 72 - 5), 10000)).Height + 3) / 72d);
        double Optional(bool show, string text) =>
            show && !string.IsNullOrEmpty(text) ? Row(text, regular, 2.86, .18) : 0;
        string Amount(decimal amount) => h.CurrencyCode + " " +
            amount.ToString("0.00", CultureInfo.InvariantCulture);

        // Top/bottom margins, body allowance, receipt title, ID/date and item headings.
        var height = .24 + .05 + .10 + .19 + .18 + .18 + .19 + .20;
        height += Row(h.BusinessName, title, 2.86, .25);
        if (p.ShowLogo && h.MerchantLogo is not null) height += .48;
        height += Optional(p.ShowLegalName, h.MerchantLegalName)
            + Optional(p.ShowAddress, h.MerchantAddress)
            + Optional(p.ShowPhone, h.MerchantPhone)
            + Optional(p.ShowEmail, h.MerchantEmail)
            + Optional(p.ShowBranch, h.LocationName)
            + Optional(p.ShowCustomerName && h.CustomerName.Length > 0, "Customer name: " + h.CustomerName)
            + Optional(p.ShowCustomerPhone && h.CustomerPhone.Length > 0, "Customer phone: " + h.CustomerPhone)
            + Optional(p.ShowCustomerAddress && h.CustomerAddress.Length > 0, "Customer address: " + h.CustomerAddress);

        foreach (var line in data.Lines)
        {
            var description = line.LineNumber + ". " + line.ProductName;
            if (p.ShowSku && line.ProductSku.Length > 0) description += "\nSKU: " + line.ProductSku;
            description += "\n" + line.Quantity.ToString("0.###", CultureInfo.InvariantCulture);
            if (p.ShowUnit && line.UnitCode.Length > 0) description += " " + line.UnitCode;
            if (p.ShowUnitPrice) description += " @ " + line.UnitPrice.ToString("0.00", CultureInfo.InvariantCulture);
            height += Math.Max(Row(description, regular, 1.95, .31),
                Row(Amount(line.LineTotal), regular, .91, .31));
        }

        height += Row(Amount(h.TotalAmount), total, .91, .25);
        if (p.ShowCashReceived) height += Row(Amount(h.CashReceived), regular, .91, .18);
        if (p.ShowChange) height += Row(Amount(h.ChangeAmount), regular, .91, .18);
        if (p.ShowFooter) height += Row(h.FooterText, regular, 2.86, .18);

        // Cap long documents at 40 inches; allow normal RDLC page breaks.
        // Round up to a tenth of an inch rather than risk a clipped last row.
        var inches = Math.Clamp(Math.Ceiling(height * 10) / 10, 3, 40);
        return "<DeviceInfo><PageHeight>" + inches.ToString("0.0", CultureInfo.InvariantCulture)
            + "in</PageHeight></DeviceInfo>";
    }

    private static ReportParameter Flag(string name, bool value) =>
        new(name, value ? "true" : "false");

    private static Stream Open(string name) =>
        typeof(RetailReceiptRenderer).Assembly.GetManifestResourceStream("BusinessSaas.Api.Reports." + name)
        ?? throw new InvalidOperationException("Missing embedded receipt definition: " + name);
}
