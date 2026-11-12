# P1 — Foundation Implementation

**Status:** P1 development slice with EF Core migration and SQL Server CI verification. Build and tests PASS; production launch is blocked until identity and commercial integrations are complete.  
**Repository:** `dharmendra1031/business_saas`  
**Source of truth:** [Master SRS](./BUSINESS_SAAS_MASTER_SRS.md) · [Database Architecture](./DATABASE_ARCHITECTURE.md) · [Development Roadmap](./DEVELOPMENT_ROADMAP.md)

## Implemented in this slice

- One ASP.NET Core .NET 10 Web API: Identity user accounts with **numeric long IDs**, built-in password hashing/lockout, secure HttpOnly same-site cookies and antiforgery protection on mutating API requests.
- EF Core SQL Server DbContext and checked-in `InitialCreate` and `AddBusinessSubscription` migrations with numeric identity users/businesses/subscription IDs, one supported General Retail type, BusinessUsers membership and initial BusinessLocation.
- Auth endpoints: `GET /api/auth/csrf`, `POST /api/auth/register`, `POST /api/auth/login`, `GET /api/auth/me`, `POST /api/auth/logout`.
- Business endpoints: `GET /api/business-types`, `GET/POST /api/businesses`, `GET /api/businesses/{businessId}/dashboard`.
- Create Business uses one transaction to save business, Owner membership and first shop; a client creation token handles idempotency, and SQL Server unique-key races return the same result or a conflict for mismatched payloads.
- Business list/dashboard queries are restricted by logged-in **UserId + BusinessId membership**; user-supplied BusinessId is not enough. New businesses receive a configurable 14-day Starter trial record, and dashboard displays trial/expiry eligibility; transaction-level enforcement is still pending.
- One React + TypeScript + Vite + Bootstrap web app with original blue/white enterprise layout inspired by **read-only CBK design principles**. Login, registration, list/switch business, creation and Retail Dashboard are implemented.
- Real business details/locations only; no demo products, sales, stock or fabricated KPI counters. Country selection uses a native searchable datalist backed by 249 ISO country/territory entries; the API validates country, currency and selected IANA timezone against the catalog. Multi-currency and multi-timezone countries expose appropriate choices, with no extra UI dependency or country database table.
- xUnit tests cover two separate tenant users, multiple owned businesses, CSRF rejection and production signup blocking. A separate CI job boots SQL Server 2022 and verifies the actual EF migration, concurrent same-key creation, single membership/location and cross-business access denial.
- One GitHub Actions workflow for .NET backend tests, SQL Server 2022 integration test and React typecheck/build. Documentation-only changes do not trigger this resource-intensive workflow.

## Local development — Windows

**Requirements:** .NET 10 SDK, Node 22, SQL Server 2022/LocalDB, Git.

1. Clone `https://github.com/dharmendra1031/business_saas.git`.
2. For development SQL Server LocalDB, the API uses `backend/BusinessSaas.Api/appsettings.Development.json`. This connection is sample **local-only**, not a production connection.
3. Run API:
   ```powershell
   dotnet run --project backend/BusinessSaas.Api/BusinessSaas.Api.csproj
   ```
4. In a second terminal, run React:
   ```powershell
   cd frontend/web
   npm install
   npm run dev
   ```
5. Open the local URL displayed by Vite (typically `http://127.0.0.1:5173`). The Vite proxy sends `/api` to `http://localhost:5080`.
6. Register, sign in, create a General Retail business and enter its workspace. Test a second account: it must not open the first account's dashboard.

For a different SQL Server, set **`ConnectionStrings__Default`** in your environment, not in Git. Development with SQL Server/LocalDB now runs **EF Core `MigrateAsync`**, applying only the checked-in migrations. It does not drop databases. If you used the old `EnsureCreated` development database, first **back it up**, then switch to a new disposable development database or migrate its data with a reviewed plan; do not blindly run the baseline migration against an `EnsureCreated` schema.

For a controlled non-development deployment, set `ConnectionStrings__Default` for the migration process and run the reviewed migration explicitly (after a database backup):

```powershell
dotnet tool install --global dotnet-ef --version 10.0.14
dotnet ef database update --project backend/BusinessSaas.Api/BusinessSaas.Api.csproj --startup-project backend/BusinessSaas.Api/BusinessSaas.Api.csproj
```

The design-time factory reads `ConnectionStrings__Default`, falling back only to the local development database. Confirm the target database before applying a migration. Production startup does **not** apply migrations automatically.

