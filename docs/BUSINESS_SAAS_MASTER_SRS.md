# Business SaaS — Master Software Requirements Specification

**Status:** Phase 0 design baseline; requirements approved direction, implementation pending  
**Last updated:** 2026-11-10  
**Authoritative repository:** `dharmendra1031/business_saas` (`main`)  
**Product:** Self-service multi-business operating platform  
**Related:** [Database](./DATABASE_ARCHITECTURE.md) · [Roadmap](./DEVELOPMENT_ROADMAP.md) · [Retail V1](./RETAIL_V1_SRS.md) · [UX Standards](./UX_STANDARDS.md) · [CBK UI Reference](./CBK_UI_REFERENCE.md)

## 1. Vision and measurable outcomes

One person registers once, creates one or more business workspaces, chooses a supported business type and eligible plan, configures the first shop/branch and begins operations without manual infrastructure setup **on the standard shared SaaS tier**. Dedicated/customer-hosted deployments are reviewed Platform Admin/Enterprise operations.

Target outcomes (not yet achieved):
- Owner completes a simple business onboarding and first useful business task in a **5–10 minute target** for the supported starter template, tested with real pilot users.
- A business never reads/writes another business's records through identifiers, exports, caches, files, reports or background tasks.
- Core retail sale, returns, inventory postings and plan transitions reconcile under retries and concurrency.
- Business Admin, POS/Operations and Platform Admin remain focused experiences.
- Long-term taxonomy can include **1,000+ distinct business categories**; this does **not** mean 1,000 fully implemented operational workflows at launch. Supported/preview/unsupported are explicit.

Do not build a generic engine that pretends every industry has identical operations. Start with proven workflow families, evolve with validated vertical requirements.

## 2. Product boundaries and decisions

- **One platform:** common identity, organizations, roles, branch/location, module entitlements, billing, audit, language and infrastructure.
- **Multiple businesses per user:** user can own/manage a salon and a retail shop independently; business switching changes the complete authorized context. A business can have multiple locations.
- **Subscription per business**, not per login account; plan/version, usage limits, module entitlements, billing and renewal belong to selected business.
- **Business type = setup template; not a security authority.** Product entitlement + user permission + active business/location scope determine access.
- **Default business-specific operations:** Retail uses Product/Purchase/Stock/POS/Returns; Salon uses Services/Appointments/Staff/POS; Repair uses Job/Technician/Parts/Billing. Do not force into one generic transaction table.
- **Public account vs platform account:** Platform Super Admin access is separately assigned and audited; it is not inherited from a business Owner role.
- **Technology:** ASP.NET Core **.NET 10 LTS** Modular Monolith, EF Core, SQL Server; React + TypeScript + Vite; Bootstrap 5, minimal custom design tokens, CBK as **read-only UX reference**.
- **Keys:** All app entity primary keys numeric auto-increment SQL IDENTITY. No UUID/GUID entity IDs, including Identity users/roles. External processor references, opaque verification/anti-CSRF tokens and operation tokens are not entity primary keys.
- **Privacy:** `business_saas` is public; CBK/Kaifac are private read-only references. No copying private source/assets, customer data, SQL backups, secrets or historical commits into public repo without explicit authorization.
- **No fake operational data** in app. Test-only fixtures and essential idempotent system catalog seed data are distinct.

### White-label, dedicated databases and customer-selected provider (approved product capability)

**Two separate choices:** branding/white-label does **not** imply a dedicated database, and dedicated database isolation does **not** imply a different database engine.

| Deployment offering | Branding | Operational data storage | Provisioning |
| --- | --- | --- | --- |
| Standard SaaS | Our product brand | Shared SQL Server, all records BusinessId-scoped | Self-service |
| Branded/white-label SaaS | Approved customer logo, app name, colors, verified domain and mail/receipt branding | Shared or managed dedicated DB, as contracted | Brand/domain verification by Platform Admin |
| Managed Enterprise dedicated | Customer branding optional | Independent SQL Server database for a selected BusinessId (or separately contracted business group) | Platform Admin; independent backup/restore |
| Customer-hosted Enterprise / BYO database | Customer brand and optionally isolated deployment | Supported customer-managed DB **after** connectivity, security, backups, network and compatibility checks | Explicit sales/technical onboarding; not an arbitrary self-service connection string |

