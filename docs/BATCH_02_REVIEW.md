# Batch 02 — Foundation (BS-011–BS-020)

**Scope:** architecture/CI review and minimal security/operations improvements. Status is evidence-based, not an assertion of production readiness.

| Task | Verified work or blocker | Status |
| --- | --- | --- |
| BS-011 | Existing one ASP.NET Core .NET 10 API and xUnit test project; no new API project | BASELINE |
| BS-012 | Existing single React/TypeScript app; no split frontend | BASELINE |
| BS-013 | Reviewed existing two EF SQL Server migrations, no schema change | BASELINE |
| BS-014 | Existing long/numeric Identity and business entity IDs, no GUID entity keys added | BASELINE |
| BS-015 | Added optional protected Data Protection key-ring configuration; secrets remain outside code; actual IIS secrets/permissions not tested | UX REVIEW PENDING |
| BS-016 | Existing backend & SQL Server CI workflows; require new run on this commit | BASELINE |
| BS-017 | Existing React typecheck/build CI; require new run on this commit | BASELINE |
| BS-018 | Added request ID response header and production Problem Details correlation; exception bodies remain generic | UX REVIEW PENDING |
| BS-019 | Added certificate-encrypted, persistent Data Protection key-ring opt-in. IIS restart/multi-node/restore test still required | UX REVIEW PENDING |
| BS-020 | Added secure Windows configuration notes to P1 implementation, but fresh installation walkthrough is pending | TODO |

## Every-task UI/UX gate

- Existing paths affected: Login, CSRF issuance, My Businesses, Create Business and dashboard. API response error references may be shown by support; **do not leak internal paths, passwords or stack traces**.
- Static review: unchanged forms/navigation, no new UI controls, no new operational data or demo numbers. Smoke-test requests for authenticated and unauthenticated users via CI; then inspect the actual browser UI.
- **Browser review remains pending** for 360/768/1440px, keyboard, mobile, login/session restart and IIS installation. Do not mark any UI-dependent task DONE based only on the code review.
- Evidence: CI run for the commit will be linked once verified; operational testing required before production release.

**Next:** Batch 03 BS-021–BS-030 includes lockout/revocation tests and session-expired UX. Email delivery, recovery and MFA remain explicitly blocked until their secure workflows exist.
