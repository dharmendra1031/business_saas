# Salon SaaS — Master Product & Engineering Plan

**Status:** Planning approved; implementation not started  
**Date:** 7 November 2026  
**Target repository:** `dharmendra1031/salon_saas`  
**Reference only:** `dharmendra1031/Kaifac_salon`, branch `development`  
**Reviewed source commit:** `3ebb119f0cb62f699513ef343861ebd897588950`  
**Target:** Support a platform of up to 10,000 salons; concurrency must be verified by performance testing.

## 1. Repository and scope rules

1. **All new work is exclusively in `dharmendra1031/salon_saas`.** No changes, commits, branches, PRs, database operations or deployments in Kaifac or other projects.
2. Kaifac Salon's `development` branch is a **read-only reference** for functional code, business rules and existing UI.
3. **Controlled clean rewrite:** implement a new maintainable codebase in `salon_saas`, using Kaifac only to understand verified workflows, rules and expected outputs. Deliver small end-to-end modules rather than a risky big-bang rewrite.
4. Document the affected screen, API, business rule, database and permissions before changing a workflow.
5. Application entity primary keys will be **numeric auto-increment IDs** (`BIGINT IDENTITY` / `INT IDENTITY`); no UUID/GUID IDs.
6. Do not include demo customers, fake transactions, hardcoded salon details, secrets or production data.
7. Keep code human-readable: clear names, focused methods, standard .NET and React conventions, minimal CSS, no unnecessary abstractions.

**Security blocker:** `salon_saas` is currently **public**, whereas the Kaifac repository is **private**. Never copy Kaifac source, `.bak` files, deployment configuration, keys, passwords, customer data or private assets into the public repository. Get an explicit repository-visibility and source-sharing decision before any source import. A public plan document is not permission to publish private code.

## 2. Architecture decision

**Modular Monolith — not Microservices.**

| Layer | Target |
| --- | --- |
| Backend | New ASP.NET Core .NET 10 LTS API, informed by proven Kaifac business concepts |
| Frontends | React + TypeScript: separate Platform Admin, Salon Admin and POS |
| Persistence | SQL Server + EF Core |
| Identity | Salon-scoped login, server-enforced roles and permissions |
| Mobile | Existing Flutter apps are reference material; optional later phase |
| Reporting | Preserve existing financial reports and RDLC contracts |
| Background jobs | Only for notifications, renewals, outbox and heavy work when required |
| Scaling | Stateless API replicas, indexes, bounded queries, job separation and measured load tests |

Keep understandable Domain, Application, Infrastructure and API boundaries in the new implementation where they add value. Preserve verified business invariants via tests, not by blindly copying old components. Do not create microservices or redundant layers without a demonstrated requirement.

### Core business modules

- Platform accounts, salons, branches and subscriptions
- Users, roles, permissions and tenant authorization
- Customers, services, categories, packages and branch prices
- Appointments, staff availability and schedules
- POS, cash register, payments, split tender, receipts, returns and voids
- Products, purchase orders, goods receiving, adjustments and stock transfers
- Employee attendance, commissions and payroll
- Loyalty, salon-customer memberships and customer benefits
- Reporting, notifications, configuration and audit logs

**Platform subscriptions are not salon-customer memberships.** Model their business lifecycles separately.

## 3. Findings in current Kaifac reference

These findings come from the inspected `development` source. They do **not** mean that all business flows have been runtime tested.

### Technical blockers for SaaS

- `InitialSetupService` checks whether **any** salon exists and rejects a second setup. SaaS onboarding must work per salon.
- `AuthenticationService.FindUserAsync` looks up email/phone without first filtering by salon and rejects duplicate matches as a single-salon integrity issue.
- Public branding can fall back to the first active salon if no salon code is provided. A multi-tenant request must resolve the intended salon reliably.
- Many domain entities already have `SalonId`; reuse this as the tenant key instead of adding redundant `TenantId` everywhere.

### Admin navigation and maintainability