- **Database provider choices are certified options, not "any database works"**: SQL Server shared + SQL Server dedicated are the first target. PostgreSQL, MySQL/MariaDB and Oracle are candidates **only after** a compatible EF Core provider, separate migrations, feature parity, payment/stock tests and operational support are verified. The UI must label unsupported engines "Contact Sales" or "Not available"; do not claim multi-engine implementation today.
- **Identity/control plane stays central for managed SaaS:** account, login, business ownership/membership, subscription and plan, domain/brand mapping, database assignment and audit. **Business operational data** (products, inventory, orders, invoices, customers and business files) follows the assigned data store. If the customer requires *all* personal, identity and billing data within their own infrastructure, negotiate an **isolated application deployment and identity model**, not just a separate operational DB.
- Keep **one common codebase** with the same APIs and React UI; branding comes from verified tenant configuration and never from hardcoded copies/forks. A reseller serving several businesses must not merge their BusinessIds or grant automatic cross-business rights.
- Platform Admin chooses supported **Shared / Dedicated / Customer-hosted** modes only after permission/contract and environment checks. Ordinary business users never submit raw database credentials, hostnames or connection strings to a working tenant API.
- Tenant-to-database routing must be resolved **server-side after authenticated BusinessId membership**; map trusted BusinessId to a vetted data-store assignment, never by user-supplied DB name, hostname or unverified domain. Credentials are stored in a secrets manager, not Git, ordinary business records or browser responses.
- A domain only becomes active after ownership proof, TLS setup and approved host-to-brand mapping. Domain selection cannot bypass login, BusinessId permissions, payment entitlement or location restrictions.
- **Customer migration** between shared/dedicated/provider is an explicit, audited maintenance workflow: backup, export/import, verify data and money/stock totals, pause writes at cutover, change mapping atomically and test rollback. No untested instant switch, cross-provider DB joins or dual-writes.
- Dedicated DBs need schema/version inventory, provider-specific reviewed migrations, isolated backups and restore drills, encryption, monitoring, region/data-residency controls and price/usage support. IDs remain numeric auto-increment *inside each database*. Combine BusinessId + local Id when identifying a record across deployments.
- **No premature infrastructure:** record this as an Enterprise requirement now; do not introduce a shard router, DB-per-category, provider plugins, automatic arbitrary DB provisioning or extra projects into the current P1 application. Before Retail P4 gains many cross-table dependencies, establish the control-plane/operational-data boundary and deliver/test the first **dedicated SQL Server** pilot. Add a second database provider only when an actual contracted need is approved.

**Acceptance criteria:** one account switches between a shared and dedicated business safely; invalid BusinessId/domain/role never accesses another data store; same workflow and numeric IDs function in both; backup/restore, migration and failback have evidence; secret rotation and account revocation work; branding/domain do not expose another business.

### Lean implementation baseline

- **Initial code shape:** one `.NET 10` Web API project with feature folders and one test project; one React/Vite application with separate lazy-loaded Platform Admin, Business Admin and POS layouts. These are **three experiences, not three deployments**.
- **Start with General Retail only.** Salon/Repair remain future vertical SRS until the Retail flow is genuinely tested. No code for hypothetical industries or 1,000 categories.
- **Initial Business Type template:** simple supported type + default module mapping in well-named code/config. No dynamic workflow engine, runtime schema generation, plugin system or template version database on day one.
- **First business roles:** Owner, Manager, Cashier with server-side policy checks and BusinessId membership. Add a custom role editor and per-location assignments as actual supported workflows require; never bypass tenant scoping.
- **Initial platform data:** verified account, business, membership, location and minimal trial/plan. Add module tables, translator upload tables and paid subscription history as their **working features** are implemented, not as speculative scaffolding.
- **No early distributed infrastructure:** one shared SQL Server initially; a verified managed dedicated SQL Server option is a contract-driven Enterprise milestone, not an excuse for Redis/queues/sharding/extra workers on day one.
- **Do not simplify away correctness:** numeric IDs, safe auth and business switching, restricted access, reliable money/stock posting and UI error recovery are mandatory.
- Follow the concrete [Development Roadmap](./DEVELOPMENT_ROADMAP.md) and its no-over-engineering rules before introducing any new layer, library or table.

