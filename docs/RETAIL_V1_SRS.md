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
