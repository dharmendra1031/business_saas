# Windows local setup (development)

This guide starts the existing API and web app against a new **disposable test database**. Use .NET 10 SDK, Node.js 22 or newer, and SQL Server LocalDB. Run commands from the repository root in PowerShell unless a step says otherwise.

1. Start LocalDB and check its state:

   ```powershell
   sqllocaldb start MSSQLLocalDB
   sqllocaldb info MSSQLLocalDB
   ```

2. In the first terminal, choose a new database name and run the API:

   ```powershell
   $env:ASPNETCORE_ENVIRONMENT = 'Development'
   $env:ConnectionStrings__Default = 'Server=(localdb)\MSSQLLocalDB;Database=BusinessSaasTest;Trusted_Connection=True;Encrypt=False'
   dotnet run --project backend/BusinessSaas.Api/BusinessSaas.Api.csproj
   ```

   Development startup creates the named database if absent and applies the checked-in EF migrations. Wait for `Now listening on: http://localhost:5080`. Do not point this command at an existing production database or an old database created with `EnsureCreated`.

3. In a second terminal, start the web app:

   ```powershell
   cd frontend/web
   npm ci
   npm run dev
   ```

   Open `http://127.0.0.1:5173/`. Vite forwards `/api` to the API on port 5080.

4. Verify both processes:

   ```powershell
   (Invoke-WebRequest http://localhost:5080/api/health -UseBasicParsing).StatusCode
   (Invoke-WebRequest http://localhost:5080/api/ready -UseBasicParsing).StatusCode
   (Invoke-WebRequest http://127.0.0.1:5173/api/health -UseBasicParsing).StatusCode
   ```

   All three should return `200`. `/api/health` checks the running API; `/api/ready` also requires a reachable database and, for SQL Server, no pending EF migrations. Register a development account, sign in, create a General Retail business, and open its dashboard. Local development has no seeded administrator password. To configure the single Platform Super Admin account, follow [Platform Owner Setup](PLATFORM_OWNER_SETUP.md). A customer cannot promote themselves.

5. Stop each server with `Ctrl+C`. The LocalDB database persists for the next run. For another clean test, use a **different new database name** in the connection string. Never delete an existing database just to repeat this walkthrough.

## Build and tests while the API is running

The running Debug executable can lock the API output file. Stop the API before a normal Debug test run, or use a separate Release build:

```powershell
dotnet test backend/BusinessSaas.Tests/BusinessSaas.Tests.csproj -c Release
cd frontend/web
npm run build
```

For the SQL Server integration test, choose a separate new disposable database name:

```powershell
$env:SQLSERVER_TEST_CONNECTION = 'Server=(localdb)\MSSQLLocalDB;Database=BusinessSaasIntegration;Trusted_Connection=True;Encrypt=False'
dotnet test backend/BusinessSaas.Tests/BusinessSaas.Tests.csproj -c Release --filter 'Database=SqlServer'
```

Do not use the database containing manual test data for this test. A passing test run does not prove browser usability or production deployment.

## Deployment boundary

For a non-development installation, set the SQL connection string through protected environment configuration and apply reviewed migrations explicitly after a backup. Configure HTTPS, data-protection key storage and certificate permissions, verified email delivery, and the remaining release gates in [P1 implementation](P1_IMPLEMENTATION.md). The development registration endpoint is intentionally unavailable outside Development. This local runbook is not a production install procedure.