## 3. Personas and access paths

| Persona | Workspace / route group | Jobs |
| --- | --- | --- |
| Platform Super Admin | `platform` layout | Business directory, categories/templates, plans, modules, quotas, translations, subscriptions, support/audit |
| Platform Billing/Support | `platform` layout | Restricted finance/support functions; no blanket customer data access |
| Business Owner | `business` layout | Create business, choose plan, locations, users, products/services, reporting |
| Location Manager | `business` layout | Assigned locations only; relevant approvals and inventory/operations |
| Cashier | `pos` layout | Fast sale, refunds if allowed, register, receipt |
| Receptionist/Stylist/Technician | Role-specific routes when supported | Bookings or jobs only when corresponding vertical supported |
| Public customer | Public URL where activated | Book/order with trusted business context |

A single person may have different roles in multiple businesses. Platform roles are separate from each business membership.

## 4. Self-service onboarding (first operational vertical = general retail)

### 4.1 New account

1. Register with email or phone and verify contact before privileged operations.
2. Sign in securely. A first-time user sees **Create Business** or **Join Business** (invitation).
3. Enter business name, country, legal/trading details where required, timezone, currency and preferred language.
4. Select **BusinessCategory -> BusinessType**; show only released **Supported** templates for direct activation. Unsupported types show clear "Coming later / Request support" status, not a nonfunctional dashboard.
5. Choose a compatible subscription plan or an eligible trial. Price, tax, limits, add-ons and renewal terms must be visible.
6. Provision business + Owner membership + plan snapshot + default settings + first location reliably. Provide safe retry/resume if a step fails.
7. Show a short **Get Started** checklist tailored by template:
   - Retail: location, first products/categories, optional opening stock, payments/receipt settings, first sale.
   - Salon: location/hours, services, staff/resources, first appointment.
   - Repair: location, job types, technicians, first job.
8. Enter correct Business Admin workspace. Hide empty modules from outside template; show actionable empty states without demo sales.

### 4.2 Existing user / second business

- Switcher lists only memberships the account can access, with business name, type, role and active status.
- Creating a second business requires a separate subscription/trial eligibility evaluation and creates a new business context; do not reuse an existing BusinessId.
- On switching: discard/invalidate prior-business API caches, drafts, filters and in-flight sensitive requests safely; reload entitlements, locations, language defaults and role.
- API receives an authenticated membership-scoped active business context; client-provided BusinessId is never sufficient authorization.
- Invite flows use expiring signed invitation tokens; accepted membership uses numeric database IDs; no silent cross-business account merge.

## 5. Modules and business-template catalog

### Shared platform capability families

Identity/Organizations; Locations; Users/Roles/Permissions; Customer and Supplier contacts; Catalog (Product/Service variants); Purchase/Receiving; Inventory and Stock Ledger; Sales and POS; Cash Register; Payments and Refunds; Invoicing/Reports; Booking/Resource scheduling; Job/Work Orders; Classes/Capacity; Rentals; Staff; Projects; Notifications; Localization; Subscription and Usage; Audit/Export.

### Template engine contract

A published template can eventually have a stable BusinessType, template version, required/optional modules, initial navigation, setup fields and workflow family. The template produces configuration; **it does not generate arbitrary executable code or database tables from user input**.

| Type | Workflow family | Included initial capabilities | Release status |
| --- | --- | --- | --- |
| General Retail Store | Catalog -> Purchase -> Stock -> POS -> Returns | Products, Suppliers, Stock, Sales, basic Cash/Reports | **First planned vertical** |
| Salon / Barber | Services -> Staff -> Booking -> Checkout | Booking, Resources, POS, customer history | **Second planned vertical** |
| Repair Shop | Customer -> Job -> Technician -> Parts -> Bill | Jobs, Parts, Inventory and Sale | **Third planned vertical** |
| Studios, classes, rentals, other verticals | Distinct workflow families | Future specialized modules | Discovery/Not supported until tested |

