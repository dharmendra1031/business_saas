# Batch 09 — Retail Stock Foundation (BS-081–BS-090)

**Reviewed:** 12 November 2026. **Scope:** Initial movement ledger and one-time stock opening only.

| Task | Status | Evidence / remaining gate |
| --- | --- | --- |
| BS-081 — Stock movement ledger | UX REVIEW PENDING | `RetailStockMovements` stores BusinessId, LocationId, ProductId, actor, timestamp, source type/id, signed quantity change and balance after; readable history API/UI |
| BS-082 — Opening stock once | UX REVIEW PENDING | `POST /api/businesses/{id}/stock/locations/{locationId}/opening` posts a balance and immutable opening movement atomically; duplicate protected by unique DB indexes |
| BS-083 — Scoped stock view | UX REVIEW PENDING | Paginated product stock view with location selection, uninitialized vs zero quantities; only assigned primary location for Manager, Owner sees business locations |
| BS-084 — Stock adjustment | TODO | No adjustment write implemented; policy, reason and approval pending |
| BS-085 — Concurrent non-negative stock | TODO | DB check and unique opening guard exist, but concurrent decrement/sale/receiving endpoints and tests do not |
| BS-086 — Audited corrections | TODO | No corrections/reversals allowed until compensating movement workflow is designed |
| BS-087 — Low stock | TODO | No thresholds or alerts |
| BS-088 — Transfers | TODO | In-transit and location rules not designed |
| BS-089 — Valuation | TODO | No cost or valuation posting; accounting policy pending |
| BS-090 — Ledger reconciliation | TODO | Need real receipt/sale/adjustment transactions, automated totals reconciliation and drift detection |

## Data rules

- Two small tables: `RetailStockBalances` is the mutable quantity cache with unique (BusinessId, LocationId, ProductId) and `Quantity >= 0` DB constraint; `RetailStockMovements` is append-only audit history. All entity IDs are numeric `BIGINT IDENTITY`.
- Quantity has three decimal places (`decimal(18,3)`). Product SalePrice remains `decimal(18,2)`; stock quantity must not be confused with money.
- Opening is posted **once per product/location**, even when amount is zero. A filtered unique index on Opening movement and a unique StockBalance index guard concurrent duplicate requests.
- Opening balance and movement share one SQL transaction. No direct overwrite endpoint is exposed, and subsequent attempts respond with HTTP 409. A later change must be an audited stock adjustment, not a second opening.
- The movement model includes source type and numeric source ID for future purchase receipt line/sales line posting; those operations must commit financial/stock records together and use idempotent source indexes.
- Decrement under concurrency is **not yet implemented**; future outflows must atomically check current nonnegative balance inside the database, not merely trust an earlier screen read.
- No valuation method chosen. Never guess purchase unit cost from retail sale price.

## Permissions and evidence

- Owner and Manager only; Cashier is denied from inventory masters. Cross-business ProductId/LocationId and other business template are denied. Manager can access the primary location only until explicit location assignment is implemented.
- Expired Trial and inactive subscription block opening writes; read-only stock/history remains available.
- Integration tests cover non-member/cashier, foreign product/location, Manager primary vs other branch, invalid precision/negative quantity, one-time duplicate rejection, exact movement/balance, and expired Trial.
- GitHub Draft PR [#5](https://github.com/dharmendra1031/business_saas/pull/5) depends on unmerged Supplier/Customer Draft PR [#4](https://github.com/dharmendra1031/business_saas/pull/4). **Do not merge #5 before #4.**
- Running browser acceptance at 360/768/1440px, keyboard/focus, confirmation, and SQL Server concurrency/reconciliation testing are still required before marking any task DONE.

**Next:** Real browser verification and BS-084 adjusted stock with reason + BS-085 safe concurrent movement/posting service before enabling Purchase Receiving or POS.
