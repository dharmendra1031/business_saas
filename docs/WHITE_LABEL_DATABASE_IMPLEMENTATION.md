# White Label, Custom Domain and Dedicated Database — Implementation Plan

**Status:** Planned architecture / not implemented  
**Repository:** dharmendra1031/business_saas  
**Updated:** 2026-11-10  
**Scope:** One managed SaaS product serving shared businesses and optionally custom-branded, domain-bound, database-isolated businesses. Do not confuse a dedicated **operational** database with a fully standalone installation.

## 1. Product decisions

1. **White label**, **custom domain**, **dedicated database**, and **customer-hosted database** are separate configurable commercial capabilities, not a single bundled feature.
2. A **Business** (numeric BusinessId) is the tenancy and authorization boundary. One branded custom domain maps to one Business in the initial implementation; reseller/multi-business white label is out of scope.
3. Maintain **one ASP.NET Core API, one React application, one shared codebase**, and the existing numeric auto-increment application IDs.
4. The first supported database engine is **SQL Server**, in both shared and managed-dedicated modes. PostgreSQL, MySQL/MariaDB, and Oracle are separate certifications, not automatic EF Core capabilities.
5. The managed SaaS retains global Identity and membership/subscription in a **central control database**. A dedicated business database contains its operational data, not independent global user accounts. Full independent installation requires a separately planned deployment and identity/license boundary.
6. Normal business owners may **request** a domain or dedicated database; only authorized Platform Admin/provisioning operations can activate a destination or approve database credentials.
7. Country, currency, timezone and tax-market readiness are separate from database choice and must not be implied by availability of a country in the onboarding list.

## 2. Current code and refactor boundary

**Current truth (10 November 2026):** AppDbContext contains Identity users, BusinessTypes, Businesses, BusinessUsers, BusinessLocations and BusinessSubscriptions, using a single SQL Server connection. One shared operational shell exists; there is no white-label domain routing, per-business operational DbContext, provisioning, customer DB connectivity, or second SQL provider. Do not claim the features are live.

**Control plane, authoritative in the original database:**
- Identity users, user security/session management
- Businesses, BusinessTypes, BusinessUsers and role/membership authorization
- BusinessSubscriptions, plans/entitlements and eventual provisioning status
- Approved custom domains, branding, approved data-store assignments and audit history
- Existing BusinessLocations stay centrally registered initially to avoid an unrelated migration. Location authorization must check BusinessId and must not rely on foreign keys across databases.

**Operational plane, through a dedicated EF Core DbContext inside the SAME API project when the first Retail masters are implemented:**
- ProductCategories, Units, Products, Suppliers, Customers
- Stock, purchase, sales, returns, payment/ledger and cash-register business records
- Later: salon Services, Staff, Resources, Appointments, Checkout
- All tenant-owned records include BusinessId even if stored in an isolated database. Local row Id is numeric auto-increment. If IDs are referenced outside a database, the reference must include BusinessId + local Id.
- Never require operational tables to have SQL foreign keys into central Identity/Businesses/BusinessLocations; validate centrally before executing local transactions.

**Implementation constraint:** Do not put Retail masters into the current combined AppDbContext and then attempt to split hundreds of tables later. Introduce an operational context with the first real business master slice, pointed to the existing shared SQL Server database at first. Keep central transactions and operational transactions explicitly separate. Configure distinct EF Core migration histories for the control and operational contexts if they initially share one physical database; review table mappings and schema ownership to avoid deployment collisions.

## 3. Smallest necessary metadata (create only when the feature ships)

**BusinessDataStores**
- Id: bigint identity primary key
- BusinessId: bigint unique foreign key to Businesses
- Mode: Shared / Dedicated
- Provider: SQLServer initially
- SecretReference: opaque server-side secret/configuration lookup key (never a connection string/password in database)
- State: Provisioning / Ready / Migrating / Suspended / Failed
- MappingVersion: concurrency control or rowversion
- CreatedAtUtc, UpdatedAtUtc

The same shared database may be referenced by many businesses. Any routing entry must be explicitly approved and in Ready state before normal operations. Schema version is checked against operational EF migration history; add a stored version only if operational rollout needs it.

**BusinessDomains** (when custom-domain delivery ships)
- Id: bigint identity primary key
- BusinessId: bigint foreign key
- HostName: unique normalized DNS host
- Status: PendingVerification / PendingCertificate / Active / Disabled
- VerifiedAtUtc, UpdatedAtUtc

Use existing Businesses columns for a simple brand name, logo reference and selected theme as needed; do not introduce a generic theming/template engine or duplicate branding table until necessary. Limit to one active customer domain per Business in first release.