A taxonomy with 1,000+ labels can be searchable long-term, but `BusinessTypes.IsSupported` (and published template status once persisted templates exist) gates usable selection. No promise of support from a catalog label alone.

### Configuration ownership

- Platform: Module Master, capabilities, plan and quota definitions, category/type catalog, template versions, published language packs.
- Business Owner: permitted modules' operational settings, locations, role assignments, price lists, branding, preferred language and local setup.
- Staff: only operations granted by membership role and location assignment.
- No tenant owner can alter global pricing, global module executable logic or translation security strings.

## 6. Subscription, entitlement and billing

- Four proposed public tiers: Starter, Growth, Professional, Business. Enterprise is sales-led custom; Trial is a phase, not a paid plan.
- Same tier catalog can have **business-type compatibility and distinct module mapping/price versions**. Do not hardcode Salon features into retail Starter.
- Effective access checks: business active state AND subscription status/policy AND plan snapshot/modules/add-ons/overrides AND usage limits AND authenticated role AND authorized location.
- Editing a live plan does not silently alter existing paying subscribers; retain immutable effective terms/snapshot and an explicit renewal/migration policy.
- Limits: locations, active users, register terminals, storage/messages; real metering and soft warnings before enforced cap.
- Platform SaaS subscriptions and **business customer payments** have distinct flows, provider credentials, settlement/accounting and privacy boundaries.
- Signed webhooks, dedupe and reconciliation for external payment events; retries must not double-bill.
- Suspension/grace policy must preserve lawful access to subscription receipts, support and historical data/export as defined.
- See [Subscription Plans](./SUBSCRIPTION_PLANS.md) for detailed lifecycle; salon examples in that document are vertical illustrations.

## 7. Multi-language requirements

- Global language catalog + versioned JSON/CSV translation packs, validated and published via Platform Admin.
- User preferred language -> business default -> platform fallback English; locale-specific fallback to base language.
- English/Arabic RTL plus additional languages without frontend rebuild for translation-only changes.
- Local currency, timezone, taxes and legal documents are **business/market configuration**; UI language does not change these.
- Business names/product names/descriptions may require localized business content, distinct from global UI strings.
- See [Language Management](./LANGUAGE_MANAGEMENT.md).

## 8. UI/UX and applications

| Frontend workspace (initially one React app) | Goal | Required main experience |
| --- | --- | --- |
| Platform Admin | Manage platform and subscriptions | Businesses, categories/templates, modules, plans, languages, finance, usage, audit, support |
| Business Admin | Operate one selected business | Business switcher, context-specific navigation, people, catalog, operations, reports, settings |
| POS | Rapid, reliable counter sale | Search/scan, basket, pricing, payment, refund, register and receipt |
| Public booking/storefront (later) | Sell/book to clients | Brand-specific, authenticated/unauthenticated flows with consent and customer ownership |

**CBK UI reference:** Compact responsive blue/white shell, clear lists/forms/line editors, proper validation, server pagination, RTL. New UI must be original React components; do not import CBK code or branded assets into public repository. Normal tasks should not require nested "Tools" dialogs or excessive clicks.

Target first-use onboarding 5–10 minutes; actual outcome requires usability tests. Browser, touch, keyboard, scanner, printer and weak-network recovery are explicit acceptance gates.

## 9. Security and non-functional requirements

