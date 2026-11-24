using System.Data;
using System.Drawing;
using System.Runtime.Versioning;
using System.Text;
using Microsoft.Reporting.NETCore;

namespace BusinessSaas.Api.Reports;

public static class RetailReceiptRenderer
{
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
        return report.Render("PDF");
    }

    private static ReportParameter Flag(string name, bool value) =>
        new(name, value ? "true" : "false");

    private static Stream Open(string name) =>
        typeof(RetailReceiptRenderer).Assembly.GetManifestResourceStream("BusinessSaas.Api.Reports." + name)
        ?? throw new InvalidOperationException("Missing embedded receipt definition: " + name);
}
