# Business SaaS — Retail invoice / receipt RDLC design contract

**Status (24 Nov 2026):** Two source-controlled design-time RDLCs, typed XSD, snapshot DTO, owner print-preference API and Business Settings UI implemented. A Windows-only RDLC PDF adapter is connected in Development; actual render CI and physical thermal printing remain release gates. Cash POS remains Development-only. The templates are **retail cash receipts, not legally issued tax invoices**.

## Developer-owned files (no runtime layout generation)

| File | Responsibility |
| --- | --- |
| `backend/BusinessSaas.Api/Reports/RetailCashReceiptThermal80.rdlc` | Static 80mm receipt layout |
| `backend/BusinessSaas.Api/Reports/RetailCashReceiptA4.rdlc` | Static A4 retail receipt layout |
| `backend/BusinessSaas.Api/Reports/RetailReceiptDataSet.xsd` | Visual Studio designer DataSet: two exact tables, no SQL queries |
| `backend/BusinessSaas.Api/Reports/RetailReceiptReportData.cs` | Named C# records and mapper from immutable posted cash-sale values |
| `backend/BusinessSaas.Api/Features/RetailBillingSettingsEndpoints.cs` | Owner-only tenant-scoped print preferences, revision check, audit |
| `frontend/web/src/BusinessReceiptSettings.tsx` | Business Settings → Invoice & Print, high-contrast form and shared save feedback |
| `backend/BusinessSaas.Tests/RetailReceiptReportTests.cs` | XML, XSD, contract and money-snapshot tests |

`ReportViewerCore.NETCore` 15.1.33 is pinned for Windows PDF rendering. The user cannot upload/edit RDLC XML; an ordinary developer edits the two checked-in files using a compatible Visual Studio RDLC designer, reviews the XML diff and ships the reviewed change. **Do not** save templates in a database, generate XML strings at runtime, dynamically edit report definitions, or create per-tenant copies. Select between the two approved file names only.

## RDLC DataSets and field contract

`InvoiceHeader`: exactly **one** record: `BusinessName`, `ReceiptNumber`, `DateText`, `TimeText`, `CurrencyCode`, `PaymentMethod`, `TotalAmount`, `CashReceived`, `ChangeAmount`, `FooterText`, `MerchantLegalName`, `MerchantAddress`, `MerchantPhone`, `MerchantEmail`, `CustomerName`, `CustomerPhone`, `CustomerAddress`, `LocationName`, `MerchantLogo` (nullable `base64Binary` / byte array).

`InvoiceLines`: **one or more** rows with `LineNumber`, `ProductName`, `Quantity`, `UnitPrice`, `LineTotal`, `ProductSku`, `UnitCode`. `LineTotal` uses the same two-decimal midpoint-away-from-zero rule as POS posting. The backend verifies that the stored line totals sum exactly to the posted sale total before creating the report data. Printed monetary fields are never recomputed from the current Product table.

The rendering adapter binds these **exact DataSet names** and pass these sixteen strongly named **Boolean report parameters**:

| RDLC parameter | Owner setting | Behavior |
| --- | --- | --- |
| `ShowTime` | `showTime` | Add local time after the **mandatory** date |
| `ShowUnitPrice` | `showUnitPrice` | Show rate column on A4, or rate within description on 80mm |
| `ShowCashReceived` | `showCashReceived` | Show tender amount row |
| `ShowChange` | `showChange` | Show change row |
| `ShowFooter` | `showFooter` | Show configured footer row |
| `ShowBranch` | `showBranch` | Show historical location row when available |
| `ShowSku` | `showSku` | Show SKU column on A4 or SKU line on 80mm |
| `ShowUnit` | `showUnit` | Show unit column on A4 or inline unit on 80mm |
| `ShowLegalName` | `showLegalName` | Legal display name |
| `ShowAddress` | `showAddress` | Merchant address |
| `ShowPhone` | `showPhone` | Merchant phone |
| `ShowEmail` | `showEmail` | Merchant email |
| `ShowCustomerName` | `showCustomerName` | Selected customer name |
| `ShowCustomerPhone` | `showCustomerPhone` | Selected customer phone |
| `ShowCustomerAddress` | `showCustomerAddress` | Selected customer address |
| `ShowLogo` | `showLogo` | Historical logo (PNG) |

The selection `paperSize = Thermal80 | A4` determines which **fixed** `.rdlc` the adapter loads. Mandatory fields are **always visible**: business name, receipt/reference number, date, item descriptions/quantities, line amounts and total payable. The Owner may not hide these or alter RDLC expressions via an API.

**Snapshot rule:** `ReceiptNumber` is currently the internal cash receipt ID, **not** a registered statutory invoice number. New cash sales snapshot business display name, location, timezone, SKU, unit and line total in the posting transaction. Legacy rows remain nullable (no fabricated master-data backfill); they retain the legacy display-name and timezone fallback. New sales also snapshot optional merchant name/address/contact, logo and selected customer details. Statutory merchant registration and tax remain pending. The templates intentionally contain no GST/CGST/SGST/HSN claims, discounts, customer credit balance or fake QR codes because these business fields are not yet posted on RetailCashSale.

