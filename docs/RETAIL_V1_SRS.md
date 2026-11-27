# Business SaaS — General Retail V1 Requirements

**Status:** Retail specification; catalog masters under review, transactional modules pending  
**Updated:** 2026-11-24  
**Repository:** `dharmendra1031/business_saas` ONLY  
**Reference UI:** CBK `development` read-only. New React + TypeScript + Bootstrap UI.  
**Related:** [Master Screen Register](./MASTER_SCREEN_REGISTER.md) · [Master SRS](./BUSINESS_SAAS_MASTER_SRS.md) · [Database](./DATABASE_ARCHITECTURE.md) · [UX Standards](./UX_STANDARDS.md) · [Appointment & Financial Rules (Salon reference)](./APPOINTMENT_FINANCIAL_RULES.md)

## 1. V1 launch slice

**General Retail Shop** is the first fully functional business template. A real Owner, after a Platform Super Admin provisions a Retail business and assigns/invites the verified owner, must be able to sign in, set up their shop, add products, receive stock, sell and return an item, reconcile day-end money and see branch-scoped reports. **Only Super Admin creates businesses** (latest owner direction; see [Master Screen Register](./MASTER_SCREEN_REGISTER.md)).

**In V1:**
- **Super Admin** creates General Retail business -> assigns Owner -> published plan/trial -> location, business date/timezone/currency and initial settings -> separate Owner/Admin login.
- Owner/Manager/Cashier roles, store assignment and server-side permissions.
- Product Masters: Group, Subgroup, Category, Brand and Unit; Supplier is separate. Customer is optional for an ordinary walk-in sale.
- Opening Stock with one-time/reversal rules and complete audit.
- Purchase Order -> Partial/Full Goods Receipt -> balance/ledger.
- Fast POS with barcode or product search, cart, quantity, approved price/discount, **Owner-controlled Cash and Customer Credit (Udhar)** sale methods, supported card/external payment record and split tender only when implemented and configured, printed receipt.
- Draft/Held Bill, posted invoice list, authorized full/partial Sales Return, Refund or Exchange policy, reason and stock reversal.
- Cash Register Open -> Sales -> Count/Close with variance record and approval threshold.
- Stock View, Stock Adjustment, Low Stock and basic sales/purchase/stock/cash summaries.
- Language switch and role/location context; platform-owned entitlements/usage limits.

**Not automatically part of first release:** complex general ledger, wholesale B2B, serial/IMEI and warranty, batch/expiry, multi-tier variants, discount campaigns, multi-currency POS, tax e-invoice integrations, offline payment capture or retail e-commerce website. **Customer Credit/Udhar is explicitly requested for Retail V1**, but must not be presented as available until customer balances, collection posting, authorization and audit are implemented and accepted. The domain should remain extensible but no speculative tables/abstractions for these features.

## 2. Personas and menus

| User | Visible core menus |
| --- | --- |
| Owner | Dashboard, POS/Sales, Products, Customers, Suppliers, Purchase, Inventory, Cash Register, Reports, Users/Settings, Subscription |
| Location Manager | Dashboard, Sales, Products, Purchase/Receiving, Inventory, Reports; location-scoped approvals |
| Cashier | POS, Held Bills, Sales History, Cash Register and approved Returns only |

A small store may have one person holding multiple roles; do not force unnecessary staff/account creation for its first sale.

## 3. Retail setup and product master

**Owner flow:** Accept assigned business invitation / sign in -> select authorized business -> review its provisioned first location -> configure tax/payment/receipt policy -> create Group/optional Subgroup/optional Category, Brand and Unit -> create Product with name, code/SKU, barcode optional, sale price and unit -> optional opening stock -> New Sale. The Owner does not have a Create Business button.

Product:
- Five distinct Product Masters tabs: **Groups**, **Subgroups**, **Categories**, **Brands**, **Units**. Groups/Subgroups/Categories reuse one Business-scoped `RetailCategories` parent tree (maximum depth three). Brands remain independently stored in `RetailBrands` and are never classification parents. Units remain independent.
- In Product Add/Edit, Group → Subgroup → Category are dependent selectors. Group is mandatory; Subgroup/Category may be omitted to preserve existing products and allow simpler retail catalogs. The saved `CategoryId` is the deepest selected node. Changing a parent resets the selected deeper levels rather than leaving stale IDs.
- Product filters apply the same Group → Subgroup → Category hierarchy and include descendants when filtering by a parent. Existing inactive classification must remain recognizable when editing historical products, but new selections require active masters.
- No extra Group/Subgroup database tables, no separate ProductGroupId/ProductSubgroupId columns, no automatic migration of existing product IDs and no duplicate category-service implementation.
- Name, code/SKU (unique within Business), category/group identity, optional brand, unit, active, sale price and applicable tax classification.
- Barcode can be linked when available; no invented barcode values. Later variants/serial/batches have separate requirements.
- Purchase unit and sales unit conversion, if needed, require exact defined behavior; V1 may support a single stock unit instead of pretending universal conversions.
- Products are Business scoped. The exact ProductId is always rechecked for BusinessId and active status.
- Edit prefill, first editable field focus, validation, duplicate SKU warning and clear inactive behavior.
- Inventory cannot be silently reset by changing product metadata.

