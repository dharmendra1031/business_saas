# Salon SaaS — Engineering Guardrails and Release Gates

**Status:** Mandatory engineering specification; implementation and tests not yet completed  
**Updated:** 2026-11-07  
**Working repository:** `dharmendra1031/salon_saas` only  
**Reference:** `dharmendra1031/Kaifac_salon` `development` branch (READ-ONLY)  
**Companion docs:** [Master Plan](./SALON_SAAS_MASTER_PLAN.md) · [UX Standards](./UX_STANDARDS.md) · [Plans](./SUBSCRIPTION_PLANS.md) · [Languages](./LANGUAGE_MANAGEMENT.md)

## 1. Engineering decisions

- **Controlled clean rewrite:** New project, clear business ownership, tested parity with the Kaifac workflows. No blind code copy or big-bang release. Work feature-by-feature from business specifications to UI, API, database and tests.
- **Modular monolith:** ASP.NET Core API, React + TypeScript (Platform Admin, Salon Admin, POS), SQL Server + EF Core, selective background processing. Do not build microservices without measured need.
- **Runtime:** New backend targets **.NET 10 LTS** with supported compatible libraries; the Kaifac .NET 8 backend is reference only. Confirm dependency compatibility before implementation.
- **Database keys:** `BIGINT IDENTITY` / `INT IDENTITY` for application entity primary keys. No GUID/UUID entity keys. Use existing conceptual `SalonId` as the tenancy key instead of a duplicated tenant column.
- **Private-to-public security gate:** At the time of writing `salon_saas` is public and Kaifac is private. No private source, backup, secrets or real customer data may be transferred to this repo until its visibility and publication permissions are explicitly approved.
- **No demo data in production UI:** Seed essential system settings/module codes only where necessary, in an auditable and idempotent way. Synthetic fixtures belong in tests.
- **Real workflows first:** UX completion is a release requirement, not a cosmetic task. Follow [UX Standards](./UX_STANDARDS.md).

## 2. Critical risk register

| ID | Priority | Failure | Required mitigation |
| --- | --- | --- | --- |
| R01 | BLOCKER | Salon A can access Salon B's customers, invoices or files | Trusted salon context, server-side scope checks, cross-tenant negative test suite |
| R02 | BLOCKER | Duplicate charge or sale after timeout/retry | Durable idempotency and server reconciliation; show outcome-unknown state |
| R03 | BLOCKER | Wrong amount/tax/discount/stock due to concurrency or rounding | Decimal currency arithmetic, single server authority, transaction and ledger tests |
| R04 | BLOCKER | Private Kaifac data/code exposed through public repo | Visibility/source approval, import allowlist, secret scan, never copy .bak |
| R05 | BLOCKER | Disabled/expired module or wrong branch bypassed through API | Subscription + entitlement + role + branch + tenant checks on every sensitive action |
| R06 | HIGH | Database upgrade causes loss/outage | Additive migrations, backup/restore drills, tested rollback and deployment plan |
| R07 | HIGH | Incorrect expiry/renewal blocks daily salon work | Published grace/recovery policy, warnings, invoice/export access rules |
| R08 | HIGH | Delayed payment webhook causes wrong subscription status | Signature validation, durable event processing, idempotency, reconciliation |
| R09 | HIGH | Peak POS traffic or slow reports overwhelms DB | Bound/paginate queries, tenant-aware indexes, slow-job isolation, load tests |
| R10 | HIGH | Cashier/owner cannot use rewritten UI | Role-specific task testing, keyboard/RTL/mobile checks, recorded usability findings |
| R11 | HIGH | Incorrect language or printed invoice | Versioned packs, placeholder validation, locale fallback, bilingual print tests |
| R12 | HIGH | Data cannot be restored after incident | Encrypted backups, restore tests, documented RPO/RTO and incident runbooks |
| R13 | HIGH | Misuse of PII or inappropriate support access | Least privilege, audited support, retention policy, launch-market privacy review |
| R14 | MEDIUM | Unexpected cost from storage/messages/exports | Usage meters, quotas, cost alerts, rate limits |
| R15 | MEDIUM | One giant codebase becomes unmaintainable again | Feature ownership, small focused components, reviews and change-size discipline |

This is an initial engineering risk assessment, **not** a claim that a penetration test or compliance certification has occurred.

## 3. Tenant and identity isolation

1. Resolve authenticated SalonId from a trusted tenant context. Never trust arbitrary SalonId query/body values for authorization.
2. Platform users and salon users have separate access boundaries; a Platform Admin does not automatically receive salon business data access.
3. Validate SalonId/BranchId and permission for each relevant read, write, report/export, file download and background job.
4. Audit EF queries, raw SQL, joins, projections, caches, idempotency keys and object storage for tenant scoping.
5. Require verified tenant context for login; resolve duplicate email/phone used by multiple salons without guessing.
6. Protect Platform Admin with MFA, role separation, audit trails and carefully controlled support access.
7. Prove a two-tenant negative test suite: attempts to access foreign appointments, customers, employees, sales, inventory, invoices, attachments, settings and reports must fail.
8. Hidden frontend modules are not security controls.

