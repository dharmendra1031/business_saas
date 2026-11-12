# Batch 06 — Super Admin Foundation (BS-051–BS-060)

**Reviewed:** 12 November 2026.  
**Scope:** Add the smallest **read-only** Platform Business Directory. Avoid exposing subscription mutation before audit/entitlement/notification rules exist.

## Ten tasks: exact delivery status

| Task | What now exists / what's missing | State |
| --- | --- | --- |
| BS-051 — Platform Admin permission | Existing `licenses.manage` claim stayed protected. A separate `businesses.view` claim is required for directory access, and Business Owner alone is insufficient | BASELINE |
| BS-052 — Admin layout | Authenticated, permitted users see a Platform Admin sidebar entry. Ordinary Owner/Manager/Cashier do not see it. Admin page is distinct from shop dashboard | UX REVIEW PENDING |
| BS-053 — Business directory | `GET /api/platform/businesses`, search by name, server-side paging (20 default, max 50) and honest loading/empty/error states. React table has contextual View labels | UX REVIEW PENDING |
| BS-054 — Business details | `GET /api/platform/businesses/{id}` shows name, owner, business type/country/currency/timezone, plan and subscription/trial expiry. Read-only, avoids unrelated tenant operational data | UX REVIEW PENDING |
| BS-055 — Supported-type toggles | Not implemented; changing a type must be audited, show downstream implications and never enable an unfinished vertical | TODO |
| BS-056 — Plans & entitlement editing | Not implemented; prices, quota rules, effective dates, versioning and existing customer contracts require approved design | TODO |
| BS-057 — Trial admin adjustments | Not implemented; extension/expiry rules, reason, rights and audit log are prerequisites | TODO |
| BS-058 — Platform audit history | Not implemented; historical data source, retention and sensitive-field policy pending | TODO |
| BS-059 — Admin account provisioning | Not implemented as an API/UI; no self-service grants or automatic Owner promotion. Provisioning must be a controlled, audited privileged operation, with MFA before production | TODO |
| BS-060 — Platform cross-role denial | Backend tests cover anonymous user, regular Business Owner, authorized directory-only admin, denied license access, cross-business read-only directory, paging, and immediate permission revocation | UX REVIEW PENDING |

## Security and implementation details

- Separate platform privilege: `AspNetUserClaims` / `platform.permission = businesses.view`. Uses the existing numeric `IdentityUser<long>` and `AppDbContext`; **no new table, migration, API project, or generic role framework**.
- Directory endpoints query platform control-plane data only; they never bypass operational BusinessId isolation for Retail records.
- No Platform Admin can change a subscription, activate a business type, grant admin permissions, or extend trial using these read-only endpoints.
- Permission removal is rechecked from the DB, so revoked access fails without requiring old authentication cookies to expire.
- Production account signup is still deliberately blocked pending verified-email delivery; admin provisioning is not falsely described as completed.

## Task-by-task UI/UX gate

**Static review:** admin link hidden from ordinary users; selected nav state follows current page; search has label, clear action and loading/empty/error states; page navigation has explicit Previous/Next controls; owner and subscription status appear as separate columns; detail view has a clear Back action, readable label/value pairs and no fake business metrics.

**Additional fix:** API list now includes the trial end and displays “Trial expired” rather than “Trial” if the trial end has passed. Paging input is bounded to avoid invalid skip arithmetic. Repeated View controls now have per-business accessible names.

**Actual rendered validation pending:** browser at 360px/768px/1440px, keyboard-only navigation, screen reader, real Platform Admin account provisioning, view/detail click-through and post-revocation UI recovery. A passing React build does **not** certify visual usability.

## CI and quality evidence

- Platform API and negative security tests commit: `20cb611`.
- UI commit: `13a391e`.
- Expired-trial and page-bound check: `021015c`.
- Contextual View labels: `edd49eb`.
- [Platform directory CI](https://github.com/dharmendra1031/business_saas/actions/runs/43148405714) for `13a391e` **PASS** across backend, SQL Server and frontend.
- [Final runtime CI 43148748888](https://github.com/dharmendra1031/business_saas/actions/runs/43148748888) for `edd49eb` — **PASS**: Backend **8/8**, SQL Server Integration **1/1**, React TypeScript Production Build **PASS**. Subsequent documentation commits do not modify runtime code.

## Next gate

Before any live business-type, plan or trial write action: approve permission/provisioning model, immutable audit trail, change reason/effective date, negative cross-role tests and full owner-facing UI impact checks. Do not expose a generic configuration editor.