- Related operations are spread across Workspace, Growth, Finance and Stock sections.
- `Packages` and `Branch Service Prices` both link to `/service-packages`, creating ambiguous navigation.
- Inventory groups Stock, Suppliers, Purchase Orders, Goods Receipts, Transfers and Barcode Labels into six tabs; a better purchase/stock task sequence is needed.
- Several large components combine orchestration, UI and state. Examples: `ReportsPage.tsx`, `EmployeesPage.tsx`, `UserAdministrationPanel.tsx`.
- Existing reports, settings, permissions and branch-switching must be preserved during UX simplification.

### POS workflow

- Main POS tools group Held Bills, Barcode, Benefits, Cash Register and Employee Work within a dialog.
- Everyday cashier actions require clearer, more direct access.
- Payment flow includes a keypad, selection, adding payment and final confirmation even for basic transactions.
- Improve labels, state visibility and button hierarchy, but do **not** relax discount, payment, tax, stock, cash-register or refund validations.
- Inspect existing retry/idempotency and held-bill recovery behaviour before changing checkout.

## 4. Multi-tenant foundation

### Tenant model

`SalonId` is the salon/tenant key, `BranchId` identifies an authorized branch within that salon.

1. Determine the salon through a trusted salon-domain/code context and a validated authenticated session.
2. Never accept an arbitrary client-provided `SalonId` as sufficient authorization.
3. Scope user authentication, branch switching and every business operation to the selected salon.
4. Apply authorization at the API/business-service boundary; UI visibility does not replace server authorization.
5. Filter salon-owned queries, projections, raw SQL, exports, reports, attachments, caches and background jobs.
6. Validate that a user and branch always belong to the selected salon.
7. Add tenant-aware unique constraints and composite indexes after reviewing the existing schema.
8. Protect cross-salon access in generated documents, file URLs, audit entries and idempotent operations.
9. Separate Platform Super Admin access from Salon Owner/Admin access; any cross-tenant support access must be explicit and audited.

EF Core query filters can help prevent accidental leaks but are not enough alone. Test direct access to another salon's customer, invoice, report, appointment and attachments by changing entity IDs and query parameters.

### Onboarding

- Unique salon identity/code and verified owner.
- Atomic creation of salon, primary branch, settings, owner role/user, entitlements and document sequences.
- Duplicate requests are handled safely; partial failures do not leave incomplete salons.
- Login and public branding resolve the correct salon.
- Salon can create its own branches and users without accessing another salon's data.

### Platform subscription lifecycle

Plans, trials, activation, renewal, grace period, failed payments, suspension, cancellation, module entitlements and invoices. Implement payment provider webhooks with signature verification and idempotency. Define data/export access during expiry before enabling restrictions.

## 5. Admin Panel UX specification

### Proposed navigation

| Menu | Tasks |
| --- | --- |
| Dashboard | Today's overview and actionable warnings |
| Appointments | Calendar, booking, status, staff availability |
| Customers | Directory, profile, history, loyalty |
| Services & Pricing | Services, categories, packages, branch prices |
| Staff | Employees, shifts, attendance, commissions, payroll |
| Inventory & Purchase | Products, suppliers, PO, receiving, stock, transfers |
| Sales & Reports | Sales, reconciliation, day closing, reports |
| Settings | Branches, users, roles, permissions, taxes, payment methods, language |
| Platform Console | Separate Super Admin experience; not a salon menu |

**UX acceptance rules**

1. One unambiguous menu item and one clear route per business task.
2. Menus and operations respect the logged-in role and active branch.
3. Page header shows title, branch/scope, primary action, filters and meaningful state.
4. Search/filter/pagination are server-driven where datasets can grow.
5. Do not show misleading totals calculated from incomplete client-side pages.
6. Forms open with focus on the first editable control and correct edit prefill.
7. Inline validation, clear saving/success/error feedback, keyboard access and protection against duplicate submissions.
8. Warn before discarding unsaved changes during branch switching or navigation.
9. Confirm destructive actions using clear, task-specific text.
10. Use branch business timezone for operational dates, never the browser timezone by accident.
11. Maintain responsive layouts for desktop, laptop and mobile without unusable horizontal overflow.
12. Keep English/Arabic, RTL, permissions and accessibility functional.
13. Reuse consistent form, table, dialog and navigation components; remove duplicate CSS only after checking visual regressions.
14. No unnecessary dashboards, gradients, oversized cards, animations, or decorative widgets.
15. Show empty, loading and error states that explain the user's next action.

