using System.Globalization;
using BusinessSaas.Api.Data;

namespace BusinessSaas.Api.Reports;

// Contract for the two static RDLC datasets in RetailReceiptDataSet.xsd.
// There is intentionally NO report-engine, SQL query or RDLC XML generation here.
public sealed record RetailReceiptHeader(
    string BusinessName, string ReceiptNumber, string DateText, string TimeText,
    string CurrencyCode, string PaymentMethod, decimal TotalAmount,
    decimal CashReceived, decimal ChangeAmount, string FooterText, string MerchantLegalName, string MerchantAddress, string MerchantPhone, string MerchantEmail,
    string CustomerName, string CustomerPhone, string CustomerAddress, string LocationName, byte[]? MerchantLogo,
    string CreditDueDateText = "", string OriginalSaleNumber = "", string ReturnNumber = "",
    decimal ReturnValue = 0m, decimal NetSettlement = 0m);

public sealed record RetailReceiptLine(
    int LineNumber, string ProductName, decimal Quantity,
    decimal UnitPrice, decimal LineTotal, string ProductSku, string UnitCode);

public sealed record RetailReceiptParameters(
    bool ShowTime, bool ShowUnitPrice, bool ShowCashReceived,
    bool ShowChange, bool ShowFooter, bool ShowBranch, bool ShowSku, bool ShowUnit,
    bool ShowLegalName, bool ShowAddress, bool ShowPhone, bool ShowEmail,
    bool ShowCustomerName, bool ShowCustomerPhone, bool ShowCustomerAddress, bool ShowLogo);

public sealed record RetailReceiptReportData(
    RetailReceiptHeader Header,
    IReadOnlyList<RetailReceiptLine> Lines,
    RetailReceiptParameters Parameters)
{
    // Only historical transaction values feed the financial rows.
    // Legacy receipts retain the existing merchant-name fallback; missing SKU/unit/location remain blank.
    public static RetailReceiptReportData FromCashSale(
        RetailCashSale sale, Business business, TimeZoneInfo timeZone, RetailCashReturn? exchange = null)
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
                line.LineTotal ?? decimal.Round(line.Quantity * line.UnitPrice, 2, MidpointRounding.AwayFromZero),
                line.ProductSku ?? "", line.UnitCode ?? ""))
            .ToArray();
        var credit = sale.CreditDueDate.HasValue;
        if (exchange is not null && (credit || exchange.BusinessId != sale.BusinessId ||
            exchange.ReplacementSaleId != sale.Id || exchange.SaleId == sale.Id || exchange.Total < 0m ||
            exchange.NetCashSettlement != sale.Total - exchange.Total ||
            sale.Tendered != Math.Max(0m, exchange.NetCashSettlement.GetValueOrDefault())))
            throw new InvalidOperationException("Posted exchange amounts require reconciliation.");
        if (lines.Sum(x => x.LineTotal) != sale.Total ||
            (credit ? sale.Tendered != 0m || !sale.CustomerId.HasValue || string.IsNullOrWhiteSpace(sale.CustomerName)
                    : exchange is null && sale.Tendered < sale.Total))
            throw new InvalidOperationException("Posted receipt amounts require reconciliation.");

        var receiptZone = sale.TimeZoneId is null
            ? timeZone : TimeZoneInfo.FindSystemTimeZoneById(sale.TimeZoneId);
        var local = TimeZoneInfo.ConvertTimeFromUtc(
            DateTime.SpecifyKind(sale.PostedAtUtc, DateTimeKind.Utc), receiptZone);
        var header = new RetailReceiptHeader(
            sale.BusinessName ?? business.Name, sale.Id.ToString(CultureInfo.InvariantCulture),
            local.ToString("dd MMM yyyy", CultureInfo.InvariantCulture),
            local.ToString("hh:mm tt", CultureInfo.InvariantCulture),
            sale.CurrencyCode, credit ? "Credit" : exchange is not null ? "Exchange" : "Cash", sale.Total, sale.Tendered,
            credit || exchange is not null ? 0m : sale.Tendered - sale.Total, business.ReceiptFooterText,
            sale.MerchantLegalName ?? "", sale.MerchantAddress ?? "", sale.MerchantPhone ?? "", sale.MerchantEmail ?? "",
            sale.CustomerName ?? "", sale.CustomerPhone ?? "", sale.CustomerAddress ?? "", sale.LocationName ?? "", sale.MerchantLogo,
            sale.CreditDueDate?.ToString("dd MMM yyyy", CultureInfo.InvariantCulture) ?? "",
            exchange?.SaleId.ToString(CultureInfo.InvariantCulture) ?? "",
            exchange?.Id.ToString(CultureInfo.InvariantCulture) ?? "",
            exchange?.Total ?? 0m, exchange?.NetCashSettlement ?? 0m);
        return new RetailReceiptReportData(header, lines,
            new RetailReceiptParameters(
                business.ReceiptShowTime, business.ReceiptShowUnitPrice,
                !credit && exchange is null && business.ReceiptShowCashReceived,
                !credit && exchange is null && business.ReceiptShowChange,
                business.ReceiptShowFooter, business.ReceiptShowBranch,
                business.ReceiptShowSku, business.ReceiptShowUnit,
                business.ReceiptShowLegalName, business.ReceiptShowAddress, business.ReceiptShowPhone, business.ReceiptShowEmail,
                credit || business.ReceiptShowCustomerName, business.ReceiptShowCustomerPhone, business.ReceiptShowCustomerAddress, business.ReceiptShowLogo));
    }
}