## 4. Purchase and receiving

**Flow:** Supplier -> Purchase Order header/items -> Save -> Receive some/all -> Post Goods Receipt -> Stock increases -> Outstanding quantity updates.

- Purchase Order does not change stock; receipt **posting** does.
- PO has ordered, accepted-received and outstanding quantities per line.
- Receiving can be partial; over-receive blocked by server.
- Direct Receiving without PO is a separately authorized flow, flagged explicitly in receipt type and stock audit, if enabled for the chosen release scope.
- An incorrect posted receipt requires controlled reversal/correction with actor/reason; not silent quantity edit.
- Unit cost, landed costs and valuation method require approved market/product policy before profitability reports.
- Attachments/invoices require tenant-checked access.

## 5. Stock and transfers

- Opening Stock posted once per defined business/location/product initialization rule; amendments through audited adjustment.
- Stock movement ledger contains BusinessId, LocationId, ProductId, quantity delta, origin type/id, actor, timestamp.
- Negative stock is blocked by default in V1; if business wants different behavior, treat it as separate controlled requirement.
- Show **Available Stock** prominently before sale/transfer; server confirms again when posting to avoid stale stock race.
- Branch transfer V1 is optional and must be correctly modeled with source issue/destination receipt and in-transit state if released. Do not pretend there is no in-transit state when there is physical transit.
- Stock Adjustment: product, current stock, variance, reason, authorized save/approval, before/after movement; no direct balance overwrite.

## 6. Fast POS

**Normal sale:** Cashier opens assigned register -> focuses search/barcode -> adds product(s) -> optional customer for cash, **mandatory customer for credit** -> reviews clear amount -> selects an Owner-enabled, released sale method -> posts invoice once -> prints -> ready for next sale.

UI:
- CBK-inspired compact enterprise theme, but **cashier-first layout** with visible basket/total.
- Search/scan keyboard entry; product name, unit, price and stock where relevant; cart supports change qty/removal.
- Tax, discount and item/total policy are server-authoritative. Price overrides require appropriate permission. Legally required tax must not be bypassed with a payment-settings toggle.
- Cash shows paid/tendered/change; credit shows customer, limit, existing outstanding, new outstanding and due date without pretending payment has been collected. Card and split tender show remaining, no blind assumptions of provider success.
- Payment failure, pending, unknown and success states are separate. Re-try checks original operation first.
- Receipt printing is post-commit and retryable without a new invoice. A fixed developer-maintained 80mm/A4 RDLC design and typed header/line dataset must be used when the renderer is approved; no runtime RDLC XML generation or per-tenant template copies.
- Held bills remain drafts and cannot deduct stock or count as sales unless a documented reservation policy is later adopted.
- Keyboard shortcuts and barcode scanners should be tested with actual hardware; touch POS screens must remain operable.

## 6A. Owner-controlled Billing & Payments settings — confirmed 24 November 2026

- **Business Settings → Billing & Payments** is an **Owner-only write** screen. Manager/Cashier may read the effective options but cannot alter them. Super Admin can control platform-wide feature release/plan entitlement; the Owner may enable only the methods supported and released for their business.
- Per-business options: **Cash Sales ON/OFF**, **Customer Credit/Udhar ON/OFF**, and approved discount, return/refund and external payment capabilities as those features are implemented. Defaults: Cash ON for the Development cash-POS slice; Credit OFF until its end-to-end ledger workflow is accepted. Do not expose a working-looking toggle for unimplemented credit/card/UPI.
- Changing settings is a tenant-scoped, auditable write requiring Owner authorization and a valid active subscription. The effective setting must be enforced in server sale-posting endpoints, not just through a hidden POS button. Disable affects **new** sales only, never already-posted invoices, collection/settlement, receipt lookups or outstanding recovery.
- The Owner may deliberately switch **Cash OFF even if Credit is not released**; this pauses new checkout, visibly explains why and keeps historical receipts available. No fake fallback payment method. Platform pause and subscription expiry take precedence. Configuration has a revision/concurrency check; a stale update is rejected.
- **Cash** is an immediately settled tender; **Credit/Udhar** is an *unpaid or partially settled customer receivable*, not a cash tender. Every credit sale requires an active customer belonging to the same BusinessId and an authorized credit policy. Walk-in/anonymous credit is forbidden.
- Before credit posting: validate configured credit limit, current outstanding, requested amount and due date on the server; persist posted invoice, stock issue, receivable movement and due date **atomically** with an idempotency key. Define permitted partial payments and overdue blocking before release. Financial balances are derived from immutable movements, not overwritten by editing a customer's total.
- Collections can be posted against prior credit invoices and must reduce outstanding exactly once, with method, actor, location, timestamp and receipt reference. Cash collection impacts a cash register; an unpaid credit sale does **not** increase cash takings. Returns, refunds, reports and day closing reconcile invoice totals separately from receipts and receivables.
- Turning Credit OFF blocks **new** credit invoices only. Existing customer dues remain visible, collectible and reportable; disabling must not erase or silently settle them. Changes to discount or refund policy must not rewrite historical bills.
- **Implementation status (24 Nov 2026):** The Owner Cash ON/OFF setting has a persisted Business-scoped revision, owner-only API, change audit, POS view and server gate. Credit setting is visibly locked and cannot be enabled by API until its receivable/collection workflow is released. Cash POS remains Development-only. Credit invoices, ledger and collections are **not implemented**. Hosted browser and retailer acceptance remain pending.

