# Business SaaS

**Status:** Development foundation with Retail catalog, stock, purchase orders and receiving. Not production-ready; POS billing and production release checks are pending.

A multi-business SaaS platform with **Super Admin-controlled business creation** and separate Business Owner/Admin and Cashier/POS workspaces. The platform owner provisions the business, assigns/invites its Owner and provisions the compatible plan and first location. Authorized users then operate only their business; actual code still needs the P0 provisioning/role-flow change described in the screen register.

## Where to start

For a fresh Windows development run with a separate test database, use the [local setup guide](docs/WINDOWS_LOCAL_SETUP.md).

| Document | Purpose |
| --- | --- |
| [Master Screen Register — 145 tracked UI items](docs/MASTER_SCREEN_REGISTER.md) | **Canonical screen IDs, role boundaries, page/tab/modal flows, statuses, acceptance protocol and one-screen-at-a-time development queue** |
| [Frontend-First Visual QA Tracker](docs/FRONTEND_FIRST_DESIGN_TRACKER.md) | Approved-image hashes, platform UI design progress, design-freeze requirements and backend-later release order |
| [Approved POS + Payment Popup](docs/APPROVED_POS_UI_UX.md) | Approved full-width Ibdaa-inspired 61/39 checkout design and payment dialog acceptance |
| [Business SaaS Master SRS](docs/BUSINESS_SAAS_MASTER_SRS.md) | **Authoritative** product requirements and the multi-business model |
| [Database Architecture](docs/DATABASE_ARCHITECTURE.md) | **Authoritative** BusinessId tenant isolation, numeric SQL identity IDs, schema rules |
| [White Label & Dedicated Database Plan](docs/WHITE_LABEL_DATABASE_IMPLEMENTATION.md) | Custom domains, safe tenant DB routing, provisioning, migration, provider support and release gates |
| [Offline / On-Premise Deployment Plan](docs/OFFLINE_ON_PREMISE_DEPLOYMENT.md) | Independent local IIS/API/SQL deployment, offline license, LAN auth, backup, updates and release tests |
| [License Management Integration](docs/LICENSE_MANAGEMENT_INTEGRATION.md) | Reuse existing private licensing projects in Platform Admin and offline package; tests and release gates |
| [Development Roadmap](docs/DEVELOPMENT_ROADMAP.md) | Phase ownership, dependencies and release quality gates |
| [190-Step Development Task Tracker](docs/DEVELOPMENT_TASK_TRACKER.md) | 190 numbered tasks, status, dependency sequencing and mandatory UI/UX review after **every** task |
| [Retail V1 SRS](docs/RETAIL_V1_SRS.md) | First planned working business: General Retail Shop |
| [Subscription Plans](docs/SUBSCRIPTION_PLANS.md) | Starter, Growth, Professional, Business, Enterprise; plan/version entitlements |
| [Language Management](docs/LANGUAGE_MANAGEMENT.md) | Platform-published language files, fallback, RTL, per-business language preference |
| [UX Standards](docs/UX_STANDARDS.md) | User-friendly forms, keyboard/navigation, task-based UX testing |
| [CBK UI Reference](docs/CBK_UI_REFERENCE.md) | Compact blue/white UI patterns from read-only CBK source |
| [Engineering Guardrails](docs/ENGINEERING_GUARDRAILS.md) | Financial, tenancy, security, privacy and release safety |
| [Approved UI/UX Reference](docs/APPROVED_UI_UX.md) | Approved four Super Admin screenshots, exact navigation/features, visual fidelity and role-specific UX guardrails |
| [Platform Owner Setup](docs/PLATFORM_OWNER_SETUP.md) | Single Super Admin identity, customer boundaries and secure provisioning |
| [P1 Implementation](docs/P1_IMPLEMENTATION.md) | Run guide, committed endpoints, tests and launch blockers |
| [Production Database Safety](docs/PRODUCTION_DATABASE_SAFETY.md) | Backup, tested restore, migrations and readiness checks before deployment |
| [API Performance Baseline](docs/PERFORMANCE_BASELINE.md) | Lightweight read-only smoke tool and measurement guidance |

### Vertical-specific reference documents

The documents below preserve valuable **Salon-specific** workflow knowledge for the future Salon/Barber release. They do **not** define the new platform-wide tenant model:

- [Salon Vertical Plan](docs/SALON_SAAS_MASTER_PLAN.md)
- [Salon Business Workflows](docs/SALON_BUSINESS_WORKFLOWS.md)
- [Salon Appointment & Financial Rules](docs/APPOINTMENT_FINANCIAL_RULES.md)
- [Salon Pilot Acceptance Plan](docs/PILOT_ACCEPTANCE_TESTS.md)

## Platform boundaries

- **One identity account can access many separately owned/authorized businesses.**
- **BusinessId** is the tenant key; **BusinessLocationId** scopes operational locations.
- Business categories and published templates select available operations; unsupported categories are not advertised as working.
- Subscriptions, modules, roles and branch permissions are enforced server-side.
- Application entity keys are **numeric auto-increment** (SQL Server IDENTITY), including auth users and roles; no GUID/UUID entity IDs.
- Proposed stack: ASP.NET Core .NET 10 LTS, EF Core, SQL Server; React + TypeScript + Bootstrap 5; modular monolith.
- **Lean V1:** One API project + tests; one React app with Platform/Business/POS route layouts; shared SQL Server by default. Enterprise may later use a dedicated approved database without product forks. Feature folders, not premature projects/services/plugins.
- **Schema by working slice:** add only required tables/migrations. White-label branding/custom domain and database mode are distinct: shared SQL Server is implemented; dedicated SQL Server, customer-hosted databases and other certified engines are **planned, not built**. Routing requires central authorization and secure secrets; no Redis, queues, microservices or speculative category engine. See [Roadmap](docs/DEVELOPMENT_ROADMAP.md) and [Database Architecture](docs/DATABASE_ARCHITECTURE.md).
- CBK and Kaifac `development` are **read-only private references**. Do not copy proprietary source, backup, data, logo, secrets or history to this **public** repository without explicit approval.

## Initial vertical rollout

1. **General Retail** — setup, products, purchase/receiving, stock, POS, returns, register and reports.
2. **Salon / Barber** — services, staff/resources, appointments and checkout.
3. **Repair Center** — job cards, technicians, parts and billing.

The long-term goal of **1,000+ business categories** is a product coverage target, *not* a claim that 1,000 industry workflows are ready. Actual release, adoption and concurrency are separately measured.

## Work status

The development branch includes authentication, existing **owner self-service** business/trial creation (to be replaced with Super Admin-only provisioning), tenant-scoped Retail masters, stock ledger and adjustment review, purchase orders and partial goods receiving, a Development-only cash-sale prototype and partial UI components. The CI covers backend, SQL Server migration/integration and a locked frontend build. **The approved Ibdaa-style POS layout and Payment Popup, release-ready POS tax/payments, returns, register, reports, verified owner onboarding, subscription-payment acceptance, restore drills and production-scale security/load testing remain pending or incomplete.** Production signup is disabled until email verification is implemented.
