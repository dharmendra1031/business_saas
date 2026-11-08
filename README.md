# Business SaaS

**Status:** Phase 0 complete; P1 development slice committed. Build/tests and production readiness require independent verification.

A self-service multi-business SaaS platform. A person registers once, creates one or more independently managed businesses, selects a published business template and plan, configures a first location and begins operations in the workflow appropriate to that business.

## Where to start

| Document | Purpose |
| --- | --- |
| [Business SaaS Master SRS](docs/BUSINESS_SAAS_MASTER_SRS.md) | **Authoritative** product requirements and the multi-business model |
| [Database Architecture](docs/DATABASE_ARCHITECTURE.md) | **Authoritative** BusinessId tenant isolation, numeric SQL identity IDs, schema rules |
| [Development Roadmap](docs/DEVELOPMENT_ROADMAP.md) | Phase ownership, dependencies and release quality gates |
| [Retail V1 SRS](docs/RETAIL_V1_SRS.md) | First planned working business: General Retail Shop |
| [Subscription Plans](docs/SUBSCRIPTION_PLANS.md) | Starter, Growth, Professional, Business, Enterprise; plan/version entitlements |
| [Language Management](docs/LANGUAGE_MANAGEMENT.md) | Platform-published language files, fallback, RTL, per-business language preference |
| [UX Standards](docs/UX_STANDARDS.md) | User-friendly forms, keyboard/navigation, task-based UX testing |
| [CBK UI Reference](docs/CBK_UI_REFERENCE.md) | Compact blue/white UI patterns from read-only CBK source |
| [Engineering Guardrails](docs/ENGINEERING_GUARDRAILS.md) | Financial, tenancy, security, privacy and release safety |
| [P1 Implementation](docs/P1_IMPLEMENTATION.md) | Run guide, committed endpoints, tests and launch blockers |

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
- **Lean V1:** One API project + tests; one React app with separate Platform/Business/POS route layouts; one SQL database. Feature folders, not premature projects/services/plugins.
- **Schema by working slice:** add only required tables/migrations, one supported General Retail template first. No Redis, queues, microservices or speculative 1,000-category engine without evidence. See [Roadmap](docs/DEVELOPMENT_ROADMAP.md) and [Database Architecture](docs/DATABASE_ARCHITECTURE.md).
- CBK and Kaifac `development` are **read-only private references**. Do not copy proprietary source, backup, data, logo, secrets or history to this **public** repository without explicit approval.

## Initial vertical rollout

1. **General Retail** — setup, products, purchase/receiving, stock, POS, returns, register and reports.
2. **Salon / Barber** — services, staff/resources, appointments and checkout.
3. **Repair Center** — job cards, technicians, parts and billing.

The long-term goal of **1,000+ business categories** is a product coverage target, *not* a claim that 1,000 industry workflows are ready. Actual release, adoption and concurrency are separately measured.

## Work status

The repository now contains a **development-only P1 foundation slice**: one API, one React app, numeric Identity IDs, Register/Login, Create Business, membership-scoped switching and Retail Dashboard shell. [Run instructions and limitations](docs/P1_IMPLEMENTATION.md). Retail transactions, migrations, verification email, subscription payments and production security/scale testing remain pending.