## 6B. Owner-managed Invoice & Print preferences — 24 Nov 2026

- Per-business, Owner-only, revision-checked and auditable settings: approved **80mm/A4** retail cash receipt template, show/hide sale time, unit price, cash received, change and footer, and optional footer text (up to 120 characters). New inactive subscriptions cannot edit. A manager/cashier cannot change the settings.
- Mandatory printed information (merchant name, receipt reference, date, product/qty, each line amount and total payable) is **never hideable**. No tax invoice, discount or customer credit fields are fabricated until transactional models and legal requirements support them.
- Static RDLC source files and two-table XSD are checked into `backend/BusinessSaas.Api/Reports/`. `RetailReceiptReportData.FromCashSale` reads posted sale values, verifies total reconciliation and prevents cross-business mapping. Report XML contains no queries and is not rewritten at runtime.
- **Release gate:** An actual report renderer/driver integration, tested .NET 10 hosting compatibility, 80mm continuous-paper height, A4 pagination, printed tax requirements, device performance and real printer acceptance are **not** complete. Saved settings presently do not modify the older browser `window.print()` flow. See `docs/RDLC_INVOICE_DESIGN.md`.

## 7. Sales return, refund and exchange

- Identify original posted invoice, line quantity/amount already returned, eligible balance, refund method and whether stock will return.
- Full/partial returns cannot exceed original sold quantity or refundable amount across prior returns.
- Confirm reason and authorized role. Use compensating inventory and financial movements.
- Exchange is a linked return + new sale with transparent net difference; do not silently edit the original invoice.
- Refund to original provider requires actual integration capability; otherwise record verified external settlement separately.
- Implementation prerequisite (24 Nov 2026): the shared read-only cash receipt detail exposes historical SKU, unit, sold quantity, original rate, posted line amount, location and customer name. For older lines without a stored amount, the API derives the original rounded amount using the immutable posted quantity and price, not current masters. These are original sale values, **not** approved return eligibility or a refund balance. No return posting, stock restoration, cash refund or exchange action is enabled until the compensating document, authorization, approval and reconciliation policy is implemented. Owner-wide receipt access remains Owner-only; cashier history remains actor/location scoped.

The Development-only Owner receipt detail now offers a read-only full/partial return estimate using original posted line amounts, bounded by original sold quantity, with three-decimal quantity validation and duplicate-line rejection. The API explicitly returns canPost=false. The estimate is not a refundable balance and is not stored; when actual returns are implemented it must use cumulative returned quantity and allocated cents, with final return consuming the exact remaining amount. No refund, cash movement or stock restoration is posted. Owner-only preview is a conservative implementation choice pending refund approval and drawer rules.

## 8. Cash register and basic reports

- Register session has user, business/location, opened time, opening float, posted cash movements, counted cash, variance and close approval.
- Multiple cashiers using the same drawer is a deliberate config/policy, not an assumed safe default.
- Daily sales report shows gross, discount, tax, refund, net invoice amount, actual receipts by method, outstanding and cash variance separately.
- Stock report uses movement-backed current balances; purchase report distinguishes placed orders from goods actually received.
- Reports use branch business date/timezone and correct server-side totals, not sums of only the displayed page.
- SaaS subscription revenue is excluded from a retail business's own sales dashboard.

## 9. Proposed UI screens (each has Create/Edit/Empty/Error/Permission states)

| Screen | Main action | Required guardrail |
| --- | --- | --- |
| Retail Setup | Complete checklist | Skip only optional setup; resume |
| Dashboard | Open next task | Totals sourced from correct business/location |
| Product List | Search / New Product | Server paging, duplicate SKU, correct empty state |
| Product Form | Save product | Edit prefill, focus, validation |
| Supplier List | Search / New supplier | Business-scoped records |
| Purchase Orders | Create/approve PO | Approval alone cannot add stock |
| Goods Receipt | Receive remaining quantity | Partial posting and over-receipt prevention |
| Stock | View balances and movements | Branch scope, real available stock |
| Adjustment | Submit variance | Reason, permissions and audit |
| POS | Scan/add/pay | One posted invoice, no duplicate charge |
| Sales History | Find/reprint invoice | Reprint has no stock/money effect |
| Return/Refund | Return eligible item | No over-refund or unapproved action |
| Cash Register | Open/count/close | Variance approval and reconciliation |
| Reports | Filter/export | Accurate totals, business date, tenant access |
| Users / Settings | Manage access / billing methods / tax / receipt | Owner-only server-enforced Cash/Credit availability, customer ledger and compliance guards |

## 10. Business and technical acceptance tests