- BusinessId on every business-owned record, with parent LocationId where applicable. Enforce tenant/branch authorization at service/API and data query boundaries.
- Global Identity User IDs are numeric. Membership is `(BusinessId, UserId)`; roles can differ by business and location.
- Never trust user-supplied BusinessId to authorize access. Verify ownership of linked Product/Invoice/Customer/Location IDs.
- Reject cross-business direct object references in APIs, exports, attachments, reports, background jobs and caches.
- Money via decimal, explicit rounding/currency/tax rules; central stock ledger and no duplicate posted sales from retries.
- Least-privilege Platform Support, strong authentication/MFA for elevated roles, secure credential storage, logs without PII/payment secrets.
- Stateless APIs, pagination, workload-aware indexing, backups and actual restore drills, monitoring p50/p95/p99 and error budgets.
- **1,000 business types** is a catalog/feature coverage target; number of registered business customers and simultaneous cashiers are separate capacity metrics and require separate load tests.
- No microservices or generic EAV schema without demonstrated need. No untested claim of 10,000 concurrent cashiers.

## 10. Minimum API capability contracts (design only)

| Boundary | Candidate routes and behavior |
| --- | --- |
| Identity | `POST /api/auth/register`, login, verify, current user |
| Business | `POST /api/businesses`, `GET /api/my-businesses`, select/active business context |
| Setup | `GET /api/business-types?supported=true`, published templates, onboarding checklist |
| Locations | `GET/POST /api/business/locations`, authorized active business only |
| Membership | invite, role assignment, location assignment, revoke |
| SaaS Platform | businesses, module master, templates, plans, languages, billing/usage and audit |
| Retail | products, suppliers, purchase/receiving, stock, sales/returns, cash register |
| Translation | available locales and published bundles |

Use conventional ASP.NET route names and contracts when implementing. Concrete data transfer objects, authorization policies and error response formats are part of Phase 1 API design; do not generate a generic endpoint router to cover all industries.

## 11. Release and acceptance rules

- Phase 0 documentation must be consistent about **BusinessId**, business templates, per-business subscriptions and rollout order.
- First practical vertical must work end to end: setup -> product -> purchase/stock -> sale/payment -> return -> reconciled report, verified with actual tests.
- Browser usability is distinct from unit-test success; roles, loading, error/empty states and RTL must be tested.
- Tenant isolation, double-charge protection, financial/stock reconciliation and successful backup restore are release blockers.
- No private reference-repo assets, history, secrets or data in public `business_saas`.
- Update status only after GitHub commit and objective verification; planning is not implementation.

## 12. Open decisions (not silently assumed)

1. Initial launch market/country and operational currency/tax/invoice requirements.
2. Company-wide owner/merchant-of-record policies for SaaS subscriptions and customer POS payments.
3. Trial eligibility, duration, quotas, renewal and suspended access.
4. Per-business plan mapping/prices, terminal/device policy and real usage metering.
5. Retail inventory valuation (weighted average/FIFO), negative-stock policy, customer credit/udhar and exchange rules.
6. Data export/retention and applicable privacy rules per launch market.
7. Public booking/storefront as optional later phase and payment gateway integrations.
8. Deployment tiers and measured capacity requirements for actual concurrent businesses.
9. Supported-template onboarding behavior, data migrations and version upgrades.
10. Exact customer service/reconciliation and backup recovery objectives.

These require final business and country-specific approval before production release, even though a safe core application skeleton can be built earlier.

## 13. Decision log

- **BS-001:** Product name and repository: `business_saas`; previous `salon_saas` docs become a **Salon vertical archive/reference**, not platform architecture.
- **BS-002:** One global login, many businesses, independent BusinessId/locations/memberships/subscriptions.
- **BS-003:** Central catalog/templates only enable verified, released workflows; 1,000+ labels are an expansion goal.
- **BS-004:** .NET 10 LTS modular monolith; React/TypeScript/Bootstrap; CBK read-only UX reference.
- **BS-005:** All application entity IDs numeric SQL IDENTITY; never use GUID/UUID entity IDs.
- **BS-006:** Default first fully working vertical: General Retail; second Salon; third Repair.
- **BS-007:** White-label branding/custom domain is independent of database deployment mode. Shared SQL Server first, contract-driven dedicated SQL Server, other providers only after certification; no customer-supplied connection strings in public APIs.
- **BS-007:** All modifications restricted to `dharmendra1031/business_saas`; CBK/Kaifac remain read-only references.
