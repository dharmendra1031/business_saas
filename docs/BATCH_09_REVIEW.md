# Batch 09 — Retail Stock Foundation (BS-081–BS-090)

**Reviewed:** 12 November 2026. **Scope:** Initial movement ledger and one-time stock opening only.

| Task | Status | Evidence / remaining gate |
| --- | --- | --- |
| BS-081 — Stock movement ledger | UX REVIEW PENDING | `RetailStockMovements` stores BusinessId, LocationId, ProductId, actor, timestamp, source type/id, signed quantity change and balance after; readable history API/UI |
| BS-082 — Opening stock once | UX REVIEW PENDING | `POST /api/businesses/{id}/stock/locations/{locationId}/opening` posts a balance and immutable opening movement atomically; duplicate protected by unique DB indexes |
| BS-083 — Scoped stock view | UX REVIEW PENDING | Paginated product stock view with location selection, uninitialized vs zero quantities; only assigned primary location for Manager, Owner sees business locations |
| BS-084 — Stock adjustment | UX REVIEW PENDING | Initial single-item voucher: Manager/Owner create with system/physical/delta/reason, Owner review and post, movement-backed balance update, backend tests; browser UX pending |
| BS-085 — Concurrent non-negative stock | UX REVIEW PENDING | Conditional database UPDATE and transaction-owned ledger; SQL Server two 70-unit sales from 90 available allows only one, no negative quantity; POS acceptance pending |
| BS-086 — Audited corrections | TODO | No corrections/reversals allowed until compensating movement workflow is designed |
| BS-087 — Low stock | TODO | No thresholds or alerts |
| BS-088 — Transfers | TODO | In-transit and location rules not designed |
| BS-089 — Valuation | TODO | No cost or valuation posting; accounting policy pending |
| BS-090 — Ledger reconciliation | TODO | Need real receipt/sale/adjustment transactions, automated totals reconciliation and drift detection |

## Stock Adjustment reference and implementation (BS-084)

**Read-only reference:** `dharmendra1031/CBK` `development` — `StockAdjustmentService`, `StockMovementService`, `ReceivingService`, `StockAdjustmentsController`, `StockAdjustmentForm`. No changes to CBK.

- Adopted CBK's distinct **Pending Approval → Approved/Rejected → Posted** workflow. Creating and approving a voucher does not affect stock.
- First SaaS slice is **one product per voucher**, not CBK's multi-line voucher. This deliberately avoids an extra detail table before the workflow is proven. Future multi-line count support requires separately tested transaction semantics.
- All adjustment requests include BusinessId, LocationId, ProductId, numeric ID, requester, reason, optional remarks, expected system quantity, counted physical quantity, reviewer/poster IDs and timestamps.
- Manager can request adjustments only for the authorized primary location; Owner can request/review/post within own business. Other tenants, Cashier and expired Trial writes are denied.
- Posting checks product active state and **current quantity equals saved system count**. It updates balance with a conditional database update, changes voucher status, and records an `Adjustment` stock ledger entry in one serializable SQL transaction.
- The existing unique `RetailStockMovements` index on (BusinessId, SourceType, SourceId) prevents a duplicate movement. Posting again returns existing Posted status without changing quantity.
- Reject requires remarks and never changes stock. An outdated approved voucher remains unposted; the user must create a new one with the latest balance.
- React Stock tab: Adjust action on initialized active products, physical count, visible signed Difference, reason, Manager/Owner request, Owner approval/rejection and confirm posting. Existing Opening Stock and History are preserved.
- Tests cover tenant isolation, inactive subscription, Cashier/Manager role boundary, rejected voucher, stale stock, duplicate posting and exact movement/balance totals. CI is necessary but actual 360/768/1440px browser checks remain a release gate.
- [Draft PR #6](https://github.com/dharmendra1031/business_saas/pull/6). Never mark BS-084 DONE merely because builds pass.

## Data rules

- Two small tables: `RetailStockBalances` is the mutable quantity cache with unique (BusinessId, LocationId, ProductId) and `Quantity >= 0` DB constraint; `RetailStockMovements` is append-only audit history. All entity IDs are numeric `BIGINT IDENTITY`.
- Quantity has three decimal places (`decimal(18,3)`). Product SalePrice remains `decimal(18,2)`; stock quantity must not be confused with money.
- Opening is posted **once per product/location**, even when amount is zero. A filtered unique index on Opening movement and a unique StockBalance index guard concurrent duplicate requests.
- Opening balance and movement share one SQL transaction. No direct overwrite endpoint is exposed, and subsequent attempts respond with HTTP 409. A later change must be an audited stock adjustment, not a second opening.
- The movement model includes source type and numeric source ID for future purchase receipt line/sales line posting; those operations must commit financial/stock records together and use idempotent source indexes.
- Reusable StockPosting now uses a guarded database UPDATE under the caller's transaction; it prevents negative quantity even under concurrent requests. No direct unaudited deduction endpoint exists.
- No valuation method chosen. Never guess purchase unit cost from retail sale price.

## Permissions and evidence

- Owner and Manager only; Cashier is denied from inventory masters. Cross-business ProductId/LocationId and other business template are denied. Manager can access the primary location only until explicit location assignment is implemented.
- Expired Trial and inactive subscription block opening writes; read-only stock/history remains available.
- Integration tests cover non-member/cashier, foreign product/location, Manager primary vs other branch, invalid precision/negative quantity, one-time duplicate rejection, exact movement/balance, and expired Trial.
- GitHub Draft PR [#5](https://github.com/dharmendra1031/business_saas/pull/5) depends on unmerged Supplier/Customer Draft PR [#4](https://github.com/dharmendra1031/business_saas/pull/4). **Do not merge #5 before #4.**
- Running browser acceptance at 360/768/1440px, keyboard/focus, confirmation, and SQL Server concurrency/reconciliation testing are still required before marking any task DONE.

**Next:** Complete Purchase Receiving SQL contention and browser UX checks, then POS. The StockPosting helper is implemented and tested, but full sale posting is not.
