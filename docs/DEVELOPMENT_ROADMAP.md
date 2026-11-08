# Business SaaS — Development Roadmap, Phase Gates and Decisions

**Status:** Phase 0 documentation prepared and link-checked; business/country decisions remain open; application code not started  
**Updated:** 2026-11-08  
**Repository:** `dharmendra1031/business_saas` ONLY  
**Source-of-truth:** [Master SRS](./BUSINESS_SAAS_MASTER_SRS.md) · [Database Architecture](./DATABASE_ARCHITECTURE.md) · [Retail V1 SRS](./RETAIL_V1_SRS.md)  
**Design:** [CBK UI Reference](./CBK_UI_REFERENCE.md) · [UX Standards](./UX_STANDARDS.md)  
**Legacy vertical reference:** Kaifac `development` for salon behavior only; CBK `development` for layout/workflow reference, both read-only.

## 1. Development principles

- Build the **smallest production-quality vertical slice**: specification -> database migration -> API -> usable UI -> authorization -> tests -> documentation.
- Never mark a module done because a page displays or an API responds. Its real business journey, negative cases, security boundaries and user experience must be tested.
- Do not create 1,000 business-specific code branches. Organize reusable foundations and **verified** workflow families, then add catalog templates for supported variations.
- Do not hardcode subscription pricing, language packs, tax details, business-type-specific menus or a single SalonId tenant model.
- Use .NET 10 LTS ASP.NET Core modular monolith, EF Core + SQL Server, React + TypeScript + Vite + Bootstrap. No microservices until measurements justify.
- SQL application IDs numeric IDENTITY. No UUID/GUID PKs or application entity IDs.
- Clean developer-style code, minimal purposeful layers, no duplicate abstractions or code copied from private repositories into public `business_saas`.
- Feature flags/unsupported templates are honest and enforced by server. No fake dashboard transactions.

## 1A. Strict no-over-engineering rules

1. **First runnable slice:** login -> create Retail business -> first location -> product -> receive stock -> bill -> return -> report. Do not scaffold Salon, Repair, Rental, Class or Projects until needed.
2. **One API and one web app:** separate role-based layouts and feature folders, not multiple deployments or cross-app synchronization.
3. **One SQL Server database:** no sharding, separate database per category, Redis, Kafka, message broker, microservices, event sourcing, CQRS, generic repository/unit-of-work wrappers or home-grown workflow engine in V1.
4. **Minimal tables per working slice:** use the phased physical schema in [Database Architecture](./DATABASE_ARCHITECTURE.md). The future entity catalog is not a migration checklist.
5. **No framework for hypothetical 1,000 categories:** one supported Retail type, basic declarative default modules in code/config; add persisted template editor/versioning only when a second real workflow needs it.
6. **Simple roles:** Owner, Manager and Cashier with explicit API policies first; add configurable role/permission editor only when verified customer requirements demand it. Business membership and location checks remain mandatory.
7. **Subscription basics first:** one business trial/plan entitlement path and clear usage limits. Payment automation, overrides, complex add-on billing and historical plan versions are implemented only when actual paid operations require them; never lose agreed plan terms.
8. **Language scope:** keep keys and localization hooks from first screen; begin with known published packs and implement the user-requested Platform Admin upload/preview/publish when Platform Admin localization is built, without a generic file-processing framework.
9. **No premature caching:** React query cache scoped by BusinessId; SQL indexes, pagination and correct queries first. Add distributed cache/background workers after real performance or reliable delivery need.
10. **Keep non-negotiables:** numeric IDENTITY IDs; verified tenant switching; permissions; financial idempotency; stock ledger; safe transactions; clear UX; responsive/RTL; backup/restore and tests.

**New-dependency and new-table test:** before adding a package, project, table, interface, background worker or engine, name the specific current feature requiring it. If none exists, defer it. Each delivered feature should have a small understandable diff and an end-to-end test.

## 2. High-level roadmap

