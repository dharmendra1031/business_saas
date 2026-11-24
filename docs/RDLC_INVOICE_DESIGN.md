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

### Direct printing boundary and approved behavior

Single-click silent printing is an optional third mode, controlled by the Business Owner in Invoice & Print Settings. The final modes are mutually exclusive:

| Mode | Payment action | Receipt transport |
| --- | --- | --- |
| Manual | Save Only | Existing authorized RDLC PDF link on demand |
| Preview | Save & Print (or Save Only) | Open existing RDLC PDF after confirmed posting |
| Silent | Save & Print (or Save Only) | Configured workstation printer; no preview or confirmation |

Manual remains the default for existing businesses. The paper format, optional fields and requested copies use the existing receipt settings and the same static RDLC datasets. A printer is a workstation-specific resource, not a SaaS tenant-wide Windows default. Silent mode must only become selectable after the workstation's authorized printer integration is configured and a test job succeeds. Do not expose a nonfunctional setting or persist a second pair of mode booleans; introduce a single three-value mode when the actual silent transport is ready, migrating the existing AutoOpen preference. No new receipt template, reporting engine, generic plugin framework or speculative print-job database is needed.

A hosted server's installed printers are not the cashier's USB printers. Microsoft documents that System.Drawing.Printing and System.Printing are unsupported inside ASP.NET applications and Windows services. Do not add an arbitrary printer endpoint, shell print commands or a fake silent mode. Use a supported, explicitly authorized workstation print mechanism for the actual installation; keep printer selection local to that workstation and never accept arbitrary printer paths from a hosted API. A local Windows on-premise deployment and a hosted deployment must both keep the same RDLC output.

After a committed sale, printing failures are independent of sale recovery. Do not auto-retry a print with an ambiguous submission result. Display whether the job was submitted (not that paper physically printed), retain the receipt ID and let an operator reprint intentionally. Never repost a sale for printing. A reprint must use the original posted amounts and the same authorization checks. Recovery of an uncertain sale does not auto-print. The Save Only action must never submit a print job.

Release gates: physical test using the selected 80mm driver, correct paper width/variable length and cutter behavior; exact configured copy count; preview and manual modes unchanged; no popup or confirmation in supported silent mode; offline/disconnected printer and ambiguous submission; unauthorized or cross-business receipt blocked; one sale on repeated payment clicks. Silent transport is not implemented or released yet. Existing PDF preview and manual behavior remain available. Browser QA checks blocked popup and Save Only with automatic preview enabled.