### Critical Admin journeys

- Owner setup -> branch -> staff account -> services -> appointment.
- Service -> branch price -> appointment -> checkout.
- Supplier -> PO -> partial receiving -> stock update -> transfer.
- Employee -> branch assignment -> shift -> appointment -> commission.
- Branch-based report -> totals -> export -> print.

## 6. POS UX specification

**Normal sale:** Select item/service → select customer or walk-in → payment → completion/receipt.

1. Show salon, branch, cashier, POS/register state and connectivity clearly.
2. Keep catalog search and barcode entry accessible without unnecessary dialogs.
3. Display item types, price, quantity, stylist assignment, discounts, taxes and final total clearly.
4. Do not force customer registration for an ordinary walk-in unless the business rule requires it.
5. Optimize a **single payment** first; expose split payment as an explicit option.
6. Always show total, paid, remaining, cash tendered and change for the appropriate payment method.
7. Keep held bills, invoice history, print/reprint and cash register easy to find.
8. Disable or guide payment when the register is not in a valid state.
9. Keep refund, void, discount and override permissions enforced by backend checks.
10. Handle processing, success, failure and **unknown completion outcome** differently. Never post a duplicate sale on retry.
11. Preserve server-authoritative pricing, tax, inventory posting, membership, loyalty and payment totals.
12. Support fast keyboard, scanner and touch interactions; correct focus and tab order.
13. Avoid hard-coded English in Arabic mode and test 80 mm receipts.
14. Confirm that held bills and drafts survive refresh/reconnect according to the intended shared persistence model.
15. Never improve apparent speed by silently omitting large catalog results or completed invoices.

### POS regression scenarios

- Walk-in cash and card payment.
- Split tender, exact payment, over-tendered cash and rounding.
- Appointment billing with permitted extra services.
- Employee selection and skill validation.
- Discounts with/without the required authority.
- Register closed/open; closing variance approval.
- Held bill save/restore across permitted sessions.
- Slow API, timed-out checkout, duplicate click and unknown transaction outcome.
- Full/partial refund, void, invoice reprint and stock reversal.

## 7. Scale and performance (10,000 salons)

10,000 **registered salons** is different from 10,000 simultaneously active checkout terminals. Capacity cannot be guaranteed without assumptions and load testing.

- Define tenants, branches per tenant, peak active cashiers, average sale rate, booking traffic and report workload.
- Index frequently queried `SalonId`, `BranchId`, date and status combinations where justified by query plans.
- Use projection, bounded pagination and correct server-side filtering.
- Keep financial transactions short with concurrency control and durable idempotency.
- Run slow exports and notifications asynchronously.
- Ensure tenant-aware caching, storage and event processing.
- Keep API stateless before adding multiple instances.
- Measure API p50/p95/p99, error rate, database waits, queue lag, connection saturation and hosting costs.
- Test synthetic workloads at 100 / 1,000 / target tenant counts, then increase actual peak operations.
- Scale the modular monolith before splitting into microservices.

## 8. Code quality rules

- Use the established ASP.NET Core layers and focused business services, not needless patterns.
- Make small changes; delete obsolete duplicate implementation only after verifying callers and behaviour.
- Do not rewrite EF migrations that have been applied. Add safe forward migrations.
- Numeric auto-increment database IDs throughout.
- Clear, conventional C#/TypeScript naming; comments explain business constraints, not obvious syntax.
- Keep page-level orchestration readable; extract large reusable forms, tables and pure calculation rules.
- Avoid redundant hooks, deep chains of wrappers/decorators and unnecessary API calls.
- Centralize relevant business rules without creating a generic framework.
- Preserve SQL transactions and inventory/financial invariants.
- Reuse style tokens and controls; maintain RTL and accessibility.
- Never commit secrets, real production data, backup files or generated build artifacts.