| Phase | Scope | Deliverable | Gate |
| --- | --- | --- | --- |
| **P0 — Product & Architecture** | Master SRS, BusinessId multi-tenancy, database, templates, roles/plans, onboarding, Retail V1, risk/pilot standards | Reviewed authoritative documents | Phase 0 document consistency and key open decisions recorded |
| **P1 — Foundation** | Solution, identity with long IDs, auth/verification, business memberships, location, tenant security, audit, migrations | One user can safely create/switch businesses | Auth, numeric IDs, cross-tenant tests, build/migration pass |
| **P2 — SaaS Platform Admin** | Minimal business directory, supported-type toggle, module/plan mapping, trial status and requested language upload/publish | Central configuration for working Retail modules | Entitlement/role/limit and translation import/publish tests |
| **P3 — Self-Service Onboarding** | Signup, choose supported type, choose plan/trial, create business + first location, guided setup | Owner can open a usable business workspace | Idempotent provisioning, retry/resume, tested business switch |
| **P4 — General Retail V1** | Products, Suppliers, Purchase, Receiving, Stock, POS, Returns, Cash Register, basic Reports | First working shop end-to-end | Financial/stock reconciliation, usability pilot, browser/device tests |
| **P5 — Salon / Barber V1** | Services, Staff, Resources, Appointment Calendar, Booking, Checkout, manual Waitlist/Deposit where supported | Second verified workflow family | Concurrency/no-show, booking and checkout pilot |
| **P6 — Repair Center V1** | Customer, Job Card, Technician, Parts, Work Status, Billing | Third verified workflow family | Repair job and inventory/billing acceptance |
| **P7 — Operating Platform Launch** | Production payments, compliance per market, backups, monitoring, support, security, scaling | Production pilot then staged general rollout | No critical risk, tested restore and load test |
| **P8 — Expansion** | Other templates/workflow families, localized markets, add-ons, integrations | Evidence-based category growth toward 1,000+ | Support matrix and per-vertical acceptance |

**Execution detail:** Some P2 subscription and P3 onboarding foundation must be implemented together as narrow slices (e.g. minimum plan/trial resolution before business creation). The table states primary ownership/order, not an artificial ban on testing cross-phase integration.

## 3. Recommended project structure (implementation target)

```text
business_saas/
  backend/
    BusinessSaas.Api/
      Features/
        Auth/
        Businesses/
        Subscriptions/
        Retail/
      Data/
    BusinessSaas.Tests/
  frontend/
    web/
      src/
        app/
        features/
          platform/
          business/
          retail/
        components/
        styles/
  docs/
```

**One deployable ASP.NET Core API + one test project + one React/Vite app at first.** The React app contains three distinct route/layout groups: Platform Admin, Business Admin and POS. Lazy-load route groups so a cashier does not download the Platform Admin screens. Keep authorization exclusively on the backend.

Within the API, keep business logic in focused feature/service files and EF Core in `Data`. Do not create separate Domain/Application/Infrastructure projects, a shared UI package, generic repositories, a plugin framework, a worker, or microservices on day one. Extract a project/package only when repeat use, code ownership, independent deployment or measurable build complexity makes it necessary.

## 4. P0 detailed tasks and exit criteria

| Task | Outcome | Status |
| --- | --- | --- |
| Establish canonical Multi-Business SRS and source-of-truth | BusinessId, memberships, setup and entitlement behavior | Documented |
| Database architecture | Numeric identity users, global account/tenant model, module/version mapping | Documented; schema not migrated |
| Business Type / Template catalog | Supported vs unsupported, published versions and dependencies | Documented; admin not built |
| Single login / multi-business flow | Verified membership selection, scoped switch, safe cache reset | Documented; API not built |
| Subscription + role + location access | Server-side permission matrix and billing version rule | Documented; final prices/policy open |
| First retail workflow | Create business -> products -> receiving -> POS -> return -> reports | Documented; tests not run |
| Roadmap & reference governance | CBK/Kaifac read-only; old salon docs classified as vertical | Documented; cross-document links verified |
| Country-specific financial and privacy rules | Launch market, tax, payments, export/retention | **Open — must approve before production** |
| Source/privacy security boundary | Public repo may contain only independently authored docs/code | Mandatory; private source import prohibited |

No development implementation or performance testing is represented by these statuses.

## 5. P1 Foundation build sequence

1. Scaffold one .NET 10 API and test project + one React/Vite app with separate Platform, Business and POS layouts, minimal Bootstrap theme, CI build/test and secure environment config.
2. EF Core numeric identity `Users<long>`, platform roles, verified login, safe session management and optional MFA for privileged users.
3. Businesses, BusinessTypes, BusinessUsers, roles/permissions, Locations and membership-based BusinessId context.
4. `POST /businesses` uses a durable server-side idempotency boundary; create owner, first location, basic settings and trial/subscription safely.
5. Scope all queries by BusinessId, validate LocationId, authorize linked entity IDs; test two businesses under one user and two users.
6. Build business switcher and onboarding progress; ensure in-flight requests/caches cannot show previous business information.
7. Audit/error logging, secrets management, rate limits, backup baseline and branch-based deployment.
8. Build + integration tests, migrations, browser smoke flows and negative authorization before moving on.

## 6. P2/P3 SaaS Admin and onboarding milestone

### Platform Admin screens

