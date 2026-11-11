# Batch 01: Product Scope & Architecture (BS-001–BS-010)

**Reviewed:** 11 November 2026 · [190-task tracker](./DEVELOPMENT_TASK_TRACKER.md)

Ten tasks reviewed. Existing design/code is not counted as finished without real user-flow acceptance. No extra project, database table, dependency or duplicate licensing code was added.

| Task | Review / work performed | Status | UI/UX check or blocker |
| --- | --- | --- | --- |
| BS-001 — Multi-business scope | Master SRS and existing membership-scoped business list | BASELINE | User account switching walkthrough pending |
| BS-002 — Numeric IDs and BusinessId | IdentityUser<long>, long entities and existing cross-tenant SQL tests | BASELINE | Screen/user IDs not manually exposed; browser check pending |
| BS-003 — Retail V1 acceptance | SRS defines Product → Receiving → POS → Return → Report; only dashboard exists | BASELINE | Real POS screens not implemented; no false signoff |
| BS-004 — Security and release gates | Existing Engineering Guardrails define tenancy, private-repo boundary, payment and release no-go | BASELINE | Full release and role UX acceptance pending |
| BS-005 — Country/tax | Country/currency/timezone catalog present; tax and invoice numbering not approved | DECISION REQUIRED | Select first India/Kuwait or another launch market, then verify actual tax policy |
| BS-006 — Plans/trial | Four public plans plus Enterprise described, Starter trial currently 14 days | DECISION REQUIRED | Approve exact pricing, quota and plan inclusions; no fake price labels |
| BS-007 — Payments/refunds | Provider, refund limit and approval policy not finalized | DECISION REQUIRED | Choose provider and supported tender methods before checkout UI |
| BS-008 — Printer/barcodes | Keyboard barcode lookup/manual selection specified in SRS | DECISION REQUIRED | Pilot hardware/device list needed; avoid unsupported device-specific screens |
| BS-009 — Supported business types | API IsSupported guard + new inactive Salon negative test; React picker uses API list | UX REVIEW PENDING | Only RETAIL should appear. Need browser viewport and keyboard confirmation |
| BS-010 — Development order | Roadmap: one API, one React app, Retail before Salon/Repair, no speculative abstractions | BASELINE | Supported route labels and task navigation browser review pending |

## UI/UX review after this batch

- **Create Business:** the business type picker gets its options from `GET /api/business-types`. New regression test seeds an unpublished Salon type, checks that only RETAIL is listed, and verifies unsupported POST is rejected with HTTP 400. Server-side checking already existed; this adds test coverage.
- **Retail Dashboard:** corrected a misleading fixed “Trial active” label. Active subscriptions now show “Plan active”; trial subscriptions show “Trial active”.
- **Unaffected paths:** Login, My Businesses, Create Business and Business switch retain their existing API/UI contracts and must be regression checked.
- **Actual browser/real user review is pending:** not verified at 360/768/1440px, with keyboard, mobile or cashier/owner walkthrough. **Do not mark BS-009 DONE until that evidence exists.** RTL review follows after Arabic is supported.
- **No fabricated UX evidence:** Product/Purchase/POS/Return/Reports screens are not implemented, so they cannot be visually signed off.

## Verified GitHub CI

[Run 42986340375](https://github.com/dharmendra1031/business_saas/actions/runs/42986340375) — **all jobs PASS** on the Batch 01 code commit `1798c317`:

- Backend: **5/5 tests PASS**.
- SQL Server 2022 integration: **1/1 test PASS**.
- React TypeScript production build: **PASS**.

The new inactive-Salon negative case runs within the existing backend flow test. These results verify code behavior and build, **not** actual responsive browser UX or pilot-user usability. Keep those tasks short of DONE until UI evidence is available.

## Decisions still open

1. **BS-005:** First official market (India/Kuwait/other); supported tax invoice/numbering and receipt rules.
2. **BS-006:** Paid prices, branch/user/storage quotas, effective terms and trial eligibility policy.
3. **BS-007:** Online subscription payment provider, physical POS tender handling, refund approvals and reconciliation.
4. **BS-008:** Browser/OS, receipt printer and scanner models to certify at the pilot.

**Next batch:** BS-011–BS-020 Foundation. For every task: minimal change → CI tests → screen/role/viewport UI review → regression check → evidence; no DONE without all gates.
