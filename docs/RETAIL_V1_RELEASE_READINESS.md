# General Retail V1 — release readiness checkpoint

**Decision: NO-GO for live merchants.** Source reviewed at `f2ebc02003437f63b14a91d22c193ff3edc2ec4f` on 2026-11-28. This is a separate audit, not a change to the approved SRS.

## Evidence

- [Build 46153163281](https://github.com/dharmendra1031/business_saas/actions/runs/46153163281): frontend, backend, SQL Server and Windows renderer jobs passed.
- [Browser QA 46152976539](https://github.com/dharmendra1031/business_saas/actions/runs/46152976539): passed on the preceding frontend commit `f20a9bf`. Browser mocks are not a live multi-user SQL acceptance run.
- Existing production defaults deliberately disable live payment and do not expose the development-only POS, cash-register and global catalogue routes. Do not set a live environment to Development as a workaround.
- No merchant deployment, live payment, real backup restore, hardware acceptance, or full bilingual/responsive audit is certified by these runs.

## Release blockers and required evidence

| Priority | Blocker | Acceptance before release |
| --- | --- | --- |
| P0 | Production route and capability policy | Explicitly approved release configuration, non-development staging startup and fail-closed role/module/subscription tests. No Development bypass. |
| P0 | Financial closure and concurrent posting | Prove register closing versus sale/return/collection race on SQL Server; recover stale totals and uncertain responses; reconcile open shifts and closed snapshots. |
| P0 | Credit returns and supplier accounts | Approve or explicitly exclude paid-credit refunds, credit exchanges, supplier reversals, invoices spanning multiple receipts and multi-invoice settlement. Never silently create negative liabilities or cash refunds. |
| P0 | Real payment and identity | Test Razorpay in isolated staging, payment/renewal/webhook/refund reconciliation; verified owner onboarding, protected secrets and durable encrypted Data Protection keys. No live payment keys in Git. |
| P0 | Database and recovery | Rehearse reviewed migrations on a restored staging database, verify tenant counts and stock/financial balances, test off-host backup restoration and rollback. |
| P1 | Business-wide daily report | Define business-date and currency boundaries, distinguish shift snapshots from daily transactions and test consolidated totals, without double-counting opening float. |
| P1 | User acceptance | Real integrated browser/API/SQL workflows for owner, manager and cashier; English/Arabic RTL and phone/tablet/desktop; scanner and printer acceptance without changing RDLC. |
| P1 | Operational reliability | Staging preflight, representative POS/database performance, monitoring/alerting, support and recovery runbook with actual evidence. |
| P1 | Catalogue identity | Manual ambiguous product review, pack-size distinction, private/linked barcode alias conflict acceptance. |

Use [Production database safety](PRODUCTION_DATABASE_SAFETY.md) for operational procedures. Keep test evidence linked to exact commits, and verify any newly added code separately. This checkpoint does not imply that all source files or all requirements have been audited.

Do not change main, approved SRS or RDLC templates for this checkpoint.