- Business Directory and Business Details (status, type, owner membership, subscription, usage).
- Supported Business Type list and activate/deactivate status. Add category tree only when a sufficiently large real catalog needs it.
- Simple setup defaults for the released Retail template; build template editor/versioning only when multiple maintained live versions require it.
- Modules and dependencies used by Retail, Plan/Trial setup, effective limits. Add paid plan version history, complex overrides and add-on billing when real paid customers require them.
- Language Master, CSV/JSON pack validation/preview/publish/rollback.
- Basic subscription status and audit. Add payment reconciliation, support desk, advanced settings alongside their actual integrated provider/features.

### Onboarding and entitlement flows

- No manual DB provisioning. Registration/verification; choose business type; plan/trial; create named business; assign owner and first location; finish category-specific setup.
- Plan comparison and billing terms correct for country; incompatible plan cannot be chosen.
- No unsanctioned second account or new trial to bypass limits.
- Every module API checks subscription state + entitlement + role + location + BusinessId.
- Bad translation uploads cannot replace last good published pack.
- Business context switching does not allow cross-tenant report/download URLs.

## 7. P4 Retail V1 slices and release evidence

| Slice | Includes | Must-pass examples |
| --- | --- | --- |
| R1 — Retail Setup | Business/location, store profile, permissions, tax/receipt config | First location and owner, no fake data |
| R2 — Catalog | Category/Unit/Product/Supplier/Customer | Duplicate SKU, inactive product, scoped search |
| R3 — Stock | Opening Stock, movement ledger and balance | No double opening, no negative stock on concurrency |
| R4 — Procurement | Purchase Order, partial/full receiving and report | PO 100 -> receive 40 + 60, no over-receiving |
| R5 — POS | Cash/card/split, discounts, posted invoice, printer | Duplicate-click and timeout idempotency |
| R6 — Returns | Full/partial return, refunds, inventory reversal | No over-refund; original invoice immutable |
| R7 — Cash & Reports | Register opening/close and essential reports | Cash vs card, variance, branch/day reconciliation |
| R8 — Usability Pilot | Owner/manager/cashier, RTL, scanner/printer | Task completion and error recovery measured |

A real "retail-ready" demo includes workflows from R1 through R7 using a test business, not static screenshots.

## 8. Testing matrix and release no-go criteria

| Layer | Required checks |
| --- | --- |
| Build | dotnet restore/build/test, React typecheck/build/lint, dependency/license review |
| DB | Fresh database migration, forward migration, FK isolation, rollback/restore |
| Auth | Multi-business switching, changed role/location permissions, expired token, unsupported template |
| Subscription | Trial, plan version, add-on/limit, grace and disabled module both UI/API |
| Retail | Purchase/receiving, ledger, refund, stock, invoice, cash register concurrency |
| Localization | English/Arabic RTL and uploaded third language, printed output |
| UX | Form focus/prefill, data tables, mobile, keyboard, hardware, clear errors, pilot observations |
| Payments | Double-click, late webhook, unknown outcome, provider reconciliation; no double capture |
| Operations | Logs and alerts, backup restore, predictable deployment rollback, load test |

**NO GO** while cross-business data leakage, double charge, incorrect stock/financial reconciliation, broken critical cashier flow, or untested restore is unresolved. A pretty UI or passing unit tests alone is not release evidence.

## 9. Capacity and category expansion policy

Track separately:
- **Business categories listed** (taxonomy).
- **Templates published and tested** (supported coverage).
- **Registered/active businesses** (commercial adoption).
- **Branches, cashiers and checkout/booking requests at peak** (runtime capacity).

Scaling approach: optimize proper tenant indexes, server-pagination, transaction locks, short transactions, stateless API and isolated background exports first. Measure SQL plans, contention, memory, API p50/p95/p99; only then assess separate databases, read replicas or other infrastructure. 1,000 category names do not require 1,000 databases or services.

## 10. Important product owner decisions (explicitly open)

- Launch market: India or Kuwait first; country and payment provider policy.
- Whether General Retail remains first full release, followed by Salon and Repair.
- Public product branding and legal data-controller/processor terms.
- Trial duration/prices, plan feature compatibility by business type, over-limit handling.
- Supported printers/scanners, fiscal/tax invoices and refund/credit flow per market.
- V1 customer credit and multi-location transfer scope.
- Compliance and retention policy for business customer data.

Open decisions should be tracked as explicit issues or decision records before production coding of their dependent functionality, not hidden as hardcoded assumptions.

## 11. Repository policy

All new docs/code/commits inside `dharmendra1031/business_saas`. Existing `SALON_*` documents are **Salon vertical references** and should be labeled as such; their historical `SalonId` semantics never supersede canonical platform `BusinessId`. CBK and Kaifac remain read-only. Do not import or expose their private source/assets to the public repo.

**Current state:** Documentation and planning only. No implementation, database migration, production release or pilot is claimed.