## 4. Request, identity and database selection

1. A trusted reverse proxy accepts only configured/approved domains, terminates TLS, and forwards request scheme/host through properly configured trusted proxy headers.
2. Normalize Host and resolve an **active, verified** BusinessDomains entry if using a customer domain; unknown/unapproved hosts fail closed. DNS ownership verification alone does not authorize business operations.
3. Sign in against **central Identity**, using the app and /api on the same origin for each custom domain. Auth cookies are host-only, HttpOnly, Secure, and CSRF protected. Cookies from unrelated domains are not shared, so logging into one customer domain does **not** imply SSO on other domains. SSO would be a separate future project.
4. For an operational request, authenticate UserId; authorize membership/role/location/subscription for the requested BusinessId in the control plane. On a custom domain, BusinessId MUST equal the domain's configured BusinessId. On the platform domain, business selection still requires membership.
5. Resolve the approved Ready BusinessDataStores assignment from the central DB, then select the operational DbContext connection server-side. Do not read provider, server, database, tenant ID, or connection string from an untrusted header, cookie, form or URL as a routing authority.
6. Query or modify **only** the selected operational database, retaining BusinessId filters and tenant-safe indexes/unique constraints. Never silently fall back from a failed dedicated connection to the shared DB.
7. When an operational database is down or in migration mode, return a controlled unavailable/maintenance response; do not leak SQL server names or secrets.

**Security:** Host-header injection, forwarded-header spoofing, IDN/punycode normalization, DNS/domain takeover, open redirects, origin/CORS handling, password-reset URL construction, revoked membership, file downloads and background jobs are included in the threat-model tests. Log BusinessId, correlation and assignment version without logging secrets/PII. Persist and protect ASP.NET Core Data Protection keys for multi-instance hosting.

## 5. Customer-managed domain activation

1. Owner requests a hostname, e.g. app.customer-domain.example; backend records PendingVerification.
2. Display a unique short-lived DNS TXT verification instruction. Verify exact domain ownership server-side, prevent duplicate claim, and require re-verification when ownership/pointing changes.
3. Owner configures approved CNAME/A record as instructed. Platform checks DNS routing, configures certificate issuance/renewal and HTTPS.
4. Only after domain ownership, routing and TLS pass, mark Active. Never permit an arbitrary Host header to activate tenant routing.
5. On DNS mismatch, expired certificate, removed/disabled assignment or ownership dispute: disable custom host serving, keep a safe platform-admin recovery path, and alert operations.
6. Reverse proxy must serve the React assets and /api from that same origin. Brand metadata and URLs must be tenant-scoped; cache by validated host/BusinessId, not by generic paths.

## 6. Managed dedicated SQL Server provisioning

1. Admin authorizes the upgrade; control database creates a durable Provisioning assignment with idempotency.
2. A trusted provisioning command creates a database named from an internal numeric ID (not customer text), installs a restricted application login, and stores secret material in an approved secret store. Do not use runtime credentials for CREATE DATABASE/DDL.
3. Apply the **reviewed operational SQL Server migrations** to that database as a controlled release action, not on every request or normal API startup.
4. Initialize only the business's own required operational data, check schema health/connectivity, and mark Ready. Registration/member/subscription remain central.
5. On failure, leave the business Provisioning/Failed, record a sanitized reason, and make retry idempotent; never expose partially created data to customers.
6. A business may have a custom domain without a dedicated DB, or a dedicated DB without a custom domain. Those lifecycles are independent.

**No distributed transaction** spans central control tables and dedicated operational DBs. Use persisted states, idempotency and reconciliation instead. Keep each invoice, stock posting and payment ledger transaction entirely inside one selected operational DB.

## 7. Moving a live business from shared to dedicated

1. Request approval, preflight free space/permissions/migrations, take a verified backup and document rollback window.
2. Put only the migrating Business in write-maintenance mode and drain in-flight mutations. Block API, background jobs, integration retries and late payment callbacks from posting to the old location.
3. Export/copy only BusinessId-scoped operational rows to a fresh dedicated DB, retaining original numeric keys and reseeding identity counters; migrate tenant files/attachments if applicable.
4. Reconcile **row counts, ownership, PK/FK references, balances, stock ledger totals, invoices, payments, returns, and document numbering**; test independent RESTORE as well.
5. Atomically switch the **central mapping** using an expected version, invalidate scoped caches/sessions where necessary, run authenticated smoke tests, and then resume writes.
6. If validation fails **before** reopening writes, restore the original mapping and safely resume old DB operations. Once new writes occur, a simple pointer reversal is unsafe: use controlled forward repair or an explicitly reconciled reverse migration.

