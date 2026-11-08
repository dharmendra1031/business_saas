# P1 — Foundation Implementation

**Status:** Initial development slice pushed to `main`. Build and integration test status must be checked in GitHub Actions; no production rollout.  
**Repository:** `dharmendra1031/business_saas`  
**Source of truth:** [Master SRS](./BUSINESS_SAAS_MASTER_SRS.md) · [Database Architecture](./DATABASE_ARCHITECTURE.md) · [Development Roadmap](./DEVELOPMENT_ROADMAP.md)

## Implemented in this slice

- One ASP.NET Core .NET 10 Web API: Identity user accounts with **numeric long IDs**, built-in password hashing/lockout, secure HttpOnly same-site cookies and antiforgery protection on mutating API requests.
- EF Core SQL Server DbContext with numeric identity businesses, one supported General Retail type, BusinessUsers membership and initial BusinessLocation.
- Auth endpoints: `GET /api/auth/csrf`, `POST /api/auth/register`, `POST /api/auth/login`, `GET /api/auth/me`, `POST /api/auth/logout`.
- Business endpoints: `GET /api/business-types`, `GET/POST /api/businesses`, `GET /api/businesses/{businessId}/dashboard`.
- Create Business uses one transaction to save business, Owner membership and first shop; a client creation token is saved for retry/idempotency.
- Business list/dashboard queries are restricted by logged-in **UserId + BusinessId membership**; user-supplied BusinessId is not enough.
- One React + TypeScript + Vite + Bootstrap web app with original blue/white enterprise layout inspired by **read-only CBK design principles**. Login, registration, list/switch business, creation and Retail Dashboard are implemented.
- Real business details/locations only; no demo products, sales, stock or fabricated KPI counters.
- xUnit integration tests for two separate tenants and CSRF rejection, using SQLite in-memory. They test logical authorization, **not SQL Server-specific migration/concurrency**.
- One GitHub Actions workflow for .NET backend tests and React typecheck/build.

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

For a different SQL Server, set **`ConnectionStrings__Default`** in your local environment, not in Git. `Development` performs `EnsureCreated` **only on a disposable development database**. This does not apply incremental schema changes, does not create EF migrations and must **not** be used to bootstrap production.

## Technical boundaries and open launch blockers

- **Development only:** No EF migration baseline yet; production requires reviewed EF migrations and controlled migration deployment.
- **Identity:** Account email verification, password recovery, MFA, abuse prevention and transactional email provider are not wired yet. Never expose this registration flow publicly until these are completed/tested.
- **Subscription:** The full business plan/trial and module entitlement lifecycle is planned but not yet implemented. Only supported Retail business type is available in this P1 slice.
- **Retail:** Product, Purchase, Receiving, Stock, POS, Returns, register and reports are **not implemented**; Retail Dashboard is a real tenant-scoped workspace shell.
- **Business management:** Invites, staff account access, configurable roles and extra branches are planned later.
- **International:** Country, currency and IANA timezone fields are collected, but market-specific tax, money and language management require separate implementation.
- **Concurrency:** Unique indexes and retry keys exist; concurrent same-key creation collisions need a SQL Server-specific integration test and safe conflict recovery before production.
- **Operations:** Data Protection keys must be persisted for multiple servers; HTTPS/forwarded proxy config, signing/production cookie policy, secrets, audit, backups and SQL Server transaction/concurrency tests must be completed before pilot.
- **UX:** Visual review, responsive/browser and RTL user testing not yet completed. React does not claim a published multi-language translation system.
- **No private source:** CBK/Kaifac are private and remain read-only; no source or assets copied into this public repository.

## Exit criteria for the full P1 milestone

- .NET build and React build pass with recorded CI results.
- Two-user/two-business tenant negative tests and unauthorized operations pass.
- Verified signup/email flow, session security, safe idempotent business creation and switching.
- Approved EF migration checked into source and validated on fresh SQL Server.
- End-to-end browser smoke test for Login -> Create Business -> Retail Dashboard.
- Visual accessibility/mobile review, installation notes and no hardcoded credentials or demo sales.

**A committed file is not proof of a passing test or production readiness.**
