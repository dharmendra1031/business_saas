using System.Xml.Linq;
using System.Xml.Schema;
using BusinessSaas.Api.Data;
using BusinessSaas.Api.Reports;
using Xunit;

namespace BusinessSaas.Tests;

public sealed class RetailReceiptReportTests
{
    private static string Fixture(string name) =>
        Path.Combine(AppContext.BaseDirectory, "ReportFixtures", name);

    [Theory]
    [InlineData("RetailCashReceiptThermal80.rdlc", 3.15m)]
    [InlineData("RetailCashReceiptA4.rdlc", 8.27m)]
    public void Static_rdlc_has_the_exact_typed_dataset_fields_and_safe_visibility(
        string template, decimal pageWidth)
    {
        XNamespace rdl = "http://schemas.microsoft.com/sqlserver/reporting/2016/01/reportdefinition";
        var r = XDocument.Load(Fixture(template));
        Assert.Equal(rdl + "Report", r.Root!.Name);

        var source = Assert.Single(r.Descendants(rdl + "DataSource"));
        Assert.Equal("RetailReceiptSource", (string?)source.Attribute("Name"));
        Assert.DoesNotContain("Server=", source.ToString(), StringComparison.OrdinalIgnoreCase);

        var sets = r.Descendants(rdl + "DataSet").ToDictionary(
            x => (string)x.Attribute("Name")!,
            x => x.Descendants(rdl + "Field")
                .Select(y => (string)y.Attribute("Name")!).ToArray());
        Assert.Equal(2, sets.Count);
        Assert.Equal(new[] { "BusinessName", "ReceiptNumber", "DateText", "TimeText",
            "CurrencyCode", "PaymentMethod", "TotalAmount", "CashReceived",
            "ChangeAmount", "FooterText" }, sets["InvoiceHeader"]);
        Assert.Equal(new[] { "LineNumber", "ProductName", "Quantity", "UnitPrice",
            "LineTotal" }, sets["InvoiceLines"]);
        Assert.Equal("InvoiceLines", Assert.Single(r.Descendants(rdl + "Tablix"))
            .Element(rdl + "DataSetName")!.Value);

        var parameters = r.Descendants(rdl + "ReportParameter")
            .Select(x => (string)x.Attribute("Name")!).ToArray();
        Assert.Equal(new[] { "ShowTime", "ShowUnitPrice", "ShowCashReceived",
            "ShowChange", "ShowFooter" }, parameters);
        var expressions = r.Descendants(rdl + "Value").Select(x => x.Value)
            .Concat(r.Descendants(rdl + "Hidden").Select(x => x.Value)).ToArray();
        Assert.Contains(expressions, x => x.Contains("Fields!TotalAmount.Value"));
        Assert.Contains(expressions, x => x.Contains("Fields!ReceiptNumber.Value"));
        Assert.Contains(expressions, x => x.Contains("Fields!LineTotal.Value"));
        Assert.Contains(expressions, x => x.Contains("Parameters!ShowUnitPrice.Value"));
        foreach (var optional in new[] { "ShowCashReceived", "ShowChange", "ShowFooter" })
            Assert.Contains(expressions, x => x == $"=Not(Parameters!{optional}.Value)");
        Assert.DoesNotContain("TAX INVOICE", r.ToString(), StringComparison.OrdinalIgnoreCase);
        var page = Assert.Single(r.Descendants(rdl + "Page"));
        Assert.Equal(pageWidth, decimal.Parse(page.Element(rdl + "PageWidth")!.Value
            .Replace("in", ""), System.Globalization.CultureInfo.InvariantCulture));
        var contentWidth = decimal.Parse(r.Descendants(rdl + "ReportSection")
            .Single().Element(rdl + "Width")!.Value.Replace("in", ""),
            System.Globalization.CultureInfo.InvariantCulture);
        var left = decimal.Parse(page.Element(rdl + "LeftMargin")!.Value.Replace("in", ""),
            System.Globalization.CultureInfo.InvariantCulture);
        var right = decimal.Parse(page.Element(rdl + "RightMargin")!.Value.Replace("in", ""),
            System.Globalization.CultureInfo.InvariantCulture);
        Assert.True(contentWidth + left + right <= pageWidth,
            "Body width + margins must not overflow paper width.");
    }

    [Fact]
    public void Designer_xsd_is_valid_and_names_are_exactly_matched()
    {
        using var reader = System.Xml.XmlReader.Create(Fixture("RetailReceiptDataSet.xsd"));
        var schema = XmlSchema.Read(reader, (_, e) => throw e.Exception)!;
        var compiled = new XmlSchemaSet();
        compiled.Add(schema);
        compiled.Compile();
        XNamespace xs = "http://www.w3.org/2001/XMLSchema";
        var doc = XDocument.Load(Fixture("RetailReceiptDataSet.xsd"));
        var fields = doc.Descendants(xs + "element")
            .Where(x => new[] { "InvoiceHeader", "InvoiceLines" }
                .Contains((string?)x.Attribute("name")))
            .ToDictionary(x => (string)x.Attribute("name")!, x => x
                .Descendants(xs + "element").Select(f => (string)f.Attribute("name")!).ToArray());
        Assert.Equal(10, fields["InvoiceHeader"].Length);
        Assert.Equal(5, fields["InvoiceLines"].Length);
    }

    [Fact]
    public void Receipt_contract_uses_posted_sale_snapshot_not_live_product_price()
    {
        var business = new Business {
            Id = 42, Name = "Retail Demo", ReceiptShowTime = false,
            ReceiptShowUnitPrice = false, ReceiptShowCashReceived = false,
            ReceiptShowChange = true, ReceiptShowFooter = false,
            ReceiptFooterText = "Come back soon"
        };
        var sale = new RetailCashSale {
            Id = 77, BusinessId = 42, Tendered = 100m, Total = 80m,
            CurrencyCode = "INR", PostedAtUtc = new DateTime(2026, 11, 24, 10, 0, 0, DateTimeKind.Utc),
            Lines = [
                new RetailCashSaleLine { Id = 2, ProductName = "Soap",
                    Quantity = 2m, UnitPrice = 35m },
                new RetailCashSaleLine { Id = 1, ProductName = "Rice",
                    Quantity = 0.125m, UnitPrice = 80m }
            ]
        };
        var report = RetailReceiptReportData.FromCashSale(sale, business,
            TimeZoneInfo.FindSystemTimeZoneById("Asia/Kolkata"));
        Assert.Equal("77", report.Header.ReceiptNumber);
        Assert.Equal("24 Nov 2026", report.Header.DateText);
        Assert.Equal(80m, report.Header.TotalAmount);
        Assert.Equal(20m, report.Header.ChangeAmount);
        Assert.Equal(new[] { "Rice", "Soap" }, report.Lines.Select(x => x.ProductName));
        Assert.Equal(new[] { 10m, 70m }, report.Lines.Select(x => x.LineTotal));
        Assert.False(report.Parameters.ShowTime);
        Assert.False(report.Parameters.ShowUnitPrice);

        sale.BusinessId = 89;
        Assert.Throws<InvalidOperationException>(() =>
            RetailReceiptReportData.FromCashSale(sale, business, TimeZoneInfo.Utc));
        sale.BusinessId = 42;
        sale.Total = 1m;
        Assert.Throws<InvalidOperationException>(() =>
            RetailReceiptReportData.FromCashSale(sale, business, TimeZoneInfo.Utc));
    }
}