| Test | Expected outcome |
| --- | --- |
| Super Admin provisions a new retail business | Platform-only server permission + MFA, unique numeric BusinessId, assigned verified owner/invitation, location, subscription/trial, audit, duplicate-safe retry and Owner access after activation |
| One user owns two businesses | Switching never shows first business's stock, customer, sale or cache |
| Cross-business ProductId/InvoiceId passed | Access denied without data leakage |
| Plan disables Purchase | UI entry unavailable and purchase API access denied |
| Receipt 40 + 60 against PO 100 | PO remains partial until full; stock increases 40 then 60 |
| Receiving 101 against remaining 100 | Rejected; stock unchanged |
| Two sales compete for last 5 units | No negative quantity or duplicate stock posting |
| Two taps on Pay | One invoice, one accepted charge attempt |
| Owner disables Cash and enables released Credit | New POS checkout methods follow persisted business setting; historical receipts remain accessible; roles and platform gate checked by API |
| Credit is off or not released, or customer not selected | Credit posting blocked by server even with a crafted request; no invoice, stock or receivable movements |
| Credit sale is posted then Credit is disabled | Existing outstanding, due dates and collections remain accessible; only new credit checkout is blocked |
| Two credit collections race | No overcollection, duplicate ledger movement or cash entry; one accounting result per request key |
| Payment timeout after provider success | Same logical transaction reconciled; no second charge |
| Full and partial refund | Total returned/refunded <= sold/eligible; ledger preserved |
| Receipt printer fails | Sale remains posted; reprint does not repeat posting |
| Role is Cashier at Location A | Cannot access Location B stock/transactions without assignment |
| English -> Arabic RTL -> uploaded third language | Forms and POS remain usable, correct currency/date |
| Cash register closes | Expected cash vs counted cash and variance recorded |
| Failed import or duplicate SKU | Data/permissions intact, actionable validation errors |
| Daily report | Numbers reconcile to posted sales, returns and payments |

## 11. Phase 1 unknowns to confirm (no hidden defaults)

1. First retail country/market; applicable tax and invoice numbering/receipt standards.
2. Stock valuation method, expected cost/margin calculation and rounding.
3. Payment gateway(s) and whether POS card payments are integrated or manually recorded.
4. Is direct receiving required in first retail release or subsequent iteration?
5. Are multi-location stock transfers required in launch? **Customer Credit/Udhar confirmed for Retail V1 (24 Nov 2026);** exact credit-limit/overdue/approval rules require final decision before enabling.
6. Exact cash register shared-drawer policy and approved refund thresholds.
7. Product barcode scanner/label printers supported hardware.

**No claim of functionality until code, DB migrations, end-to-end browser tests and real retailer acceptance run successfully.**


### Development return posting pilot (25 Nov 2026)
Owner-only return POST is gated by RetailReturns:PostingEnabled (default false), and only mapped in Development. Two compensating tables retain original receipt/line links, actor, reason, idempotency key, cumulative allocated amount and restock decision. A confirmed cash refund records the owner's attestation, not an automatic payout. The server compares expected amount, validates the remaining quantity, restores sellable stock within the same serializable transaction and never modifies the original receipt. Non-restocked returns do not enter available stock. The read-only estimate now accounts for earlier posted returns. Keep the switch off outside isolated testing until owner approval/drawer policy, history, net reporting, UI reconciliation/retry and return receipt are accepted. No production refund or physical cash action was performed.

Return reconciliation update: Owner receipt detail supplies previously returned and remaining quantity/amount. Estimate and posting share cumulative amount allocation; the preview selects only the remaining quantity. Daily cash sales aggregates gross receipts and posted refunds by their own business date, location and currency; refunds of earlier invoices count on the refund date, including days with no new receipts. This is a sales report, not drawer reconciliation or general ledger. SQL Server competing-return and browser regressions added; posting remains disabled by default and frontend posting/history/return RDLC remain pending.

Owner invoice detail now includes paged posted return history and a guarded record-return workflow. Estimate response exposes the existing host posting switch; when disabled only preview is offered. Owner explicitly selects sellable/non-restock per line, enters a reason and attests cash was already settled. The UI persists one operation key and exact request in account/business/location/receipt-scoped session storage before posting, offers read-only lookup or identical retry on an uncertain response, and requires reopening the invoice after success for fresh quantities. Browser storage is not a substitute for a permanent drawer reconciliation or multi-device recovery procedure. The API remains Development-only, posting disabled by default. Return RDLC and cash register release gates remain pending.

UX follow-up: verified return confirmation clears its saved request only after checking receipt ID, operation key, amount, reason and returned line quantities/dispositions against the response. On success the original receipt is reloaded automatically, with confirmation retained and retry available if the read fails. Tablet (768px) and mobile (360px) overflow checks and screenshots were added to browser QA. Invoice-wise history is implemented; separate location-wide return register, physical drawer reconciliation and return RDLC remain release gates.

### Product unit configuration (25 Nov 2026)
Product Master supports up to ten alternate units with a direct factor to the base stock unit (0.001–9999999.999, three decimal places). The base unit remains the only quantity used by current purchase, stock, POS and return transactions. Configuration does not convert or post any stock. New units must be active and tenant-scoped; the base cannot be selected as its own alternate, duplicates are rejected, and an inactive existing alternate can be retained unchanged or removed. Omitted conversion fields preserve the existing configuration for older clients. Clear alternate units before changing a previously unused product's base unit. Future transactional integration must snapshot selected unit, factor and base quantity, validate exact representability, and reconcile returns and prices before enabling alternate units. No unit-code-based whole-number policy is introduced by this change.

