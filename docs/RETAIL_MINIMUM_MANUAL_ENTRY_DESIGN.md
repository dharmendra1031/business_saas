# Retail V1 — Minimum Manual Entry and Purchase-Driven Workflow

**Status:** Reviewed design proposal; not an implementation or production release approval.  
**Date:** 2026-11-28  
**Audited baseline:** `a00112e713f181cb925cdc3375f6e603eb101814` (`development`).  
**Scope:** Retail V1; no changes to other business types, existing SRS, or main branch.  
**Related:** [Retail V1 SRS](./RETAIL_V1_SRS.md), [Database Architecture](./DATABASE_ARCHITECTURE.md), [Module Boundaries](./MODULE_BOUNDARIES.md), [Engineering Guardrails](./ENGINEERING_GUARDRAILS.md), [Production Database Safety](./PRODUCTION_DATABASE_SAFETY.md), [RDLC Invoice Design](./RDLC_INVOICE_DESIGN.md), [Offline Deployment](./OFFLINE_ON_PREMISE_DEPLOYMENT.md).

## 1. Objective and non-negotiable rules

The desired merchant journey is **provision business → receive goods → start selling**, or **provision business → initialize existing stock → start selling** for an existing shop. The Owner must not need to visit multiple setup screens before doing ordinary work. Reuse existing forms, services, authorization, transactions and feedback; expose only implemented features. Minimize typing and navigation, not essential decisions, financial controls, or verification.

- Platform Super Admin alone provisions businesses and assigns a verified Owner; merchant onboarding does not grant platform privileges.
- Business/Location authorization, subscription and feature release gates apply on the server. Manager and Cashier permissions must not expand as a side effect of a shorter flow.
- Never infer actual purchase quantities, supplier cost, payment receipt, stock counts or tax obligations from catalogue data.
- Keep one codebase (ASP.NET Core, EF Core, SQL Server, React/TypeScript); no new service, search engine, AI dependency or duplicate master for the initial release.
- Compact, accessible, high-contrast screens; keyboard/scanner and touch support, responsive layouts, existing localization/RTL conventions. No placeholder success or inactive feature controls.

## 2. Source audit and corrected baseline

