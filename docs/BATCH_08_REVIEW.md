# Batch 08 — Retail Master Data (BS-071–BS-080)

**Reviewed:** 12 November 2026  
**Scope:** Category/Unit and Product Master delivery slices. No stock, financial posting or sample business data.

| Task | Status | Evidence |
| --- | --- | --- |
| BS-071 — Category master | UX REVIEW PENDING | Tenant-scoped create/edit/list, normalized duplicate names, active/inactive status, responsive React form |
| BS-072 — Unit master | UX REVIEW PENDING | Unique code and name, validation, create/edit/list and inactive status |
| BS-073 — Product master | UX REVIEW PENDING | Tenant-scoped product CRUD, required Category/Unit and unique SKU |
| BS-074 — Barcode lookup | UX REVIEW PENDING | Optional business-unique barcode, active-only exact lookup and list search |
| BS-075 — Sale price/tax | UX REVIEW PENDING | Decimal(18,2) sale price, validation and tax classification code; actual tax computation not implemented |
| BS-076 — Product edit/deactivate | UX REVIEW PENDING | Edit prefill and active/inactive status; product is hidden from barcode lookup when inactive |
| BS-077 — Supplier master | TODO | Not implemented |
| BS-078 — Customer master | TODO | Not implemented |
| BS-079 — Product search/paging | TODO | Category/Unit paging exists; Product search does not |
| BS-080 — Cross-business master tests | IN PROGRESS | Category/Unit/Product isolated; Supplier and Customer still pending |

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

## Product Master (BS-073–BS-076)

- `RetailProducts` table uses numeric `BIGINT IDENTITY` ID, BusinessId, required active Category/Unit at creation, case-normalized business-unique SKU and optional unique barcode.
- Product create/edit/list/detail/exact barcode lookup and status filtering are protected by the existing Owner/Manager membership and active-subscription write checks. Cross-business ProductId and foreign master IDs are rejected.
- Sale price persists as `decimal(18,2)` with non-negative, at-most-two-decimal validation; pricing and tax are not calculated by a POS or tax engine.
- Product list has name/SKU/barcode search and pagination, filters, a compact editor, duplicate-code feedback and no fabricated data. Inactive items remain visible to managers but are excluded from barcode lookup.
- Integration tests cover product code collisions, barcode uniqueness, negative/three-decimal prices, manager/cashier, cross-business references, inactive master, expired Trial and edit/deactivate.
- Product work branch: [PR #3](https://github.com/dharmendra1031/business_saas/pull/3). Merge and final CI confirmation are documented separately.

## Outstanding UX and release gates

- Running browser tests for owner/manager/cashier, focus behavior, errors, 360px/768px/1440px viewports and screen-reader usability are still needed.
- English/Arabic RTL support is not implemented yet; this remains a wider product requirement.
- Supplier, Customer, stock, purchase, POS, published entitlements and plan-specific limits are **not** covered. Product tax class is a reference code, not an approved tax engine.
- No task is DONE until real workflow verification and the mandatory UX gate are completed.

**Next slice:** BS-077–BS-078 Supplier/Customer masters, then BS-079 product list performance and BS-080 expanded cross-business audit. Real browser UX remains a prerequisite for DONE.