Never implement an unverified dual-write mechanism or copy whole shared-database tables containing other businesses.

## 8. Customer-hosted databases and multiple providers

- **First enterprise BYO mode:** approved customer-hosted SQL Server only after verified private/restricted connectivity, TLS, egress/firewall allowlisting, minimum supported SQL version, backup ownership, permissions, maintenance window, schema upgrades and signed support boundaries.
- A database on an arbitrary public IP with a connection string supplied during self-service sign-up is **not supported**.
- For PostgreSQL or MySQL/MariaDB, maintain and test **separate provider-specific migrations**, numeric identity mapping, decimal/time arithmetic, concurrency, transaction isolation, money/stock reconciliation, data export/import and reporting compatibility. A provider appears in the UI only after those tests and documented support coverage pass.
- Oracle is a separate enterprise decision, not in the initial implementation.
- Full customer-controlled installation (web, API, authentication, DB, backups, license/update mechanism) is a different contract and release track from hosted dedicated operational DB.

## 9. Operation and growth safeguards

- Each managed dedicated database needs an encrypted scheduled backup, a tested restore, retention policy, monitoring, migration log and documented customer-level recovery objectives (RPO/RTO agreed in contract).
- Release migrations in controlled batches per provider and destination. Back up before destructive changes; prefer additive/expand-contract migrations. Do not run uncontrolled tenant-wide migrations at API startup.
- Use separate least-privilege app credentials and provisioning/migration credentials; support credential rotation and tenant offboarding/export/deletion requirements.
- Database-per-business carries DB count, connection-pool, storage, patching, backup and support costs. Monitor active connections and resource limits; dedicated capacity is an Enterprise-priced feature, not a free default for thousands of businesses.
- Central platform outage can affect authentication and routing even if dedicated operational DBs are healthy. Plan central high availability and recovery accordingly.
- A dedicated SQL database is **logical isolation**, not necessarily separate hardware, server, legal region or complete independence. Describe purchased isolation accurately.
- Subscription expiry must not delete data; published grace/read-only/export policies apply equally to shared and dedicated tenants.

## 10. Small implementation phases and release gates

| Stage | Minimal change | Mandatory acceptance gate |
| --- | --- | --- |
| A — Data boundary | Keep AppDbContext for control; introduce operational context with first Retail masters, initially same shared SQL Server | Cross-business CRUD isolation, numeric PKs, correct permissions, migration tested |
| B — Custom domain | Verified BusinessDomains + tiny brand settings, trusted host/proxy/TLS and same-origin auth | Host spoofing, invalid DNS, expired cert, wrong business, cookie isolation tested |
| C — Dedicated SQL Server | BusinessDataStores mapping, server-side resolver, provisioning/secret handling | Shared vs dedicated cross-access denial, failed connection no fallback, repeatable migrations, restore passes |
| D — Upgrade existing customer | Controlled shared-to-dedicated migration and reconciliation | Stock/money counts, preserved IDs, freeze, mapping switch and rollback drills |
| E — BYO SQL Server | Enterprise customer-hosted connection policy, validation and runbook | Network security, credential rotation, backup roles and failure simulation |
| F — Other SQL providers | Explicit provider-specific implementation when commercially justified | Full tests for migrations, authorization, decimals, concurrency, stock and payments |

**Pilot before selling:** Run at least three real workflow scenarios in staging: (1) two shared businesses, (2) a white-label custom domain using shared storage, and (3) a branded dedicated tenant that cannot access the shared businesses. Also verify upgrade of an existing business, restoration of a dedicated backup, and safe handling of a missing connection.

**Not permitted to claim production-ready until:** domain verification/certificate renewal, secured host routing, central auth, tenant negative tests, controlled migration, secret handling, operational backups/restores and browser tests pass on representative deployed infrastructure.

For a fully disconnected customer-owned installation, see the separate [Offline / On-Premise Deployment Plan](./OFFLINE_ON_PREMISE_DEPLOYMENT.md). It carries its own **local Identity and control plane**, not a call to the hosted SaaS control database.

## References

- [Database Architecture](./DATABASE_ARCHITECTURE.md)
- [Engineering Guardrails](./ENGINEERING_GUARDRAILS.md)
- [Development Roadmap](./DEVELOPMENT_ROADMAP.md)
- [EF Core multiple providers](https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/providers)
- [ASP.NET Core reverse proxy security](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/proxy-load-balancer?view=aspnetcore-10.0)
- [ASP.NET Core cookie sharing boundaries](https://learn.microsoft.com/en-us/aspnet/core/security/cookie-sharing)