The baseline Build workflow [46104000135](https://github.com/dharmendra1031/business_saas/actions/runs/46104000135) and Auth Browser QA [46104000146](https://github.com/dharmendra1031/business_saas/actions/runs/46104000146) passed. This does not certify the new catalogue end-to-end.

| Area | Verified current behavior | Design consequence |
| --- | --- | --- |
| Business provisioning | `PlatformBusinessProvisioning.cs` validates owner, market, currency, time zone, trial and creates first location. `PlatformCreateBusiness.tsx` already defaults currency/time zone from selected country. | Reuse existing wizard; suggest Main Shop, do not silently choose a wrong market or bypass verified owner. |
| Retail defaults | `RetailModule.AddBusinessDefaults` seeds General and ITEM. Cash preference defaults true; credit defaults false; receipt format Thermal80; auto-open not default. | Add only necessary standard units/categories when used. Do not preload a giant tenant master. |
| Global catalogue | `GlobalProductEndpoints.cs`, `PlatformGlobalProducts.tsx` implement admin list/create/edit and bounded search; development-only. | Harden identity and duplicates before adoption. No approved dataset import, local linking or review flow yet. |
| Retail product | `RetailProductEndpoints.cs` has scoped SKU/barcode, auto-SKU, category/unit validation and conversion controls. | Reuse RetailProducts and existing master dialogs; optional GlobalProductId. |
| Purchasing | `RetailPurchases.tsx` already supports inline supplier creation, Owner Save & Approve, Manager Submit for Approval, and Receive & Post Stock. `RetailPurchaseEndpoints.cs` separates draft, approval, receipt and posting with request keys. | Do not rewrite procurement. Add inline product adoption/creation and reuse the existing receipt/post flow. |
| Exact purchase lookup | The products API code filter compares SKU only; purchase UI describes barcode search as well. | Fix shared exact lookup; handle SKU/barcode ambiguity without silent selection. |
| Inventory | `StockPosting.cs` and existing receipt/sale/return posting maintain ledger and location balances. `RetailStockEndpoints.cs` supports one-time opening stock. | No manual balance overwrite; no second stock ledger. Purchase return is not a verified released flow. |
| POS | `CashCheckout.tsx` already uses optional cash customer, pending checkout recovery and register checks. `RetailCashSales.cs` has development-only cash/credit/return/exchange routes with release gates. | Preserve recovery, stock validation and server-authoritative prices. Never query remote catalogue during a sale. |
| Billing settings | `RetailBillingSettingsEndpoints.cs` still rejects enabling Credit via its settings endpoint; pilot credit posting routes are present. | Credit remains unavailable by default; reconcile release gates before advertising it. Do not bypass settings. |
| Register | `RetailCashRegister.cs` registers current/open/summary/movements; no close endpoint in audited file. | Finish register close and reconciliation as an independent production gate. |
| Printing | `CashReceipt.tsx` links RDLC PDF when available; Windows renderer required. | Reuse static A4/80mm RDLC and XSD. Auto-open/printing is optional, not a guaranteed browser silent-print capability. |
| Reports | Owner low-stock alerts exist in `BusinessEndpoints.cs`. | Reuse dashboard data; suggested reorder draft is not yet verified functionality. |

Source files are under `backend/BusinessSaas.Api/Features`, `backend/BusinessSaas.Api/Data` and `frontend/web/src` at the audited commit. Existing SRS status notes may lag code; verify actual release gates and tests before declaring completion.

## 3. Product identity and data ownership

Use the existing `GlobalProducts`, `RetailProducts` and `RetailStockBalances` tables. Global catalogue is the canonical shared identity; RetailProducts is the existing tenant operational record, not another shared master. Maintain existing local ProductId in all purchase, stock, sales, returns and invoice references. Introduce a nullable GlobalProductId with a filtered unique (BusinessId, GlobalProductId) index for linked products. Existing rows remain valid and unlinked until explicitly matched. For shared SQL Server use a restrictive FK; future dedicated/offline stores require a deliberate local catalogue copy/reference contract, not cross-database foreign keys or remote dependencies. Do not implement a tenant router or synchronization engine in this slice.

| Shared catalogue | Tenant operational record / transaction |
| --- | --- |
| Standard name, brand, category, pack identity, manufacturer GTIN, standard unit; optional licensed image/provenance | Local SKU, operational name/barcode snapshot, local classification and unit IDs, selling price, reorder level, active status, alternate units |
| Platform-managed metadata | Supplier, actual purchase cost on purchase line, quantities and financial history |
| Active means available for new adoption | Existing stock and transactions remain available according to local policy |

Identity guardrails:
- Preserve manufacturer barcode as text, with leading zeros; canonical GTIN-14 is a matching key, not necessarily the scanned representation. Accept valid GTIN-8/12/13/14 equivalents at the lookup boundary; preserve custom shop barcodes and existing SKU behavior. Do not replace stored retail barcodes blindly. Check conflicts with both local SKU and barcode.
- Checksum validity does not prove manufacturer ownership. Do not promote shop-generated or variable-weight/store-restricted codes into the shared manufacturer barcode namespace without verification.
- Normalize case/whitespace and explicitly supported pack-unit formats; do not collapse different variants, grade, flavour or pack size. Barcode-less exact duplicate protection needs database-enforced concurrency safety and an explicit identity rule; ambiguous products require human review, not destructive auto-merge.
- Global catalogue changes must not silently change a linked business's stock unit, barcode or historical transactions. Once used, identity-defining changes need a new product or explicit audited correction. Deactivate prevents new adoption, not existing sales of valid stock. Never hard-delete referenced products.
- Map global fields to the tenant's existing category/brand/unit by validated keys; create missing allowed masters on demand only, with tenant-safe uniqueness. A packaged 1 kg bag sold by PCS is not the same as loose rice sold by KG.
- Approval shares only necessary identification data and explicit consent. Private custom products can remain private. No supplier, price, stock, customer or profit data in shared catalogue/review payloads.
- Existing local duplicate with history: no automatic stock merge, rekeying, deletion, or transaction rewrite. Review conflicts and link only a compatible existing local record.
- Start with existing platform audit infrastructure and one minimal review mechanism; add a request table only if needed to represent consent, immutable submitted identity, reviewer outcome and retry safely. Do not put unapproved tenant suggestions directly into the published global table.

## 4. Minimum-entry onboarding

Keep the verified owner assignment and market checks. Preselect a first-location label (editable) and country-appropriate currency/time zone using existing market data. Use General and ITEM already provisioned; add relevant PCS/BOX/KG/LTR only as required by a real workflow, without inventing conversion factors. Standard group/brand/category mapping should happen on product adoption. Do not require configuration of all masters up front.

Give two clear next actions: Receive Goods (new stock) or Opening Stock (existing goods). Owner can review billing and receipt settings; released cash may be default-on, credit remains gated/off. Printer choice, paper size and auto-open can be confirmed when needed. Mandatory register opening and subscription/authorization checks stay intact. No fake business creation or payment activation.

## 5. My Products and purchasing: the primary flow

### 5.1 Shared product selection

1. User scans a barcode or searches by name/SKU. Search current business first, exact matches before broader results.
2. If no local match, search the active Global Catalogue from the local application database. Show a small bounded result list, with name, barcode, brand, pack and unit to distinguish variants.
3. If a global item is selected, show its identity read-only. Reuse the existing compatible local record if present; otherwise adopt once with the Owner/authorized Manager's selling price (or an explicit safe unavailable-for-sale state if not priced), unit/category mapping and auto SKU. No stock posting during adoption.
4. If truly absent, allow a private local product with name, valid stock unit, optional barcode, and selling price if ready to sell. Use General by default where appropriate. Offer optional contribution for Super Admin review; never wait for approval to operate the shop.
5. Return to originating form with the adopted local ProductId selected. Keep draft supplier, line items and quantities. Do not create a second local product on retry or refresh failure.

Use a shared, small selector/creation flow across existing Product Master and Purchase Entry only when both need it. Do not duplicate forms, validation logic, or lookup code. POS searches eligible local products only; do not let Cashier create products or alter price policy.

### 5.2 Purchase-driven flow

**Select supplier → scan/search item → adopt/create if missing → enter actual quantity and supplier rate → review selling price for new item → Save & Approve / Submit for Approval → Receive & Post Stock.**

- Existing supplier lookup and inline create already work; preserve.
- For an existing item, default the selected base unit or last compatible purchase unit, and optionally suggest the most recent confirmed purchase rate for the same business, supplier, product, currency and unit; label the source. Never assume today's supplier invoice rate.
- Quantity/rate are actual merchant-confirmed values. Existing selling price remains unchanged unless an authorized user explicitly edits it. Preserve selected conversion factor and posted snapshots.
- Owner quick receipt for goods already delivered may combine the existing steps into one user action only if the server validates authorization, quantities, approval policy and idempotent posting. Retain PO/partial receiving for orders not yet delivered. Do not fake receiving for ordered-but-undelivered items.
- Stock increases only when a goods receipt is successfully posted. A created product, draft or approved PO does not increase stock.
- If product adoption succeeds but purchase save fails, retain the same product ID and retry/recover the purchase using existing request keys. If receipt creation succeeds but posting fails, resume that receipt. Never re-create a possibly committed record automatically.
- A purchase document is not evidence of supplier payment. Do not invent accounts payable or financial postings outside the existing released scope.

## 6. Existing shop migration and opening stock

Offer a validated CSV/Excel import or fast opening-stock entry as a later reviewed slice. Preview matches, duplicates, invalid values and unmapped units; confirm before creating records. No opening stock from catalogue quantity. Opening stock uses the existing one-time authorized endpoint; subsequent corrections use audited adjustments. No duplicate opening quantities on retries. Require merchant-provided values and costing policy before claiming inventory valuation/profit accuracy.

## 7. Checkout, inventory, reporting and pricing

- Scan local eligible product and increment quantity according to existing unit rules. Preserve stock checks, decimal precision, price/quantity verification, payment confirmation and request-key recovery. Cash customer optional; credit requires active same-business customer, verified release, limit/due-date and authorized actor. Payment type is not automatically guessed.
- Post invoice and stock issue atomically. Register link and cash reconciliation remain authoritative; do not automatically open/close drawers or claim a cash receipt before server success. Finish actual close/variance approval separately.
- Reuse posted sale snapshots and static RDLC/XSD. Print/open only after confirmed posting, respect owner settings and actual Windows renderer availability. A print failure never retries billing. No extra print agent or runtime RDLC generation.
- Inventory ledger remains source of movements; balance updated via existing transaction code. Return only restocks when selected and permitted. Purchase returns are future unless implemented and tested; no manual deduction shortcut.
- Low-stock alerts already exist. Reorder suggestions are opt-in draft proposals based on actual configured target/reorder quantity and current stock/incoming orders where supported. No fixed guessed quantity or automatic supplier order.
- Optional pricing suggestion: cost × (1 + markup percentage / 100), using comparable stock-unit cost and defined rounding. Markup differs from gross margin. Confirm owner policy, applicable taxes, charges and verified MRP ceiling before use. Never silently change sale price or infer a universal MRP. No legal/tax bypass; tax release/compliance must be assessed for the intended market.

## 8. Deferred automation (not launch dependencies)

| Feature | Gate |
| --- | --- |
| Licensed global catalogue CSV import | Confirm source/license, record provenance, attribution, data separation, validation, deduplication and review; no external dataset assumed verified. |
| Existing shop Excel import | Preview, tenant boundaries, repeat-safe import and explicit quantities/prices. |
| Suggested reorder | Define target stock and supplier selection; draft only. |
| Supplier invoice PDF/photo extraction | Only after basic purchase workflow is stable. Extract to editable draft, flag low confidence and unmatched rows, validate quantities/rates/totals, human confirmation. No OCR/AI auto-posting; document access and retention policy needed. |
| Fuzzy/alias search | Only if pilot demonstrates exact/prefix/token matching is insufficient; no automatic identity merging. |
| Dedicated databases/offline catalogue updates | Preserve local selling even without central catalogue; separate future acceptance, no speculative infrastructure now. |

## 9. Delivery order and acceptance gates

| ID | Scope | Evidence before acceptance |
| --- | --- | --- |
| ME-01 | Catalogue hardening | Real SQL Server concurrent duplicate tests (with/without GTIN), equivalent GTIN scan and custom barcode tests, update/deactivation safety, admin MFA/audit tests; bounded search. |
| ME-02 | Retail adoption | Nullable link migration and snapshot, unique per-business link, safe existing-data migration, repeat/concurrent adoption, tenant master mapping, existing product IDs unchanged. |
| ME-03 | My Products UI | Shared selector, local-first results, local creation and consent, one-screen flow, no double save; responsive/keyboard/browser test. |
| ME-04 | Purchase integration | Fix exact barcode lookup, reuse supplier/PO/receiving; product adopted inline, same draft retained, owner/manager authorization, partial receiving and lost-response recovery. |
| ME-05 | Local product review | Approve/link/reject with audit; no automatic stock merges or disclosure of tenant financial data; rejected suggestion does not block private product. |
| ME-06 | Opening stock and helpful defaults | Existing shop path, one-time stock initialization, on-demand masters; compare setup effort with baseline. |
| ME-07 | Launch verification | Real SQL migrations and concurrency, build, dedicated browser flows, scanner/printer hardware acceptance, register close/reconciliation, returns/credit release-policy verification, tenant privacy and performance. |
| ME-08 | Optional imports and suggestions | Licensed import, last cost, pricing and reorder drafts as separate measured slices; OCR/AI deferred. |

Every change: development branch only; preserve main and existing SRS. Keep minimal diffs, refactor at source when necessary, delete superseded code after dependency review. Do not weaken tests or disable security to get CI green. Record commit, exact workflow runs, and scope of tests; report unverified hardware/deployment separately. Production remains gated until end-to-end acceptance.

## 10. Measure usability rather than invent percentages

Record actual baseline and after-change measurements on representative new and existing shops: time and number of fields/clicks/screens to add one catalogue item, create a new private item, purchase 20 mixed items, initialize existing stock, and complete a cash sale. Record errors, duplicate attempts and recovery after network failure. Target one product selection and price confirmation for a known product, inline completion for unknown product, and no separate compulsory master setup. These are design goals, not measured improvements. Benchmark representative catalogue sizes (e.g., 25k, 100k, 1m records) using SQL execution plans and observed response latency; no performance guarantee without results.

**Final recommendation:** Purchase-driven product adoption and a single canonical Global Catalogue, with existing local operational records, are the highest-impact low-complexity direction. Protect the financial and stock posting boundaries first. Reuse the existing flows and deliver one verified slice at a time.