Purchase integration: one optional alternate unit per order line (one line per product). On create the API validates the active product-specific unit and expected factor and snapshots unit ID/code/factor; old base-unit lines retain null snapshots and factor 1. Unit cost is per selected order unit. Partial receipts remain in the original order unit; stock posting multiplies by the saved factor and rejects results outside the stock range or not exactly representable to three decimals. Duplicate request comparison includes selected unit and factor; an existing order may be retried after master conversion changes. No new stock table or posting engine. POS and sale returns still use only the base unit. Configuration/concurrent base-unit edits and alternate-unit browser acceptance remain follow-up checks.

Purchase UI now validates exact base stock quantities before submission for orders and receivings, previews converted stock beside the quantity, and clears quantity/cost when switching order units so stale per-unit values cannot be silently reused. Browser mock coverage includes invalid fractional conversion, correct selected factor, and base stock after receiving. SQL Server remains the authority for transactional acceptance.
Purchase concurrency follow-up: product updates use a serializable transaction for their reference checks and save. Purchase creation and opening stock hold validated product rows through commit (repeatable read on SQL Server, serializable on SQLite). SQL conflict responses instruct a retry; existing purchase idempotency remains. Dedicated competing master-edit tests are still required; no claim of complete concurrency protection.

POS alternate-unit backend pilot: default-off RetailSales:AlternateUnitsEnabled, Development only. One unit per product line. Alternate price is provisionally derived from base sale price multiplied by the configured factor, rounded to two decimals before calculating the posted line amount; independent pack prices are not implemented. The request must match the current active alternate unit, factor and calculated rate; stock is posted in exact base quantity. The posted line snapshots the selected unit ID, factor, base unit code, existing selected unit code, rate and amount. Returns use the original sale factor and quantity, not current master data, and reject quantities that cannot be represented exactly in base stock. Legacy base-unit clients remain compatible. Existing RDLC data maps selected quantity/unit/rate; dedicated converted report tests and POS selection UI remain pending. Keep the switch off until UI, pricing policy and end-to-end acceptance are verified.

POS pilot UI: the existing bounded product search returns active alternate-unit quotes only when the host switch is enabled. Search and posting use the same server rate formula. Existing basket has one unit per product; changing unit resets quantity to one and requires enough base stock for one selected unit. The cashier uses quantity controls for an alternate line; repeat base-barcode scan does not silently increment an alternate line. Direct fractional entries must convert exactly. The purchase and POS clients share the same base quantity helper. Dedicated browser mocks and SQL quote assertions added. Keep alternate sales default-off pending acceptance, independent pack pricing decision and real RDLC verification.

Alternate sale price: optional positive two-decimal per-unit override on the existing Product Master conversion row (maximum same as base price). Blank/null uses derived base price × factor. Configuration is a complete replacement when conversions are supplied; omitted conversions preserve all existing rows. Inactive alternate rows cannot be changed except removed. Purchase cost and stock factors are unaffected. POS uses one shared server quote/post formula and rejects stale prices. Historical sales, returns and reprints use the recorded selected unit and amount. A4 and 80mm real-PDF tests and extracted-text/page-size checks added without modifying static RDLC or XSD. Host switch remains default-off until verification and commercial acceptance.

Final unit integration audit follow-up: Purchase/Receiving, POS and Sales Return use the same server base quantity validation in StockPosting. Return estimation now checks historical conversion precision in the existing UI before submission and displays the base stock equivalent (not a promise to restock damaged items). SQL and browser regression cases cover unrepresentable fractional return quantities and valid alternatives. Prior converted RDLC PDF checks passed; no report definitions changed. Dedicated stale base-unit selection and simultaneous master-edit cases remain to be verified, and alternate sale/return production gates remain off.

Final integration follow-up: Purchase and POS clients send the base unit code quoted when selecting a product. The API rejects a mismatching base unit before posting and verifies supplied codes during request-key replay (sale snapshot; purchase referenced master). Legacy callers omitting the optional field remain compatible; old pending sales are recoverable. No database change. Concurrent master-unit edit stress testing still required. Customer credit remains locked until receivable posting, collections, reporting and approved limits/overdue policy are ready.

### Customer credit foundation (25 Nov 2026)
Customer Master has a persisted per-customer credit limit in the business currency, default zero (no new credit). Owner-only setting in the existing customer editor; separate explicit audited save with change reason and expected previous value. Manager contact edits do not change the limit and financial limit is not returned to managers. Positive limit requires active customer; subscription and tenant checks apply. Limit is configuration only, not an outstanding balance or permission to post credit. Credit sales remain locked; invoice, due-date/overdue policy, receivable movements, collections, return adjustments, drawer reconciliation and reporting must be implemented and accepted first. No new ledger tables in this phase.

