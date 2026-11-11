# Batch 04 — Business Creation and Tenant Security (BS-031–BS-040)

**Reviewed:** 11 November 2026  
**Repo:** `dharmendra1031/business_saas`  
**Scope:** Review existing Business onboarding/security, add a **small owner-managed team access slice** using the existing `BusinessUsers` table; no schema migration, identity rewrite, additional service, package or new project.

## Task-by-task status

| Task | Evidence / change | Status |
| --- | --- | --- |
| BS-031 — Membership-based business list | Existing `GET /api/businesses` joins current UserId against `BusinessUsers`. No unowned businesses appear in list | BASELINE |
| BS-032 — Transactional Create Business | Existing transaction creates Business, Owner membership, primary Location and Subscription; no rewrite | BASELINE |
| BS-033 — Idempotent business creation | Existing request key + unique key and concurrent SQL tests preserve one business on retry | BASELINE |
| BS-034 — First location | Existing `MAIN` location created with business; one location in first-run experience | BASELINE |
| BS-035 — Starter trial | Existing configured 14-day trial with expiration checks; no paid-plan enforcement yet | BASELINE |
| BS-036 — Two accounts / two businesses | Existing two-owner and membership-revocation tests; business list switch remains scoped. Browser cache review still pending | BASELINE |
| BS-037 — Trusted business/role context | Extended Dashboard to read current membership role server-side and return only primary locations for Manager/Cashier; invalid/removed membership returns HTTP 404. Added negative tests | UX REVIEW PENDING |
| BS-038 — Owner/Manager/Cashier permissions | Added owner-only Team Access API and panel. Owner may add existing Manager/Cashier accounts; non-owner cannot manage. Future POS/sensitive operations still need explicit permission checks | TODO |
| BS-039 — Location-specific assignment | **Not implemented**. Staff are restricted to primary-location dashboard visibility; individual branch assignment and operational API scope require a reviewed schema + tests later | TODO |
| BS-040 — Staff invites/deactivation | Owner can add a previously registered account or remove its business membership. Email invite workflow and account activation are **not implemented**, so task remains incomplete | TODO |

## Working implementation

- `GET /api/businesses/{businessId}/staff`: Owner-only list of member emails and roles.
- `POST /api/businesses/{businessId}/staff`: Owner-only grant to existing account, permitted roles `Manager`/`Cashier`; rejects unknown accounts, `Owner` escalation and duplicate memberships.
- `DELETE /api/businesses/{businessId}/staff/{userId}`: Owner-only revoke (cannot remove another Owner); revoke applies on next request without waiting for the old session to expire.
- `GET /api/businesses/{businessId}/dashboard` checks current membership role; Manager/Cashier can only see primary location. This does **not** authorize future POS, reports or other branch operations.
- Existing `BusinessUsers.Id` and all UserId/BusinessId references remain numeric; no UUID entity keys added.
- React displays a compact **Team Access** panel for Owners only, with email input, role dropdown, Add action, list and confirm-before-Remove. It accurately explains the current limits of team access and email invites.

## GitHub CI verification

[CI Run 42989423781](https://github.com/dharmendra1031/business_saas/actions/runs/42989423781) for code commit `a37a728`:

- Backend xUnit **7/7 PASS**, including tenant scoping, duplicate staff, invalid role, non-owner denial, membership revoke and limited location visibility.
- SQL Server 2022 integration **1/1 PASS** (existing concurrent Create Business/tenant suite).
- React TypeScript production build **PASS**.
- Later UI-copy commit `d36e978` requires its own check before claiming the final branch fully verified.

## Mandatory UI/UX review after every task

Static review completed for changed Owner dashboard/Team Access flow: labels, required email, explicit role dropdown, no fake entries, disabled actions during saving, success/error feedback, remove confirmation, responsive grid with a one-column form at narrow widths.

**Not independently verified:** actual running browser with 360/768/1440px, keyboard/focus traversal, screen-reader behavior, real user testing, manager/cashier walk-through, and mobile device rendering. The browser integration was not available in this session. Keep **UX REVIEW PENDING**; do not call the screen fully approved.

## Remaining safe follow-up

Complete browser role/viewport checks and actual staff add/revoke exercise, then design location assignment only when multiple branches are introduced, and secure email invitation + role-change workflow after verified email delivery. No automatic invite claims or unapproved database changes.