Reference: [Microsoft System.Drawing.Printing support boundary](https://learn.microsoft.com/en-us/dotnet/api/system.drawing.printing).

### Preferred implementation: managed Chrome silent printing (24 November 2026)

User-approved direction: first test the browser's built-in silent printing policy on a dedicated Windows POS workstation with the existing connected printer. Do not introduce a local print agent, browser extension, separate print service, printer master, new reporting engine or duplicate HTML invoice. Edge is an alternative, not a separate application architecture.

Chrome 144 or later supports SilentPrintingEnabled. Set the connected thermal printer as the Windows default and turn off automatic Windows default-printer management. Set its driver paper size, scale and cutter options correctly. An authorized workstation administrator can set the following policies in an elevated PowerShell session:

```powershell
reg add "HKLM\SOFTWARE\Policies\Google\Chrome" /v SilentPrintingEnabled /t REG_DWORD /d 1 /f
reg add "HKLM\SOFTWARE\Policies\Google\Chrome" /v PrintPreviewUseSystemDefaultPrinter /t REG_DWORD /d 1 /f
```

Restart Chrome and inspect chrome://policy for both enabled policies and successful policy status. The policy applies to the browser, not just this SaaS origin; use a dedicated trusted POS workstation and do not overwrite existing organization-managed policy without approval. To undo a locally added policy, restore the prior values (or delete only the values added by this setup if no prior values existed).

Target flow: Save & Print -> committed sale -> existing authorized RDLC PDF -> automatic PDF print trigger -> browser silent printing -> configured Windows default printer. Browser policy alone does not trigger printing. Test the actual PDF viewer/trigger with Chrome before choosing an implementation; do not assume iframe load or window.print reliably prints a PDF rather than the surrounding web page. No old HTML printing fallback. A standard web application cannot reliably read these workstation policy values or confirm physical print completion. Keep Manual, Preview and Silent modes as owner-controlled business preferences; validate workstation setup before enabling silent mode. If the policy is enabled, printing a preview may also be silent when the operator invokes Print; changing application mode cannot turn off machine policy. Existing copies settings must be verified with default driver settings; arbitrary per-job copy control is not guaranteed by the browser policy. Validate both paper types, print count, popup behavior, cancellation, disconnection and intentional reprint without reposting the sale.

Status: preferred approach documented, not implemented or physically verified. No registry settings have been changed by this repository update.

Official references:
- https://chromeenterprise.google/policies/silent-printing-enabled/
- https://chromeenterprise.google/policies/print-preview-use-system-default-printer/
- https://support.google.com/chrome/a/answer/2657289?hl=en
- https://learn.microsoft.com/en-us/deployedge/microsoft-edge-browser-policies/silentprintingenabled

### Workstation pilot (development only)

A single standalone test page, `frontend/web/chrome-rdlc-print-pilot.html`, exercises the browser PDF-frame print trigger without touching checkout, inventory, or the API. Vite's existing development server serves it; it is not a production build entry. No dependency or runtime report engine is added. This is a temporary compatibility test, not an invoice template or alternative print implementation. Remove it once the print transport is selected and the result recorded.

1. Pull development and use the existing frontend development server (`npm run dev` from `frontend/web`).
2. Download and extract the synthetic `rdlc-receipt-samples` artifact from the latest successful Build workflow. Use `Thermal80-003-items.pdf` first. Verify the default physical printer and the Chrome policies documented above.
3. In Chrome open `http://127.0.0.1:5173/chrome-rdlc-print-pilot.html` (adjust port if needed), select the sample and click **Load PDF & request print once**. This loads the actual PDF and attempts the print only on its frame after the load event. It must not print the pilot page. There is no automatic retry or claim of print success.
4. Confirm one correct three-item receipt, no dialog, correct width, readable text, no blank tail or clipped totals, and correct cutter feed. Check printer queue and elapsed time. Repeat with A4 and the long receipt, using a suitable default printer and a deliberate page reload between tests. Test disconnected printer and driver copy preferences separately. The iframe trigger is browser-dependent: a load event alone does not prove the viewer is ready; if the attempt fails or prints the wrong content, keep the existing Manual/Preview modes and do not ship the trigger.

Chrome uses default printer settings in silent mode; application-specific copy count or dialog suppression is not assured by JavaScript. Current application modes and schema are unchanged until this test passes on the supported workstation. The pilot must not be used on live customer data.


### Receipt performance baseline and diagnostics

Build 45441364250 (commit `62035ed`, Windows synthetic samples) recorded initial per-case rendering times of 137.0–244.6 ms for nine cases and 3246.9 ms for the five-item thermal case with long descriptions/addresses. The tests do not isolate a fresh process for every case, so the slow sample is not proven to be a cold start or a layout bottleneck. The CI geometry check now prints both initial and repeat durations; do not infer a guaranteed sub-second SLA from these samples.

The authorized PDF endpoint reports `Server-Timing` durations in milliseconds for `receipt-db` (authorization and data retrieval), `receipt-map`, and `receipt-render`. These exclude HTTP transfer, PDF viewer startup, spooler and physical printer time. The immutable receipt and its lines are loaded using a split query to avoid repeating historical header/logo bytes for each line. This adds a database round trip; compare staging latency for short and long receipts before claiming a speed improvement. No generated report, tenant data cache, printer agent or report template changes are introduced. Use synthetic receipts, compare cold/warm process measurements, and separately measure sale POST and print latency on the actual Windows workstation. Retain existing security, money and reprint acceptance gates.