### Credit invoice and receivable pilot
Reuse the immutable sale header and lines: nullable CreditDueDate identifies fully unpaid credit invoices (Tendered=0); historical cash invoices remain unchanged. A Development-only Owner pilot requires both RetailCredit:PostingEnabled and persisted CreditSalesEnabled; the public billing settings still refuse activation. Same sale pricing, product validation, unit snapshots, stock posting and idempotency are reused. No cash receipt lookup, cash refund, cash report or cash dashboard entry for a credit invoice. Owner-only paged ledger reads actual credit invoices across locations and totals by currency, including inactive customers. Pilot policy: positive limit, active named customer, due date today through 365 days, no overdue or different-currency outstanding; serializable transaction checks outstanding before stock issue. No payments, credit returns, credit PDF, split tender or cashier posting until their respective rules and integrations are implemented; keep both gates off. Limit and due-date policy provisional, no production activation.

Credit ledger UI: Owner can open a paginated read-only customer ledger from the existing Customers list (including read-only subscription state). Shows posted unpaid invoices and per-currency outstanding totals, with genuine empty/error states. SQL test explicitly enables the business credit flag in disposable CI data only, then tests limit, invoice posting and replay, stock movement, cash report isolation, permissions, disable/recovery and ledger access. No collection or print actions exposed; dedicated credit ledger browser acceptance remains pending.

### Credit collection pilot
Immutable cash payment records link one existing credit invoice and actor, with a business-scoped unique request key. Owner-only Development POST, separate default-off RetailCredit:CollectionsEnabled, active subscription required for new collection; prior receipts and history remain available when new credit is disabled or the customer is inactive. Payment is an attestation of cash already received, not a transfer; original invoice location is the collection location for this pilot. Serializable transaction validates expected outstanding and prevents overcollection. No stock changes or invoice edits; ledger and limit validation use invoices minus posted payments. Paid overdue invoices no longer block new credit. Read-only payment history supports exact request-key recovery. Cash sales dashboard remains sales-only, not a drawer statement; physical cash reconciliation, correction/reversal policy, payment receipt RDLC, browser posting/recovery and cross-location collections remain release gates.

Collection regression: SQL test adds partial receipt, same-key recovery, stale/invalid amount rejection, competing final collections, exact zero balance, preserved invoice and stock, and unchanged cash sales totals. Existing ledger shows invoice amount, received and remaining amounts. No payment action is exposed until guarded confirmation and recovery UI are accepted.

Credit collection UI: Owner ledger exposes invoice payment history and cash attestation form only when server returns canCollect. Session recovery is keyed by user, business and customer; exact saved request is persisted before network write, and matching receipt identity/amount is required before clearing it. Lookup-only recovery never posts; retry reuses the key. Unresolved or unreadable saved requests block another collection for the customer in that tab. Closing the browser/session or switching devices is not cross-device recovery; reconciliation remains necessary. No production gate activation or cash drawer/receipt printing changes.

POS credit UI pilot: same basket and exact request key recovery, owner-only server dashboard capability requires development host, both posting and collections switches, and business credit flag. Explicit named customer, due date and unpaid sale confirmation; credit recovery endpoint remains readable for original actor when disabled. Credit confirmation shows actual invoice and outstanding, no cash receipt or PDF. Billing settings still prohibit general activation. Cash selection unchanged for ordinary users. Due date and balance checked on server; no split payment or credit returns. UI and SQL verification pending.

### Current customer outstanding and aging report
Owner-only Reports > Outstanding, across all business locations, grouped by historical invoice currency. Unpaid = posted credit invoice total less immutable collections. Includes inactive customers with debt; excludes settled invoices. Search by current customer name/phone; overdue-only filter affects both totals and rows. Business-local current date; due today is not overdue. Aging buckets 1–30, 31–60, 61–90 and over 90 days. SQL aggregation and bounded paging, consistent read transaction, no new balance table. Current balance only, not historical as-of reporting. Tests cover partial payment, 31-day overdue fixture, settled balance and authorization. Credit returns, reversals, drawer reconciliation and release remain pending.

### Invoice register UI/UX audit follow-up
Existing Owner Sales & Returns > Invoices uses real cash and credit invoice rows with all-location, date range, invoice/customer snapshot, payment and quantity-based return status filters. Server totals represent original gross invoice amounts before returns, grouped by currency and applying all filters across pages; they are not collections or net sales. Original location is passed to detail and cash PDF actions. Credit invoices are read-only using shared receipt details; credit printing and credit returns are not exposed. Cash-only cashier receipt history is unchanged. Search refresh and clear reset the list; return completion refreshes register on back. SQL and browser regressions added; dedicated return register, exchanges, broader layout/RTL review remain pending.

### Sales return register
Owner-only read-only Sales & Returns > Returns uses existing posted cash returns, not a new refund posting system. Filters cover original location, return posting date range (business timezone), return/invoice number and historical customer name/phone. Currency-separated refund totals across matching pages; bounded page line details use original product/unit snapshots and actual restock flag. Original invoice opens shared read-only receipt with existing authorized cash PDF. Credit returns, exchange posting, return-specific RDLC and Arabic RTL acceptance remain pending. Owner sidebar removes module-wide Preview badges for sales, reports and settings; unfinished tabs retain Under construction, and Cash Register retains Preview. SQL and browser regressions added.

