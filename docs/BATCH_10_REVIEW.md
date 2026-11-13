# Batch 10 — Retail Purchase and Receiving (BS-091–BS-100)

**Reviewed:** 13 November 2026. **Branch:** `feature/retail-purchase-receiving` (Draft PR [#8](https://github.com/dharmendra1031/business_saas/pull/8)). The feature has not been merged or accepted as production-ready.

| Task | Status | Evidence |
| --- | --- | --- |
| BS-091 — Purchase Order create/list | UX REVIEW PENDING | Business-scoped PO list, supplier and product selection, draft form and detail |
| BS-092 — PO Draft/Approve | UX REVIEW PENDING | Owner approval does not change stock |
| BS-093 — Ordered/Received/Remaining/totals | UX REVIEW PENDING | Line quantities and estimated cost totals in React |
| BS-094 — Partial Receiving | UX REVIEW PENDING | Receipt draft then explicit stock posting; 100 = 40 + 60 tested |
| BS-095 — Over-receiving guard | UX REVIEW PENDING | Check posted totals against PO quantities at draft creation and posting, Serializable transaction |
| BS-096 — Post receipt to ledger | UX REVIEW PENDING | Guarded StockPosting and receipt status in one transaction; numeric source line IDs and repost idempotency |
| BS-097 — Direct Receiving | TODO | Not implemented |
| BS-098 — Purchase Return | TODO | Not implemented |
| BS-099 — Supplier outstanding/payment | TODO | Not implemented |
| BS-100 — Purchase user workflow acceptance | TODO | Browser tests, realistic role and receipt retry behavior outstanding |

## Rules and scope

- Four tables with numeric BIGINT IDENTITY primary keys: `RetailPurchaseOrders`, `RetailPurchaseOrderLines`, `RetailReceivings`, `RetailReceivingLines`. No UUID/GUID or copied product/supplier tables.
- Product, Supplier and Location are validated within authenticated BusinessId. Cashier is denied; Owner approves; Manager may prepare PO and receipts for the authorized primary location. Subscription/trial rules apply.
- PO: Draft → Approved → Closed when fully received. Approval does not change stock.
- Receipt: Draft → Posted. Only final posting changes StockBalance and appends a StockMovement. Reposting the same numeric ReceiptId does not post stock again.
- Posting checks remaining qty from **posted** receipts in a Serializable transaction. It posts stock and receipt status atomically. SQL Server deadlocks are translated to controlled HTTP 409 conflicts without swallowing unrelated programming exceptions.
- Ordered, Received, Remaining and **estimated** order/received values are shown. Purchase accounting, supplier ledger, tax calculation, landed cost, cost valuation, returns and POS are **not** implemented.
- A separate client idempotency mechanism for duplicate *creation* of drafts after network retry is outstanding. It is not enough to verify only idempotent posting of the same receipt.
- Tests: tenant denial, role restrictions, 100 → 40 + 60, over-receiving, posted movement counts, duplicate post and SQL Server concurrent 70+70 receiving from a 100-unit PO. Final CI succeeded on code commit `f305879`: [CI run 43360704315](https://github.com/dharmendra1031/business_saas/actions/runs/43360704315) — backend, SQL Server and React PASS. Browser usability at 360px/768px/1440px remains required before DONE.

**Stacking:** [PR #6](https://github.com/dharmendra1031/business_saas/pull/6) Adjustment → [PR #7](https://github.com/dharmendra1031/business_saas/pull/7) safe posting → [PR #8](https://github.com/dharmendra1031/business_saas/pull/8) Purchase. Merge only in this order after acceptance.