## Owner settings and access

- GET/PUT `/api/businesses/{businessId}/receipt-settings`. Only a Retail Business Owner may read/change them; unauthorized/cross-tenant resources return 404.
- The database saves paper selection, 1–5 print copies, 16 display flags, optional merchant details and logo, footer text (maximum 120 characters) and a separate monotonic `ReceiptSettingsRevision`. These settings cannot change monetary values or historical sale records.
- A PUT requires the expected revision and 10–200 character audit reason, and is blocked for inactive subscriptions. Server conflict (409) or ambiguous write response requires a fresh GET before another save.
- The former HTML print implementation is removed. Windows Development hosts expose the read-only receipt PDF action; unsupported hosts show an availability message.

## Real printing — separate release gate

The adapter loads the two embedded static RDLC files and the existing XSD, maps the posted data explicitly and returns a PDF through the same cashier/owner receipt authorization rules. Linux PDF rendering is intentionally not enabled. Don't load RDLC from a user-supplied path. Never query Product price at render time. Never trigger sale/stock mutations from print or reprint.

The thermal adapter must handle **continuous paper length correctly**: RDLC page height and printer driver can introduce long blank tails. Do not ship until a physical 80mm printer test confirms that 1, 10, 50 and 100 item receipts print without truncation, empty pages, repeated headings, 3-meter blank tails or wrong cutter feeds. Measure warm/cold rendering, total spool latency, memory and multi-cashier contention; `CI PASS` alone does not establish fast printing. A4 page breaks and footer placement must also be tested. PDF and printer rendering must be compared against committed source totals; printing failure allows **reprint only**, never a second sale.

## Future capabilities (not silently enabled)

Tax/GST invoice registration, statutory numbering, verified statutory merchant registration/GSTIN, discount snapshots, Credit/Refund, QR, tax breakup and historical settings snapshot require separate SRS/design approval and end-to-end posting. Compliance-required fields are never controlled by a cosmetic visibility switch. Follow the existing `docs/UX_STANDARDS.md` shared Save/Success/Failure/Unknown states; no duplicate popup or second printing framework.

## Layout and data details

A4 uses seven fixed columns (number, product, SKU, quantity, unit, rate, amount), with optional columns controlled by tablix-member visibility. Thermal uses two compact columns with optional inline SKU, unit and rate. Missing or hidden header/footer rows collapse through static visibility. Black text and borders on white paper; item headings are marked to repeat on new pages. Actual render, Designer compatibility and printer feed remain release gates.

Walk-in remains the default. Optional active customers are selected through a location-authorized, tenant-scoped search. Customer ID is preserved in pending checkout and compared on replay. New sales retain historical customer name, phone and address. No invented data is backfilled into older sales. The on-screen receipt uses stored business name and timezone when available. It is a read-only view, not a print fallback.

Logo uploads are resized in the browser to PNG (maximum 384 × 192 pixels, 32 KiB). The server bounds and checks the PNG envelope; actual decoder validation is still required before rendering. Omitted logo preserves existing data; an empty byte array removes it. New sales snapshot logo bytes. Template choice selects the actual RDLC. Print copies remain a preference to select in the native PDF print dialog; silent printing and automatic copy count are not implemented.

Receipt settings use an explicit revision-guarded update and read the stored row back before committing. No-op writes do not change the revision. Empty legacy merchant fields are not rewritten just because a client submits empty strings. The frontend omits unchanged logo bytes.

Windows renderer tests execute real PDF generation for both templates. The thermal RDLC has an 11-inch design-time page; the renderer overrides output height using DeviceInfo and measured text wrapping. Real roll paper length and cutter verification are not complete. No browser HTML print fallback is provided.

## Render verification artifacts

The Windows CI job publishes actual synthetic A4 (1, 10, 50, 100 items) and Thermal80 (1, 3, 10, 50, 100 items) PDF samples with per-case initial and repeated render duration and byte counts. These are process-level measurements, not isolated cold-start or printer-spool benchmarks. Review the PDF page dimensions, optional columns and wrapping before tuning thermal pagination. The original synthetic three-item PDF occupied 81.2 mm of a 279.4 mm page; variable output height now uses measured text wrapping and a bounded allowance. CI checks actual PDF dimensions, item completeness, total visibility and short receipt paper length. A4 output is unchanged. The application pins the XML cryptography dependency to 10.0.14 to replace the vulnerable transitive 10.0.0 reported by CI.

## Checkout print behavior

Owner Invoice & Print Settings stores `autoOpen` per business (existing tenants default to manual). The dashboard supplies the effective setting when a Windows development report host is available. In automatic mode the payment primary action is Save & Print; Save Only remains available. The PDF opens only after a successful POST; blocked or closed tabs fall back to the existing read-only receipt link. Recovery never auto-prints, so uncertain writes cannot produce surprise copies. Existing cashier sessions receive changed settings on dashboard reload; the owner refreshes dashboard after saving. Browser confirmation and printer copies still require the native PDF dialog. No silent printing or HTML receipt path is added.
