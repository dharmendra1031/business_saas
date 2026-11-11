# Batch 03 — Accounts and Security (BS-021–BS-030)

**Reviewed:** 11 November 2026. Keep Identity's existing login/account code; no new authentication framework, tables, IDs or UI screens.

| ID | Change / current finding | Status |
| --- | --- | --- |
| BS-021 | Development-only registration and server-side password validation exist. Production registration remains disabled until real email delivery | BASELINE |
| BS-022 | Existing login/logout verified at API level; clearer wrong-email/password feedback and user-id-only sign-in/out logs added | UX REVIEW PENDING |
| BS-023 | Verified email delivery and link confirmation are not implemented; production signup must stay unavailable | TODO |
| BS-024 | Password recovery with token mail delivery not implemented; no insecure temporary-password workaround | TODO |
| BS-025 | Admin MFA enrollment/recovery not implemented; permission gate alone is not a substitute | TODO |
| BS-026 | Existing Identity password lockout now has regression test: four failures 401, fifth 429, correct password while locked 429. Further IP/account abuse limits still pending | UX REVIEW PENDING |
| BS-027 | Existing HttpOnly/SameSite cookies and CSRF middleware; existing no-token test remains | BASELINE |
| BS-028 | Frontend redirects to Login with a clear session-expired message after authenticated API returns HTTP 401, and clears cached business/CSRF state | UX REVIEW PENDING |
| BS-029 | Existing Platform license-permission revocation test and added membership-revocation test after login | UX REVIEW PENDING |
| BS-030 | Sign-in/sign-out and lockout are logged by numeric UserId only, without password/email/token. Persistent audit history, failed-attempt visibility and retention policy still pending | TODO |

## Mandatory UI/UX review after each task

- **Login:** Incorrect credentials now show “Incorrect email or password.” instead of “Please sign in to continue.” The lockout API shows a temporary lockout message.
- **Session expiration:** If a protected API returns 401, the app clears selected business/session state, returns to Login, and explains that another sign-in is needed. Do not redirect on an ordinary failed login or initial `/auth/me` check.
- **Business access revocation:** Even with the old browser cookie, removed membership cannot see a former business. Platform permission removal is tested separately.
- **Static code checks completed.** Real browser at 360/768/1440px, keyboard focus, session-cookie expiry timing, admin role and mobile device review are **PENDING**. No task is marked DONE until this is observed.
- **Email confirmation, password recovery and MFA cannot be approved** merely because the login form exists. These require secure delivery/recovery integration and independent negative tests.
- **CI:** attach actual run once successful; do not report unverified test success.

## GitHub verification

[Batch 03 CI](https://github.com/dharmendra1031/business_saas/actions/runs/42987573738) for commit `07f7eaf` — **all jobs SUCCESS**: backend **6/6 PASS** (including lockout test), SQL Server 2022 **1/1 PASS**, frontend typecheck/production build **PASS**. Real session-expiry browser, 360/768/1440px and keyboard review remain pending.

## Next work after this pair of batches

Finish real browser review for BS-011–030, then BS-031–BS-040 tenant permissions and business operations. Preserve the 10-task batch evidence standard.
