# P1 — Foundation Implementation

**Status:** P1 development slice with EF Core migration and SQL Server CI verification. Build and tests PASS; production launch is blocked until identity and commercial integrations are complete.  
**Repository:** `dharmendra1031/business_saas`  
**Source of truth:** [Master SRS](./BUSINESS_SAAS_MASTER_SRS.md) · [Database Architecture](./DATABASE_ARCHITECTURE.md) · [Development Roadmap](./DEVELOPMENT_ROADMAP.md)

## Implemented in this slice

- One ASP.NET Core .NET 10 Web API: Identity user accounts with **numeric long IDs**, built-in password hashing/lockout, secure HttpOnly same-site cookies and antiforgery protection on mutating API requests.
- EF Core SQL Server DbContext and checked-in `InitialCreate` migration with numeric identity users/businesses, one supported General Retail type, BusinessUsers membership and initial BusinessLocation.
- Auth endpoints: `GET /api/auth/csrf`, `POST /api/auth/register`, `POST /api/auth/login`, `GET /api/auth/me`, `POST /api/auth/logout`.
- Business endpoints: `GET /api/business-types`, `GET/POST /api/businesses`, `GET /api/businesses/{businessId}/dashboard`.
- Create Business uses one transaction to save business, Owner membership and first shop; a client creation token handles idempotency, and SQL Server unique-key races return the same result or a conflict for mismatched payloads.
- Business list/dashboard queries are restricted by logged-in **UserId + BusinessId membership**; user-supplied BusinessId is not enough.
- One React + TypeScript + Vite + Bootstrap web app with original blue/white enterprise layout inspired by **read-only CBK design principles**. Login, registration, list/switch business, creation and Retail Dashboard are implemented.
- Real business details/locations only; no demo products, sales, stock or fabricated KPI counters.
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

**Verified CI:** [SQL Server migration, backend and frontend success](https://github.com/dharmendra1031/business_saas/actions/runs/42596131153) — latest checked 9 November 2026.

## Technical boundaries and open launch blockers

- **Migration:** `InitialCreate` is committed and has been applied successfully on an ephemeral SQL Server 2022 CI database. Production migration deployment, restore and rollback still require operational approval.
- **Identity:** Account email verification, password recovery, MFA, abuse prevention and transactional email provider are not wired yet. **Non-development registration returns 503** and production sign-in requires confirmed email: public signup must not be enabled until verified email delivery is implemented and tested.
- **Subscription:** The full business plan/trial and module entitlement lifecycle is planned but not yet implemented. Only supported Retail business type is available in this P1 slice.
- **Retail:** Product, Purchase, Receiving, Stock, POS, Returns, register and reports are **not implemented**; Retail Dashboard is a real tenant-scoped workspace shell.
- **Business management:** Invites, staff account access, configurable roles and extra branches are planned later.
- **International:** Country, currency and IANA timezone fields are collected, but market-specific tax, money and language management require separate implementation.
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
