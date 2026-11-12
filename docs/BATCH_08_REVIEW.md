# Batch 08 — Retail Master Data (BS-071–BS-080)

**Reviewed:** 12 November 2026  
**Scope:** First delivery slice for BS-071 and BS-072. No stock, financial posting or sample business data.

| Task | Status | Evidence |
| --- | --- | --- |
| BS-071 — Category master | UX REVIEW PENDING | Tenant-scoped create/edit/list, normalized duplicate names, active/inactive status, responsive React form |
| BS-072 — Unit master | UX REVIEW PENDING | Unique code and name, validation, create/edit/list and inactive status |
| BS-073 — Product master | TODO | SKU and product schema have not been implemented |
| BS-074 — Barcode lookup | TODO | Not implemented |
| BS-075 — Sale price/tax | TODO | Pricing and tax classification need real product workflow |
| BS-076 — Product edit/deactivate | TODO | Not implemented |
| BS-077 — Supplier master | TODO | Not implemented |
| BS-078 — Customer master | TODO | Not implemented |
| BS-079 — Product search/paging | TODO | Category/Unit paging exists; Product search does not |
| BS-080 — Cross-business master tests | IN PROGRESS | Category/Unit isolation covered; Products, Suppliers and Customers pending |

## Implemented

- New `RetailCategories` and `RetailUnits` tables with `BIGINT IDENTITY` primary keys and `BusinessId`-scoped unique indexes.
- Case-normalized names prevent duplicate category or unit names with different casing. Unit codes are normalized to uppercase.
- API routes under `/api/businesses/{businessId}/categories` and `/units`: paged searchable list, create and update status. No destructive deletion.
- Owner and Manager only; Cashier and non-members receive NotFound. Each read/edit is scoped by the authenticated user's business membership.
- Editing is denied when subscription is not Active or an unexpired Trial. Read access remains available for subscribed business members so previous catalog data is not hidden.
- Category and Unit setup is reachable from the Retail Dashboard, with no demo records, edit prefill, clear form cancellation, validation and mobile-responsive controls.

## Verification

- SQLite xUnit integration covers new entities, duplicate rules, Manager/Cashier roles, cross-business access, foreign IDs, and expired Trial writes.
- Isolated SQL Server integration test verifies that the new migration is applied alongside existing migrations.
- GitHub CI: [initial review run 43167649449](https://github.com/dharmendra1031/business_saas/actions/runs/43167649449) — backend, SQL Server and frontend SUCCESS. Recheck final PR head before merge.

## Outstanding UX and release gates

- Running browser tests for owner/manager/cashier, focus behavior, errors, 360px/768px/1440px viewports and screen-reader usability are still needed.
- English/Arabic RTL support is not implemented yet; this remains a wider product requirement.
- Product master, Supplier, Customer, stock, purchase, POS, published entitlements and plan-specific limits are **not** covered in this slice.
- No task is DONE until real workflow verification and the mandatory UX gate are completed.

**Next slice:** BS-073–BS-076 Product Master; reuse the Category/Unit models and authorization rules, and test duplicate SKU, selected unit/category, inactive references and tenant access before considering the task complete.
