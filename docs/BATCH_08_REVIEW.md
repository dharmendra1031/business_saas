# Batch 08 — Retail Masters (BS-071–BS-080)

**Reviewed:** 12 November 2026. First narrow Category Master slice only.

## BS-071 implementation
- Numeric SQL identity category IDs and a tenant-specific `(BusinessId, NameKey)` unique constraint.
- New `ProductCategories` table; no sample data, unnecessary general-purpose framework, or other retail transaction schema.
- Authenticated, Retail-only, membership-scoped List, Create and Edit endpoints.
- Owner and Manager writes require an active subscription or unexpired trial; Cashier may read but cannot write.
- Case-insensitive duplicate checks with SQL Server unique-key race handling; trim and length validation.
- React Category List/Create/Edit UI, prefilled edits, input focus, search, loading/empty/error/retry states, and an honest Retail Dashboard entry.
- Tests for anonymous access, cross-tenant data denial, duplicate names, Cashier denial and trial expiration.
- SQL Server integration verifies EF migration and tenant protections.

## Quality evidence
- Commit: [585064c](https://github.com/dharmendra1031/business_saas/commit/585064c51a34d0671a4135d8e08b56b0640f51ac)
- [GitHub Actions 43153177335](https://github.com/dharmendra1031/business_saas/actions/runs/43153177335): backend **PASS**, SQL Server migration/integration **PASS**, React TypeScript build **PASS**.
- This is a **draft pull request**, not a merged production feature.

## Acceptance still pending
- Real browser checks at 360px/768px/1440px.
- Keyboard-only flow, screen-reader names, focus restoration and user-role UX on an actual rendered site.
- Real SQL Server staging rollout and production operational audit.
- User acceptance on realistic category volumes.

**Status:** BS-071 = UX REVIEW PENDING, not DONE.

## BS-072–BS-080
All subsequent Retail Master tasks remain TODO: Units, Products, SKU/barcodes, prices/tax, activation, Suppliers, Customers, product paging and full cross-business ProductId denial.

**Next:** Complete browser UX evidence for BS-071, then implement BS-072 Unit Master without duplicating patterns prematurely.
