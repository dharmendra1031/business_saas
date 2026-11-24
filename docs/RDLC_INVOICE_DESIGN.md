# Business SaaS — Retail invoice / receipt RDLC design contract

**Status (24 Nov 2026):** Two source-controlled design-time RDLCs, typed XSD, snapshot DTO, owner print-preference API and Business Settings UI implemented. **RDLC rendering and real thermal printing are NOT integrated or benchmarked.** Cash POS remains Development-only. The templates are **retail cash receipts, not legally issued tax invoices**.

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

**No report renderer or new NuGet package is installed.** The user cannot upload/edit RDLC XML; an ordinary developer edits the two checked-in files using a compatible Visual Studio RDLC designer, reviews the XML diff and ships the reviewed change. **Do not** save templates in a database, generate XML strings at runtime, dynamically edit report definitions, or create per-tenant copies. Select between the two approved file names only.

## RDLC DataSets and field contract

`InvoiceHeader`: exactly **one** record supplied from the posted transaction with `BusinessName`, `ReceiptNumber`, `DateText`, `TimeText`, `CurrencyCode`, `PaymentMethod`, `TotalAmount`, `CashReceived`, `ChangeAmount` and `FooterText`.

`InvoiceLines`: **one or more** rows with `LineNumber`, `ProductName`, `Quantity`, `UnitPrice`, `LineTotal`. `LineTotal` uses the same two-decimal midpoint-away-from-zero rule as POS posting. The backend verifies that the stored line totals sum exactly to the posted sale total before creating the report data. Printed monetary fields are never recomputed from the current Product table.

A future, explicitly reviewed rendering adapter needs only to bind these **exact DataSet names** and pass these twelve strongly named **Boolean report parameters**:

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

The selection `paperSize = Thermal80 | A4` determines which **fixed** `.rdlc` the adapter loads. Mandatory fields are **always visible**: business name, receipt/reference number, date, item descriptions/quantities, line amounts and total payable. The Owner may not hide these or alter RDLC expressions via an API.

**Snapshot rule:** `ReceiptNumber` is currently the internal cash receipt ID, **not** a registered statutory invoice number. New cash sales snapshot business display name, location, timezone, SKU, unit and line total in the posting transaction. Legacy rows remain nullable (no fabricated master-data backfill); they retain the legacy display-name and timezone fallback. Optional merchant name/address/contact snapshots are now supported; statutory merchant registration, tax and customer snapshots remain pending. The templates intentionally contain no GST/CGST/SGST/HSN claims, discounts, customer credit balance or fake QR codes because these business fields are not yet posted on RetailCashSale.

## Owner settings and access

- GET/PUT `/api/businesses/{businessId}/receipt-settings`. Only a Retail Business Owner may read/change them; unauthorized/cross-tenant resources return 404.
- The database saves paper selection, each display flag, footer text (maximum 120 characters) and a separate monotonic `ReceiptSettingsRevision`. These settings cannot change monetary values or historical sale records.
- A PUT requires the expected revision and 10–200 character audit reason, and is blocked for inactive subscriptions. Server conflict (409) or ambiguous write response requires a fresh GET before another save.
- UI clearly says that **these preferences are persisted but not yet connected to the current browser `window.print()` flow**. No pretending that RDLC print is live.

## Real printing — separate release gate

Rendering necessarily needs a small runtime integration **later**; the prohibition is on runtime report-definition generation or hidden/magical binding. Before enabling, choose and pin an engine compatible with the actual .NET 10 hosting OS, then implement one explicit read-only adapter for the two DataSets and parameters. Don't load RDLC from a user-supplied path. Never query Product price at render time. Never trigger sale/stock mutations from print or reprint.

The thermal adapter must handle **continuous paper length correctly**: RDLC page height and printer driver can introduce long blank tails. Do not ship until a physical 80mm printer test confirms that 1, 10, 50 and 100 item receipts print without truncation, empty pages, repeated headings, 3-meter blank tails or wrong cutter feeds. Measure warm/cold rendering, total spool latency, memory and multi-cashier contention; `CI PASS` alone does not establish fast printing. A4 page breaks and footer placement must also be tested. PDF and printer rendering must be compared against committed source totals; printing failure allows **reprint only**, never a second sale.

## Future capabilities (not silently enabled)

Tax/GST invoice registration, statutory numbering, verified statutory merchant registration/GSTIN, discount snapshots, customer identity, Credit/Refund, logo/QR, tax breakup and historical settings snapshot require separate SRS/design approval and end-to-end posting. Compliance-required fields are never controlled by a cosmetic visibility switch. Follow the existing `docs/UX_STANDARDS.md` shared Save/Success/Failure/Unknown states; no duplicate popup or second printing framework.

## Receipt snapshot dataset extension

`InvoiceHeader` additionally contains `LocationName`; `InvoiceLines` additionally contains `ProductSku` and `UnitCode`. Both RDLC definitions and the XSD expose these fields with string types; legacy absent values are empty. New line totals are persisted at posting; legacy line totals are derived from stored quantity and price. Owner layout settings now include `ShowBranch`, `ShowSku` and `ShowUnit`; all three are persisted and consumed by static RDLC parameters. No renderer or statutory invoice activation is part of this change.

## Phase 2 layout

A4 has seven fixed design-time columns (number, product, SKU, quantity, unit, rate, amount). Optional columns use static tablix-member visibility; thermal uses two compact columns and inline optional fields. Branch row collapses when hidden or absent. All text and table borders are black, paper background white. Item headings are marked to repeat on new pages. The fixed thermal page height is unchanged until actual driver/renderer validation. Actual visual rendering, Designer compatibility and printer feed remain release gates.

## Phase 3A merchant details

The existing Owner receipt-settings endpoint saves optional legal display name, address, phone and email with independent visibility flags, bounded lengths and email validation. New cash sales snapshot these four text fields in the same transaction; legacy receipts never substitute the current merchant details. Both RDLC files and XSD expose the fields and collapse empty/hidden rows. Existing clients may omit the new settings without resetting them. Audit summaries remain below 100 characters and omit raw contact details. These are optional cash-receipt identity fields, not verified statutory tax details. Logo upload, customer selection and customer snapshots are still pending. No renderer is installed.

## Phase 3B customer details

Walk-in cash checkout remains the default. An optional active customer is selected through a location-authorized, tenant-scoped POS search. The same customer ID is retained in the pending request and checked on idempotent replay. New sales snapshot customer name, phone and address; old receipts have no fabricated customer details. Customer Master now supports an optional address. The static XSD/RDLC definitions and Owner settings expose three independent customer visibility flags. No credit, tax or renderer is activated. Logo upload is a separate milestone.

## Phase 4A: logo and print preferences

Owner receipt settings support an optional PNG logo (maximum 384 × 192 pixels, 32 KiB) and 1–5 print copies. The browser resizes PNG/JPEG inputs and exports PNG. The server bounds and checks the PNG envelope; this does not replace actual decoder validation before enabling a renderer. Null request logo preserves the saved value; an empty byte array removes it. New sales snapshot the bounded image bytes; older receipts remain unchanged. Static RDLC database images bind to `MerchantLogo` (XSD `base64Binary`), with `ShowLogo` and missing-image visibility. Paper size and copies remain preferences only until printer integration. No runtime report generation, external image URL, or renderer is added.