### Exchange phase 1 — read-only server estimate
Owner-only estimate against an original cash invoice at its original location and currency. Return value uses remaining original posted quantity/amount and shared cumulative allocation; replacement uses current product price, selected unit/factor and shared checkout validation. Signed difference = replacement total minus eligible return total (positive collect, negative refund, zero even). Stock eligibility considers only explicitly restockable incoming goods, then replacement deduction; no database mutations or actual cash transfer, no reservation, no posting button. Existing cash and credit checkout use the same extracted sale line builder. SQL regression covers refund/even, stale price, stock, excess return, permissions and read-only behavior. Before posting, implement immutable link between return and replacement invoice, idempotent single transaction, actual net cash settlement and reporting/printing reconciliation; do not call two independent posting APIs. Credit and cross-location exchanges are out of pilot scope.

### Exchange phase 2 — Owner estimate UI

The original cash invoice now exposes a read-only exchange estimate using the existing Owner-scoped endpoint and POS product lookup. Select remaining return quantities and stock condition, replacement products, units and quantities. The server returns the net collect/refund/even preview; changing inputs clears the prior estimate. No posting or settlement action is exposed. Browser QA covers the request and stale estimate invalidation. Atomic linked exchange posting and reconciliation remain pending.

### Exchange phase 3 — posting prerequisites (not released)

Existing PostSale and PostReturn each own a serializable transaction and commit independently. Never orchestrate them as two HTTP calls. A single exchange transaction must revalidate eligible return lines and replacement prices/units, restore only sellable stock, issue replacement stock, and commit the linked original return and replacement invoice together. Retain original documents and stock source references. Any failure rolls back the entire exchange. A durable unique operation key and immutable link must support identical replay, reject changed payloads and allow lookup-only recovery even after new posting is disabled. Owner, tenant, original location, currency, active subscription and release gates must be checked server-side.

Gross return and replacement document values are not physical cash movements. For example a 500 return and a 650 replacement create gross documents of 500 and 650 but actual cash collected is 150; a 650 return against a 500 replacement refunds only 150; equal values move no cash. Do not populate cash received or change fields with fictitious cash, or count gross exchange values as drawer inflows/outflows. Before enabling posting, define and persist the immutable link and actual signed settlement, reconcile daily/report/register and RDLC labels, and test unchanged/replayed and concurrent requests, stale values, rollback after stock restoration, disabled gates, and recovery. No new posting endpoint or database schema in this step. Credit and cross-location exchanges remain out of scope.

SQL regression extends the read-only estimate with same-product restock versus damaged goods, and verifies that sales/return totals, document counts, stock movement counts and the original/replacement balances remain unchanged. This is an estimate regression, not evidence of atomic posting.

### Exchange phase 4 — durable link and settlement schema

The existing return header now has nullable ReplacementSaleId and NetCashSettlement (decimal(18,2)). Both are null for a standalone cash return; both must be present for a linked exchange. The replacement invoice FK is restricted, the filtered unique index prevents reusing a replacement invoice for two exchanges, and the check constraint rejects self-links or incomplete settlement records. Positive settlement means actual cash collected; negative means actual cash refunded; zero means even. Existing original sale and return totals remain gross document values. No new table or additional operation key: use the existing business-scoped return RequestKey in the future posting transaction. Downgrade refuses to discard any posted exchange link.

This is the database foundation, not an exchange posting release. The future transaction must verify tenant, location, currency, permissions, payload/replay and net amount equality against the linked invoice and return. The FK alone is not a tenant authorization check. Existing receipt cash/change labels and cash register/report semantics must be reconciled before posting is enabled. No endpoint or UI posting action is enabled by this migration. SQL regression checks the migration constraints and legacy standalone returns remain null.

### Exchange phase 5 — isolated atomic posting pilot

The Owner-only POST and GET on the original invoice exchanges route share the existing estimate validation. POST requires an explicit Development host flag RetailExchanges:PostingEnabled, return posting flag, cash sales enabled, active subscription, reason, exact signed settlement and confirmation. Both documents and stock movements commit together. Replays match original invoice, actor, key, reason, quantities, prices, units, condition and settlement; lookup-only recovery returns the linked IDs and amounts. The replacement snapshots the original customer and current merchant; Tendered stores actual cash collected only. No credit or cross-location exchange. Ordinary sale/return replay cannot replay an exchange; replacement invoices cannot themselves be returned in this pilot. Normal receipt detail shows the linked settlement and exchange RDLC is explicitly blocked until its layout is ready. No frontend posting button or production flag enabled. Gross register totals remain document values, not a drawer statement. Report labels, return history/print, all settlement cases, concurrent exchanges, rollback under mid-transaction failure and hosted release acceptance remain pending.

### Exchange phase 6 — confirmation and recovery UI

The estimate exposes canPost only when the explicit development flags, business cash setting and active subscription permit posting. The existing receipt screen requires a reason and explicit confirmation of actual collection/refund/even settlement. It stores the exact request and quoted totals in session storage before POST, verifies linked receipt identity and amounts before clearing it, and offers GET-only recovery and same-key retry. Pending recovery suppresses ordinary return and new exchange actions on that invoice; it can be reopened even when remaining quantity is zero. Storage is tab/session scoped, not cross-device reconciliation. Unreadable exchange storage blocks new actions. No production switch, exchange RDLC or cash-register reconciliation is released.

