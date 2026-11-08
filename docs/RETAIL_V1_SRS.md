# Business SaaS — General Retail V1 Requirements

**Status:** First vertical specification, not implemented  
**Updated:** 2026-11-08  
**Repository:** `dharmendra1031/business_saas` ONLY  
**Reference UI:** CBK `development` read-only. New React + TypeScript + Bootstrap UI.  
**Related:** [Master SRS](./BUSINESS_SAAS_MASTER_SRS.md) · [Database](./DATABASE_ARCHITECTURE.md) · [UX Standards](./UX_STANDARDS.md) · [Appointment & Financial Rules (Salon reference)](./APPOINTMENT_FINANCIAL_RULES.md)

## 1. V1 launch slice

**General Retail Shop** is the first fully functional business template. A real new owner must be able to register, set up a shop, add products, receive stock, sell and return an item, reconcile day-end money and see basic branch-scoped reports.

**In V1:**
- Business registration -> General Retail -> plan/trial -> location, business date/timezone/currency and initial settings.
- Owner/Manager/Cashier roles, store assignment and server-side permissions.
- Product, Category, Unit and Supplier; customer optional for ordinary walk-in sale.
- Opening Stock with one-time/reversal rules and complete audit.
- Purchase Order -> Partial/Full Goods Receipt -> balance/ledger.
- Fast POS with barcode or product search, cart, quantity, approved price/discount, cash and supported card/external payment record, split tender when configured, printed receipt.
- Draft/Held Bill, posted invoice list, authorized full/partial Sales Return, Refund or Exchange policy, reason and stock reversal.
- Cash Register Open -> Sales -> Count/Close with variance record and approval threshold.
- Stock View, Stock Adjustment, Low Stock and basic sales/purchase/stock/cash summaries.
- Language switch and role/location context; platform-owned entitlements/usage limits.

**Not automatically part of first release:** complex general ledger, wholesale B2B, serial/IMEI and warranty, batch/expiry, multi-tier variants, credit/udhar, discount campaigns, multi-currency POS, tax e-invoice integrations, offline payment capture or retail e-commerce website. The domain should remain extensible but no speculative tables/abstractions for these features.

## 2. Personas and menus

| User | Visible core menus |
| --- | --- |
| Owner | Dashboard, POS/Sales, Products, Customers, Suppliers, Purchase, Inventory, Cash Register, Reports, Users/Settings, Subscription |
| Location Manager | Dashboard, Sales, Products, Purchase/Receiving, Inventory, Reports; location-scoped approvals |
| Cashier | POS, Held Bills, Sales History, Cash Register and approved Returns only |

A small store may have one person holding multiple roles; do not force unnecessary staff/account creation for its first sale.

## 3. Retail setup and product master

**Owner flow:** Create business -> choose first location -> configure tax/payment/receipt policy -> create Category/Unit -> create Product with name, code/SKU, barcode optional, sale price and unit -> optional opening stock -> New Sale.

Product:
- Name, code/SKU (unique within Business), category, unit, active, sale price and applicable tax classification.
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

**Normal sale:** Cashier opens assigned register -> focuses search/barcode -> adds product(s) -> optional customer/walk-in -> reviews clear amount -> cash/card -> posts invoice once -> prints -> ready for next sale.

UI:
- CBK-inspired compact enterprise theme, but **cashier-first layout** with visible basket/total.
- Search/scan keyboard entry; product name, unit, price and stock where relevant; cart supports change qty/removal.
- Tax, discount and item/total policy are server-authoritative. Price overrides require appropriate permission.
- Cash shows paid/tendered/change; card and split tender show remaining, no blind assumptions of provider success.
- Payment failure, pending, unknown and success states are separate. Re-try checks original operation first.
- Receipt printing is post-commit and retryable without a new invoice.
- Held bills remain drafts and cannot deduct stock or count as sales unless a documented reservation policy is later adopted.
- Keyboard shortcuts and barcode scanners should be tested with actual hardware; touch POS screens must remain operable.

## 7. Sales return, refund and exchange

- Identify original posted invoice, line quantity/amount already returned, eligible balance, refund method and whether stock will return.
- Full/partial returns cannot exceed original sold quantity or refundable amount across prior returns.
- Confirm reason and authorized role. Use compensating inventory and financial movements.
- Exchange is a linked return + new sale with transparent net difference; do not silently edit the original invoice.
- Refund to original provider requires actual integration capability; otherwise record verified external settlement separately.

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
| Users / Settings | Manage access / tax/receipt | Business membership and plan limits |

## 10. Business and technical acceptance tests

| Test | Expected outcome |
| --- | --- |
| A user creates a new retail business | Separate numeric BusinessId, owner membership, location, subscription/trial, checklist |
| One user owns two businesses | Switching never shows first business's stock, customer, sale or cache |
| Cross-business ProductId/InvoiceId passed | Access denied without data leakage |
| Plan disables Purchase | UI entry unavailable and purchase API access denied |
| Receipt 40 + 60 against PO 100 | PO remains partial until full; stock increases 40 then 60 |
| Receiving 101 against remaining 100 | Rejected; stock unchanged |
| Two sales compete for last 5 units | No negative quantity or duplicate stock posting |
| Two taps on Pay | One invoice, one accepted charge attempt |
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
5. Are multi-location stock transfers and Customer Credit/Udhar required in launch?
6. Exact cash register shared-drawer policy and approved refund thresholds.
7. Product barcode scanner/label printers supported hardware.

**No claim of functionality until code, DB migrations, end-to-end browser tests and real retailer acceptance run successfully.**