## 4. Money, stock and operational correctness

- Financial calculations must be server-authoritative using decimal precision, explicit rounding and agreed branch currency/tax rules.
- Payment collection, sale completion, invoice posting, loyalty benefit usage and stock deduction must have defined transaction and retry semantics.
- Keep posted records immutable when required; use audited refund/void/reversal operations, not silent mutations.
- Stock ledgers reconcile to balances; transfers do not create or destroy stock. Test concurrent transfers, insufficient stock, partial purchase receiving and returns.
- Define invoice numbering scope, cash register opening/closing and cashier authorization clearly.
- Store timestamps and branch business timezone deliberately; date/time localization must not change business calculations.
- Use secure payment provider integration that keeps sensitive card information out of application databases and logs. Assess applicable PCI/payment requirements before launch.

## 5. SaaS plans, languages and access

- Four public plans (Starter/Growth/Professional/Business), custom Enterprise, Trial as a lifecycle phase.
- SaaS Plan defines feature entitlements and limits. User permissions define allowed operations within those modules and branches.
- Treat plan versioning, add-on upgrades/downgrades and grace periods as distinct tested business rules.
- Support English/Arabic and additional uploaded language packs with preview/publish/rollback, English fallback and RTL.
- Do not hardcode plan prices, quotas, or language strings in the UI.
- Do not automatically delete historic business data when subscription expires or modules become disabled.

## 6. Availability, performance and operations

- Start with a stateless horizontally scalable modular monolith. Use a separate background worker only for justified notifications, billing jobs and heavy exports.
- Define real workload assumptions: number of registered salons, branches/users, peak concurrent checkouts, booking requests, reports and generated PDFs.
- Monitor error rate, p50/p95/p99 latency, SQL locks/waits, queue lag, payment failure and infrastructure expense.
- Back up data and files, verify restores in a separate environment, define recovery point/time objectives and test rollback.
- Implement rate limits, request validation, secrets management, actionable alerts, health checks and trace IDs.
- Test degraded database/network conditions and ambiguous checkout states, not only the success path.
- Capacity for 10,000 salons must be verified with representative load testing. Registration count alone is not concurrency.

## 7. Release gates

**Gate A — Product specification:** A task owner, expected behavior, permission scope, failure cases, translations and UX acceptance criteria are written.

**Gate B — Implementation:** Small understandable code changes, no redundant abstractions, reviewed schema migrations, no unrelated files.

**Gate C — Tests:** Backend unit/integration/authorization tests plus frontend types/build, browser journeys, error and concurrency scenarios as applicable. Tests must run; passing claims require evidence.

**Gate D — UX:** Role-based hands-on tasks, responsive widths, keyboard/focus, English/Arabic/RTL, print/receipt, validation and recovery are checked against [UX Standards](./UX_STANDARDS.md).

**Gate E — Security and resilience:** Tenant cross-access tests, secrets scan, authorization checks, backup/restore and critical billing idempotency are verified.

**Gate F — Release:** Rollback instructions, monitoring/alerts, staged rollout, migration plan, smoke tests and sign-off. No production release while a blocker is open.

For each delivered module record: business flow, affected data, permissions, automated test commands/results, UX testing evidence, known gaps, commit SHA and rollback steps.

## 8. Development sequence

1. Lock repository visibility/source-handling rules and architecture/acceptance documents.
2. Create clean backend/frontend skeleton and supported .NET 10 environment in `salon_saas`.
3. Tenant context, authentication and initial platform/salon admin workflows.
4. Complete per-module vertical slices with functional backend and user-friendly frontend.
5. POS financial posting and inventory correctness with strong regression tests.
6. Plan billing, language management, reporting, notifications and tenant scale work.
7. Staging, representative user testing, security/performance tests and operational readiness before public launch.

## 9. Reference standards

- Microsoft's .NET lifecycle: https://dotnet.microsoft.com/en-us/platform/support/policy
- OWASP ASVS: https://asvs.owasp.org/
- OWASP API Top 10: https://owasp.org/www-project-api-security/
- EF Core tenancy guidance: https://learn.microsoft.com/en-us/ef/core/miscellaneous/multitenancy
- WCAG 2.2: https://www.w3.org/TR/WCAG22/
- Usability heuristics: https://www.nngroup.com/articles/ten-usability-heuristics/

**Status note:** This document defines controls and tests. It does not certify that the future application meets them.
