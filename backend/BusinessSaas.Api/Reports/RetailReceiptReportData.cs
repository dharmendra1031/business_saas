using System.Globalization;
using BusinessSaas.Api.Data;

namespace BusinessSaas.Api.Reports;

// Contract for the two static RDLC datasets in RetailReceiptDataSet.xsd.
// There is intentionally NO report-engine, SQL query or RDLC XML generation here.
public sealed record RetailReceiptHeader(
    string BusinessName, string ReceiptNumber, string DateText, string TimeText,
    string CurrencyCode, string PaymentMethod, decimal TotalAmount,
    decimal CashReceived, decimal ChangeAmount, string FooterText);

public sealed record RetailReceiptLine(
    int LineNumber, string ProductName, decimal Quantity,
    decimal UnitPrice, decimal LineTotal);

public sealed record RetailReceiptParameters(
    bool ShowTime, bool ShowUnitPrice, bool ShowCashReceived,
    bool ShowChange, bool ShowFooter);

public sealed record RetailReceiptReportData(
    RetailReceiptHeader Header,
    IReadOnlyList<RetailReceiptLine> Lines,
    RetailReceiptParameters Parameters)
{
    // Only historical transaction values feed the financial rows.
    // BusinessName is current display identity until legal invoice snapshots are implemented.
    public static RetailReceiptReportData FromCashSale(
        RetailCashSale sale, Business business, TimeZoneInfo timeZone)
    {
        ArgumentNullException.ThrowIfNull(sale);
        ArgumentNullException.ThrowIfNull(business);
        ArgumentNullException.ThrowIfNull(timeZone);
        if (sale.BusinessId != business.Id)
            throw new InvalidOperationException("Cross-business receipt access is not allowed.");
        if (sale.Lines.Count == 0)
            throw new InvalidOperationException("Cannot print a receipt without posted sale lines.");

        var lines = sale.Lines.OrderBy(x => x.Id)
            .Select((line, index) => new RetailReceiptLine(
                index + 1, line.ProductName, line.Quantity, line.UnitPrice,
                decimal.Round(line.Quantity * line.UnitPrice, 2, MidpointRounding.AwayFromZero)))
            .ToArray();
        if (lines.Sum(x => x.LineTotal) != sale.Total ||
            sale.Tendered < sale.Total)
            throw new InvalidOperationException("Posted receipt amounts require reconciliation.");

        var local = TimeZoneInfo.ConvertTimeFromUtc(
            DateTime.SpecifyKind(sale.PostedAtUtc, DateTimeKind.Utc), timeZone);
        var header = new RetailReceiptHeader(
            business.Name, sale.Id.ToString(CultureInfo.InvariantCulture),
            local.ToString("dd MMM yyyy", CultureInfo.InvariantCulture),
            local.ToString("hh:mm tt", CultureInfo.InvariantCulture),
            sale.CurrencyCode, "Cash", sale.Total, sale.Tendered,
            sale.Tendered - sale.Total, business.ReceiptFooterText);
        return new RetailReceiptReportData(header, lines,
            new RetailReceiptParameters(
                business.ReceiptShowTime, business.ReceiptShowUnitPrice,
                business.ReceiptShowCashReceived, business.ReceiptShowChange,
                business.ReceiptShowFooter));
    }
}