### Exchange phase 7 — concurrent settlement regression

The existing SQL Server retail integration test exercises a refund exchange (two competing keys for the same original line) and an equal-value exchange (same key on two independent Owner sessions). Retriable conflicts are allowed, but replay must resolve to one linked replacement and one return. Verify gross document totals, zero replacement cash tender for both cases, actual daily cash refund deltas, exactly two stock movements, and stock balance versus ledger. These are correctness races, not a throughput benchmark. Production release remains gated; physical printers and mid-transaction fault injection still require acceptance.

### Cash register phase 1 — isolated opening pilot

One persisted register session per operator and location may be open at a time. Shared drawers are not supported by this pilot; Owner access is location-scoped, Cashier access is restricted to the existing primary-location assignment, and Managers are excluded. Opening cash is explicitly supplied (zero is valid), recorded to two decimal places in the business currency, with UTC timestamp and business-scoped operation key. Identical retries return the same session; changed or foreign-operator keys and extra active sessions are rejected. The database enforces the active-session uniqueness and amount/date invariants. GET current returns only the caller's register, with no financial records from another operator. POST opening is Development-only and default-off via RetailCashRegister:OpeningEnabled; no UI or mandatory billing integration is enabled. This is not a complete drawer workflow: sales, returns, exchange and credit collection links, manual cash movements, closing/variance/approval, recovery procedures and printing must be implemented before releasing opening to actual operators. No historical cash activity is assigned retroactively.

### Cash register phase 2 — POS opening panel

The existing POS product workspace exposes the opening panel only when the development dashboard returns the explicit opening capability for an Owner or Cashier. Opening cash uses the existing minor-unit parser; an exact business/operator/location-scoped request is stored before POST. A lost response is recovered by reading the current session or retrying the original key; a mismatched active session blocks another opening. No new CSS, dependency, separate POS or drawer totals. The panel explicitly states that billing is not linked. The flag remains off by default; the opening pilot is not released, and actual sale/return/collection session linkage must be added in the next migration before use. Browser QA covers invalid opening cash and a lost response recovered by GET without another POST.

### Cash register phase 3 — financial posting links (isolated pilot)

Nullable RegisterSessionId links the existing cash sale, cash return and credit collection headers to the persisted register session, using restricted foreign keys and indexes. Historical records remain unassigned; no backfill or duplicate cash ledger. A separate default-off Development test flag RetailCashRegister:LinkingEnabled requires the posting operator's own open register in the same business, location and historical document currency. It is resolved inside the existing serializable financial transaction, after idempotent replay checks. Credit invoices have no cash link; collections are linked at the original invoice location. Exchange replacement and return share the same session; future register reconciliation must exclude linked replacement invoices from ordinary sale cash and include each signed settlement exactly once via the return. A missing session rejects new cash posting; no automatic opening. Manager cash posting cannot proceed in this pilot because Managers cannot open a register. This flag is only enabled in integration tests until all financial confirmation screens enforce register readiness before physical cash settlement, and closing/variance/recovery and release gates are complete. Downgrade refuses to remove posted links. SQL coverage asserts no-session rejection, operator-specific links, unchanged retry/stock behavior and exclusion of unpaid credit invoices.

### Real-shop readiness — register rejection recovery

POS checks the active operator/location/currency register before opening the cash payment dialog when register linking is required. Managers cannot initiate cash settlement in that pilot. A server REGISTER_REQUIRED rejection preserves the original sale key and exposes the existing opening panel inside pending-sale recovery; retry checks readiness again without collecting cash again. Credit invoice recording is unaffected. Existing disabled-register behavior and backend transactional enforcement remain unchanged. A browser regression simulates a missing shift, successful opening, then a register state change before posting and same-request recovery. This is a POS-specific fix, not the business setting or closing release; return, exchange and collection preflight still require acceptance. No production flags enabled.

### Register state synchronization correction

Browser QA found that the opening panel can load before a sale rejection arrives and retain the earlier open shift. A REGISTER_REQUIRED response or a failed active-session preflight now remounts the panel with a fresh status read. Open sessions also expose a manual status check. The original pending sale and request key remain intact. The existing failing browser regression is retained without relaxing assertions. Return, exchange and collection readiness remain separate follow-up work.

### Real-shop readiness — refund, exchange and collection register checks

Reuse the existing register opening panel in the three cash settlement forms when the register linking capability is active. The panel checks operator, location, open state and document currency; cash settlement confirmation remains disabled until the register is verified. Return/exchange/collection REGISTER_REQUIRED responses retain the pending request, refresh the register panel, and instruct the operator not to repeat the physical settlement. Lookup-only recovery remains available even if a shift is no longer open. Credit ledger exposes the existing register linking capability and uses its original invoice location; older stored requests without a location still support lookup. No new endpoint or ledger, no production activation. Server posting checks remain authoritative; this UI precheck does not eliminate races between physical cash handling and commit.