**Verified CI:** [Batch 04 staff access backend 7/7, SQL Server 1/1, React PASS](https://github.com/dharmendra1031/business_saas/actions/runs/42989423781) — 11 November 2026 (code commit `a37a728`). A subsequent UI-copy-only commit must also pass build before its status is asserted. See [Batch 04](./BATCH_04_REVIEW.md) for unresolved browser/role review.

## Protected key-ring deployment (optional until production release)

For IIS/Windows or multi-instance hosting, persist ASP.NET Core Data Protection keys in a shared, access-controlled folder and encrypt them with a private-key-enabled PFX certificate. Keep its password in protected environment configuration, never in `appsettings.json` or Git. Use the **same application name, key directory and certificate** across app nodes; back up the key ring and certificate securely before upgrades.

```powershell
# On the deployment host; create ACLs allowing only the IIS App Pool identity.
$env:ConnectionStrings__Default = "<approved SQL Server connection string>"
$env:DataProtection__KeyDirectory = "C:\BusinessSaasData\Keys"
$env:DataProtection__CertificatePath = "C:\BusinessSaasSecrets\protection.pfx"
$env:DataProtection__CertificatePassword = "<read from an approved secret store>"
```

These are **configuration examples only**, not committed credentials or proof that the IIS installation was tested. Do not create a public certificate/key folder under `wwwroot`. A partially supplied key-ring configuration fails startup instead of persisting unencrypted keys. A missing configuration retains framework defaults for development, but **is not production-ready**. Windows IIS ACL, certificate rotation, restart, multi-instance cookie/CSRF, backup/restore and real UI login checks are still mandatory.

All API responses now include `X-Request-Id` for support correlation; standard production Problem Details expose `requestId` without an exception stack trace. Sensitive error text belongs in server logs only.

## Technical boundaries and open launch blockers

- **Migration:** `InitialCreate` is committed and has been applied successfully on an ephemeral SQL Server 2022 CI database. Production migration deployment, restore and rollback still require operational approval.
- **Identity:** Account email verification, password recovery, MFA, account/IP abuse prevention and transactional email provider are not wired yet. Existing Identity account lockout is covered by a five-attempt test. Frontend session-expired handling and user-ID-only sign-in/out logging are added, but actual browser/production review is pending. **Non-development registration returns 503** and production sign-in requires confirmed email: public signup must not be enabled until verified email delivery is implemented and tested.
- **Subscription:** A single Starter trial per new business, expiry status and database migration are implemented. Paid billing, subscription renewal, plan-specific module entitlement and transaction-level server-side enforcement are not yet implemented. Only General Retail is supported as a business type.
- **Retail:** Product, Purchase, Receiving, Stock, POS, Returns, register and reports are **not implemented**; Retail Dashboard is a real tenant-scoped workspace shell.
- **Platform Admin:** `businesses.view` is a separately assigned platform permission, independent of business Owner and `licenses.manage`. Authorized admins have a **read-only business directory and detail screen** with search, server-side paging, owner, plan and trial status. No privilege editing, billing changes or trials adjustments are exposed. Actual admin provisioning, audit history and browser usability testing are pending.
- **Business management:** A first owner-only Team Access screen and APIs can grant/revoke **Manager or Cashier** membership for an **existing registered account**. Owner membership cannot be removed through this flow; role checks run from the database. Staff dashboard currently shows only the primary location. Email invitations, changing roles, configurable location assignment, operational-role permissions and extra-branch management are **not yet implemented**.
- **International:** All 249 ISO countries/territories are selectable using a native searchable dropdown; currency and timezone use a static validated catalog (two remote uninhabited territories default to UTC). Market-specific tax, invoice rules, local payment availability, data rules and published languages remain separate work. Country selection does not mean full country-specific production support.
- **Concurrency:** SQL Server test verified that concurrent same-key creates produce one business, one membership and one location; a different payload using that key returns 409. More fault-injection and real deployment testing remain pending.
- **Operations:** Data Protection keys must be persisted for multiple servers; HTTPS/forwarded proxy config, secrets, audit, backups/restore, and production load testing must be completed before pilot. SQL Server transaction/concurrency CI tests have started but do not cover these operational gates.
- **UX:** Visual review, responsive/browser and RTL user testing not yet completed. React does not claim a published multi-language translation system.
- **No private source:** CBK/Kaifac are private and remain read-only; no source or assets copied into this public repository.

## Exit criteria for the full P1 milestone

- .NET build and React build pass with recorded CI results.
- Two-user/two-business tenant negative tests and unauthorized operations pass.
- Verified signup/email flow, session security, safe idempotent business creation and switching.
- Approved EF migration checked into source and validated on fresh SQL Server. **DONE for disposable CI; production database rollout not performed.**
- End-to-end browser smoke test for Login -> Create Business -> Retail Dashboard.
- Visual accessibility/mobile review, installation notes and no hardcoded credentials or demo sales.

**A committed file is not proof of a passing test or production readiness.**
