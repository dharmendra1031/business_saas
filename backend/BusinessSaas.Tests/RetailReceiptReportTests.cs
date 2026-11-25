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
    [InlineData("RetailCashReceiptThermal80.rdlc", "3.15")]
    [InlineData("RetailCashReceiptA4.rdlc", "8.27")]
    public void Static_rdlc_has_the_exact_typed_dataset_fields_and_safe_visibility(
        string template, string pageWidthText)
    {
        var pageWidth = decimal.Parse(pageWidthText, System.Globalization.CultureInfo.InvariantCulture);
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
            "ChangeAmount", "FooterText", "MerchantLegalName", "MerchantAddress", "MerchantPhone", "MerchantEmail", "CustomerName", "CustomerPhone", "CustomerAddress", "LocationName", "MerchantLogo", "CreditDueDateText" }, sets["InvoiceHeader"]);
        Assert.Equal(new[] { "LineNumber", "ProductName", "Quantity", "UnitPrice",
            "LineTotal", "ProductSku", "UnitCode" }, sets["InvoiceLines"]);
        Assert.Equal("InvoiceLines", Assert.Single(r.Descendants(rdl + "Tablix"))
            .Element(rdl + "DataSetName")!.Value);

        var parameters = r.Descendants(rdl + "ReportParameter")
            .Select(x => (string)x.Attribute("Name")!).ToArray();
        Assert.Equal(new[] { "ShowTime", "ShowUnitPrice", "ShowCashReceived",
            "ShowChange", "ShowFooter", "ShowBranch", "ShowSku", "ShowUnit", "ShowLegalName", "ShowAddress", "ShowPhone", "ShowEmail", "ShowCustomerName", "ShowCustomerPhone", "ShowCustomerAddress", "ShowLogo" }, parameters);
        var expressions = r.Descendants(rdl + "Value").Select(x => x.Value)
            .Concat(r.Descendants(rdl + "Hidden").Select(x => x.Value)).ToArray();
        Assert.Contains(expressions, x => x.Contains("Fields!TotalAmount.Value"));
        Assert.Contains(expressions, x => x.Contains("Fields!ReceiptNumber.Value"));
        Assert.Contains(expressions, x => x.Contains("Fields!LineTotal.Value"));
        Assert.Contains(expressions, x => x.Contains("Parameters!ShowUnitPrice.Value"));
        foreach (var optional in new[] { "ShowCashReceived", "ShowChange", "ShowFooter" })
            Assert.Contains(expressions, x => x == $"=Not(Parameters!{optional}.Value)");
        foreach (var optional in new[] { "ShowBranch", "ShowSku", "ShowUnit", "ShowLegalName", "ShowAddress", "ShowPhone", "ShowEmail", "ShowCustomerName", "ShowCustomerPhone", "ShowCustomerAddress", "ShowLogo" })
            Assert.Contains(expressions, x => x.Contains($"Parameters!{optional}.Value"));
        foreach (var field in new[] { "LocationName", "ProductSku", "UnitCode", "MerchantLegalName", "MerchantAddress", "MerchantPhone", "MerchantEmail", "CustomerName", "CustomerPhone", "CustomerAddress", "MerchantLogo" })
            Assert.Contains(expressions, x => x.Contains($"Fields!{field}.Value"));
        var image = Assert.Single(r.Descendants(rdl + "Image"));
        Assert.Equal("Database", image.Element(rdl + "Source")!.Value);
        Assert.Equal("image/png", image.Element(rdl + "MIMEType")!.Value);
        Assert.Equal("FitProportional", image.Element(rdl + "Sizing")!.Value);
        var tablix = Assert.Single(r.Descendants(rdl + "Tablix"));
        var columnCount = tablix.Descendants(rdl + "TablixColumn").Count();
        Assert.Equal(template.Contains("A4") ? 7 : 2, columnCount);
        Assert.All(tablix.Descendants(rdl + "TablixRow"), row =>
            Assert.Equal(columnCount, row.Element(rdl + "TablixCells")!.Elements(rdl + "TablixCell").Count()));
        Assert.Contains(tablix.Descendants(rdl + "RepeatOnNewPage"), x => x.Value == "true");
        Assert.All(r.Descendants(rdl + "Color"), x => Assert.Equal("#000000", x.Value));
        Assert.False(expressions.Any(x => x.Contains("TAX INVOICE",
            StringComparison.OrdinalIgnoreCase)), "Unapproved legal invoice text must not render.");
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
        Assert.Equal(new[] { "BusinessName", "ReceiptNumber", "DateText", "TimeText",
            "CurrencyCode", "PaymentMethod", "TotalAmount", "CashReceived",
            "ChangeAmount", "FooterText", "MerchantLegalName", "MerchantAddress", "MerchantPhone", "MerchantEmail", "CustomerName", "CustomerPhone", "CustomerAddress", "LocationName", "MerchantLogo", "CreditDueDateText" }, fields["InvoiceHeader"]);
        Assert.Equal(new[] { "LineNumber", "ProductName", "Quantity", "UnitPrice",
            "LineTotal", "ProductSku", "UnitCode" }, fields["InvoiceLines"]);
    }

    [Theory]
    [InlineData("Thermal80", 1, true)]
    [InlineData("Thermal80", 3, true)]
    [InlineData("Thermal80", 10, false)]
    [InlineData("Thermal80", 50, true)]
    [InlineData("Thermal80", 100, false)]
    [InlineData("Thermal80", 5, true)]
    [InlineData("A4", 1, true)]
    [InlineData("A4", 10, true)]
    [InlineData("A4", 50, false)]
    [InlineData("A4", 100, false)]
    [Trait("Renderer", "Windows")]
    public void Static_templates_render_real_pdf_on_windows(string paper, int count, bool show) =>
        RenderSample(paper, count, show);

    // CI invokes each of these methods in a separate test-host process.
    [Fact]
    [Trait("Renderer", "ColdWindows")]
    public void Cold_start_A4() => RenderSample("A4", 1, true);

    [Fact]
    [Trait("Renderer", "ColdWindows")]
    public void Cold_start_Thermal80_short() => RenderSample("Thermal80", 3, true);

    [Fact]
    [Trait("Renderer", "ColdWindows")]
    public void Cold_start_Thermal80_long() => RenderSample("Thermal80", 5, true);

    [Fact]
    [Trait("Renderer", "Windows")]
    public void Warm_up_renders_both_static_templates()
    {
        if (!OperatingSystem.IsWindows()) return;
        RetailReceiptRenderer.WarmUp();
    }

    private static void RenderSample(string paper, int count, bool show)
    {
        if (!OperatingSystem.IsWindows()) return;
        var business = new Business { Id = 42, Name = "Demo Retail Store",
            ReceiptShowSku = show, ReceiptShowUnit = show, ReceiptShowUnitPrice = show,
            ReceiptShowLogo = false, ReceiptShowCustomerPhone = show };
        var sale = new RetailCashSale { Id = 77, BusinessId = 42, CurrencyCode = "INR",
            Total = count * 10m, Tendered = count * 10m,
            PostedAtUtc = new DateTime(2026, 11, 24, 10, 0, 0, DateTimeKind.Utc),
            Lines = Enumerable.Range(1, count).Select(i => new RetailCashSaleLine {
                Id = i, ProductName = "Example product " + i, Quantity = 1m,
                UnitPrice = 10m, LineTotal = 10m, ProductSku = "SKU-" + i, UnitCode = "PCS"
            }).ToList() };
        if (paper == "Thermal80" && count == 5)
        {
            sale.MerchantAddress = string.Join(" ", Enumerable.Repeat("Long business address", 10));
            sale.CustomerName = "Sample Customer";
            sale.CustomerAddress = string.Join(" ", Enumerable.Repeat("Long customer address", 10));
            sale.MerchantLogo = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+j2ZsAAAAASUVORK5CYII=");
            // Existing PNG validation is tested independently; keep the sizing sample deterministic.
            business.ReceiptShowLogo = false;
            foreach (var line in sale.Lines)
                line.ProductName += " " + string.Join(" ", Enumerable.Repeat("Long product description", 6));
        }
        var data = RetailReceiptReportData.FromCashSale(sale, business, TimeZoneInfo.Utc);
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var bytes = RetailReceiptRenderer.Render(data, paper);
        var firstMs = stopwatch.Elapsed.TotalMilliseconds;
        stopwatch.Restart();
        var repeat = RetailReceiptRenderer.Render(data, paper);
        var repeatMs = stopwatch.Elapsed.TotalMilliseconds;
        Assert.True(bytes.AsSpan().StartsWith("%PDF-"u8));
        Assert.True(bytes.Length > 1000);
        Assert.True(repeat.AsSpan().StartsWith("%PDF-"u8));
        Assert.True(repeat.Length > 1000);

        // Synthetic samples only; no customer or tenant production data in CI artifacts.
        var output = Environment.GetEnvironmentVariable("RDLC_TEST_OUTPUT");
        if (!string.IsNullOrWhiteSpace(output))
        {
            Directory.CreateDirectory(output);
            var name = $"{paper}-{count:D3}-items";
            File.WriteAllBytes(Path.Combine(output, name + ".pdf"), bytes);
            File.WriteAllText(Path.Combine(output, name + ".json"),
                System.Text.Json.JsonSerializer.Serialize(new {
                    paper, count, optionalColumnsShown = show, firstMs, repeatMs,
                    processId = Environment.ProcessId,
                    bytes = bytes.Length, repeatBytes = repeat.Length
                }));
        }
    }

    [Theory]
    [InlineData("A4")]
    [InlineData("Thermal80")]
    [Trait("Renderer", "Windows")]
    public void Credit_invoice_prints_original_unpaid_amount_and_mandatory_customer(string paper)
    {
        var business = new Business { Id = 42, Name = "Demo Retail Store",
            ReceiptShowCustomerName = false, ReceiptShowCashReceived = true, ReceiptShowChange = true };
        var sale = new RetailCashSale { Id = 79, BusinessId = 42, CustomerId = 9, CustomerName = "Credit Customer",
            CreditDueDate = new DateOnly(2026, 12, 2), CurrencyCode = "INR", Total = 440m, Tendered = 0m,
            PostedAtUtc = new DateTime(2026, 11, 25, 4, 0, 0, DateTimeKind.Utc),
            Lines = [new RetailCashSaleLine { Id = 1, ProductName = "Packed Water", Quantity = 2m,
                UnitCode = "BOX", UnitPrice = 220m, LineTotal = 440m }] };
        var data = RetailReceiptReportData.FromCashSale(sale, business, TimeZoneInfo.Utc);
        Assert.Equal("Credit", data.Header.PaymentMethod);
        Assert.Equal("02 Dec 2026", data.Header.CreditDueDateText);
        Assert.Equal(440m, data.Header.TotalAmount);
        Assert.Equal(0m, data.Header.ChangeAmount);
        Assert.True(data.Parameters.ShowCustomerName);
        Assert.False(data.Parameters.ShowCashReceived);
        Assert.False(data.Parameters.ShowChange);
        if (!OperatingSystem.IsWindows()) return;
        var pdf = RetailReceiptRenderer.Render(data, paper);
        Assert.True(pdf.AsSpan().StartsWith("%PDF-"u8));
        var output = Environment.GetEnvironmentVariable("RDLC_TEST_OUTPUT");
        if (!string.IsNullOrWhiteSpace(output))
        {
            var folder = Path.Combine(output, "credit");
            Directory.CreateDirectory(folder);
            File.WriteAllBytes(Path.Combine(folder, paper + "-credit.pdf"), pdf);
        }
    }

    [Theory]
    [InlineData("A4")]
    [InlineData("Thermal80")]
    [Trait("Renderer", "Windows")]
    public void Converted_sale_renders_selected_unit_rate_and_amount(string paper)
    {
        if (!OperatingSystem.IsWindows()) return;
        var business = new Business { Id = 42, Name = "Demo Retail Store",
            ReceiptShowUnit = true, ReceiptShowUnitPrice = true, ReceiptShowSku = true };
        var sale = new RetailCashSale { Id = 78, BusinessId = 42, CurrencyCode = "INR",
            Total = 440m, Tendered = 500m, PostedAtUtc = new DateTime(2026, 11, 25, 4, 0, 0, DateTimeKind.Utc),
            Lines = [new RetailCashSaleLine { Id = 1, ProductName = "Packed Water", ProductSku = "WATER-01",
                SelectedUnitId = 41, UnitCode = "BOX", BaseUnitCode = "PCS", ConversionFactor = 24m,
                Quantity = 2m, UnitPrice = 220m, LineTotal = 440m }] };
        var data = RetailReceiptReportData.FromCashSale(sale, business, TimeZoneInfo.Utc);
        var line = Assert.Single(data.Lines);
        Assert.Equal("BOX", line.UnitCode);
        Assert.Equal(2m, line.Quantity);
        Assert.Equal(220m, line.UnitPrice);
        Assert.Equal(440m, line.LineTotal);
        Assert.Equal(60m, data.Header.ChangeAmount);
        var pdf = RetailReceiptRenderer.Render(data, paper);
        Assert.True(pdf.AsSpan().StartsWith("%PDF-"u8));
        Assert.True(pdf.Length > 1000);
        var output = Environment.GetEnvironmentVariable("RDLC_TEST_OUTPUT");
        if (!string.IsNullOrWhiteSpace(output))
        {
            var folder = Path.Combine(output, "converted");
            Directory.CreateDirectory(folder);
            File.WriteAllBytes(Path.Combine(folder, paper + "-converted.pdf"), pdf);
        }
    }

    [Fact]
    public void Receipt_contract_uses_posted_sale_snapshot_not_live_product_price()
    {
        var business = new Business {
            Id = 42, Name = "Retail Demo", ReceiptShowTime = false,
            ReceiptShowUnitPrice = false, ReceiptShowCashReceived = false,
            ReceiptShowChange = true, ReceiptShowFooter = false,
            ReceiptShowBranch = false, ReceiptShowSku = false, ReceiptShowUnit = false,
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
        Assert.False(report.Parameters.ShowBranch);
        Assert.False(report.Parameters.ShowSku);
        Assert.False(report.Parameters.ShowUnit);

        Assert.Equal("", report.Header.LocationName);
        Assert.All(report.Lines, line => {
            Assert.Equal("", line.ProductSku);
            Assert.Equal("", line.UnitCode);
        });
        Assert.Equal("", report.Header.MerchantAddress);
        sale.MerchantLegalName = "Original Legal Name";
        sale.MerchantAddress = "Original Address";
        sale.MerchantPhone = "1234567890";
        sale.MerchantEmail = "sales@example.test";
        business.ReceiptAddress = "New Address";
        business.ReceiptShowAddress = false;
        Assert.Equal("", report.Header.CustomerName);
        sale.CustomerId = 9;
        sale.CustomerName = "Original Customer";
        sale.CustomerPhone = "1234567890";
        sale.CustomerAddress = "Original Customer Address";
        business.ReceiptShowCustomerPhone = false;
        Assert.Null(report.Header.MerchantLogo);
        var oldLogo = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+j2ZsAAAAASUVORK5CYII=");
        Assert.True(ReceiptLogo.IsValid(oldLogo));
        Assert.False(ReceiptLogo.IsValid(new byte[32769]));
        Assert.False(ReceiptLogo.IsValid(new byte[50]));
        sale.MerchantLogo = oldLogo;
        business.ReceiptLogo = [1, 2, 3];
        business.ReceiptShowLogo = false;
        sale.BusinessName = "Original Store";
        sale.LocationName = "Original Branch";
        sale.TimeZoneId = "Asia/Kolkata";
        sale.Lines[0].ProductSku = "SOAP-01";
        sale.Lines[0].UnitCode = "PCS";
        sale.Lines[0].LineTotal = 70m;
        business.Name = "Renamed Store";
        var reprint = RetailReceiptReportData.FromCashSale(sale, business, TimeZoneInfo.Utc);
        Assert.Equal("Original Customer", reprint.Header.CustomerName);
        Assert.Equal("1234567890", reprint.Header.CustomerPhone);
        Assert.Equal("Original Customer Address", reprint.Header.CustomerAddress);
        Assert.False(reprint.Parameters.ShowCustomerPhone);
        Assert.Equal(oldLogo, reprint.Header.MerchantLogo);
        Assert.False(reprint.Parameters.ShowLogo);
        Assert.Equal("Original Store", reprint.Header.BusinessName);
        Assert.Equal("Original Legal Name", reprint.Header.MerchantLegalName);
        Assert.Equal("Original Address", reprint.Header.MerchantAddress);
        Assert.Equal("1234567890", reprint.Header.MerchantPhone);
        Assert.Equal("sales@example.test", reprint.Header.MerchantEmail);
        Assert.False(reprint.Parameters.ShowAddress);
        Assert.Equal("Original Branch", reprint.Header.LocationName);
        Assert.Equal(report.Header.TimeText, reprint.Header.TimeText);
        Assert.Equal("SOAP-01", reprint.Lines[1].ProductSku);
        Assert.Equal("PCS", reprint.Lines[1].UnitCode);
        Assert.Equal(70m, reprint.Lines[1].LineTotal);

        sale.BusinessId = 89;
        Assert.Throws<InvalidOperationException>(() =>
            RetailReceiptReportData.FromCashSale(sale, business, TimeZoneInfo.Utc));
        sale.BusinessId = 42;
        sale.Total = 1m;
        Assert.Throws<InvalidOperationException>(() =>
            RetailReceiptReportData.FromCashSale(sale, business, TimeZoneInfo.Utc));
    }
}