## 9. Roadmap and release gates

| Phase | Work | Acceptance |
| --- | --- | --- |
| P0 | Plan, repository visibility, code import safety | Docs saved; Kaifac untouched; privacy decision before copying |
| P1 | New clean solution skeleton, tenant foundation and tested business specifications | .NET 10 API and Platform Admin/Salon Admin/POS builds; no secrets; smoke tests |
| P2 | Build user-friendly Salon Admin business workflows | Core admin tasks validated using [UX Standards](./UX_STANDARDS.md) and business acceptance tests |
| P3 | Build streamlined POS and reliable cashier workflows | Payment, refund, stock, register, retry, keyboard and usability tests pass |
| P4 | True multi-salon onboarding and isolation | Cross-tenant access and authorization tests pass |
| P5 | Super Admin and SaaS plan billing | Plan, renewal, payment and expiry tests pass |
| P6 | Scale, security, monitoring and launch | Load tests, backup/recovery tests and go-live checklist pass |

**Financial correctness and cross-tenant security are release blockers.**

## 10. Definition of done for every future change

- Trace screen → frontend state → API → permission → business service → database.
- Describe the exact issue, user impact, affected files, risks and smallest fix.
- Keep working behaviour stable, with feature-focused automated tests.
- Run backend build/tests and Admin/POS lint, TypeScript check and production builds.
- Validate relevant browser, DB, permission and branch/tenant test cases.
- Check responsiveness, keyboard use, language/RTL and error paths.
- Inspect the diff for accidental changes, dead code, sensitive files or package bloat.
- Commit/push only within **`dharmendra1031/salon_saas`**.
- Provide the commit link and actual verification results. Unrun tests must be stated as unrun.

## 11. First implementation backlog

1. Confirm private-source handling and make an explicit privacy decision for the target repo.
2. Scaffold the new .NET 10 API and three React apps, using Kaifac development as read-only business reference only.
3. Build the existing Admin/POS/API; capture reproducible runtime flows and identify confirmed bugs.
4. Build task-based Admin navigation, forms, tabs, filters and role-based journeys against [UX Standards](./UX_STANDARDS.md).
5. Simplify POS cashier navigation, checkout and payments while preserving financial controls.
6. Implement tenant context, per-salon onboarding and secure login.
7. Audit all business modules and exports for cross-tenant leakage.
8. Implement SaaS subscriptions and Super Admin console.
9. Validate 10,000-salon workload with realistic performance tests.

## Decisions

- **D-001:** Work exclusively in `salon_saas`; Kaifac is read-only.
- **D-002:** Start with a Modular Monolith.
- **D-003:** Controlled clean rewrite; preserve proven Kaifac *business behavior* using reference inspection and regression specifications, not wholesale code copying.
- **D-004:** Use numeric auto-increment IDs and existing `SalonId` as tenant key.
- **D-005:** Focus Admin/POS on real salon workflows, not cosmetic effects.
- **D-006:** No private source copied to a public repo without reviewed authorization.
- **D-007:** This document describes intended changes; it does not claim that code was changed or tests passed.

## Additional mandatory release standards

- The user-friendly design and screen-by-screen acceptance matrix is maintained in [UX Standards](./UX_STANDARDS.md). All three apps require task-based browser, keyboard, responsive and language/RTL review; clean code alone is not a usability pass.
- Critical tenant, finance, security, subscription and recovery risks are maintained in [Engineering Guardrails](./ENGINEERING_GUARDRAILS.md). Every release must pass the documented risk-appropriate gates.
- New codebase targets supported **.NET 10 LTS**; Kaifac's older runtime remains read-only reference.
- No previous private source/history/binary/database transfer to the public SaaS repository without explicit visibility and source-sharing approval.
