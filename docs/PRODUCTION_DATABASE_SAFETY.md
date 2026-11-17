# Production database safety

**Status:** Operator checklist only. A real production backup, restore and migration rehearsal has **not** been performed.

Use a private SQL Server staging environment and approved backup destination. Do not commit database backups, connection strings, passwords or certificates to Git.

## Before a release

1. Confirm the target branch/commit, database name, migration plan and a rollback decision-maker.
2. Stop business writes for schema changes that require downtime. Test the same migration script on a **restored staging copy** first.
3. Create a full backup and verify it. Store a protected copy off the application VPS.
4. Generate and review the idempotent EF script from the repository root:

   ```powershell
   dotnet ef migrations script --idempotent --project backend/BusinessSaas.Api --startup-project backend/BusinessSaas.Api --output retail-migrations.sql
   ```

   Review any destructive statements before applying the script with an authorized SQL operator. Do not enable automatic production migration or edit an already-applied migration.

5. Apply the approved script to the intended database, verify schema and tenant/business counts, then deploy the matching API/web build. Check `/api/ready` and run scoped smoke tests before accepting live traffic.

## Backup and restore rehearsal (SQL Server)

Illustrative T-SQL; replace database, file path and logical filenames with **verified actual values**. The SQL Server service account needs access to these paths.

```sql
BACKUP DATABASE [BusinessSaasProd]
TO DISK = N'D:\SqlBackups\BusinessSaasProd_YYYYMMDD.bak'
WITH INIT, CHECKSUM, STATS = 5;

RESTORE VERIFYONLY
FROM DISK = N'D:\SqlBackups\BusinessSaasProd_YYYYMMDD.bak'
WITH CHECKSUM;

RESTORE FILELISTONLY
FROM DISK = N'D:\SqlBackups\BusinessSaasProd_YYYYMMDD.bak';
```

After inspecting the logical file names, restore to a **different staging database**, never over the production database:

```sql
RESTORE DATABASE [BusinessSaasRestoreTest]
FROM DISK = N'D:\SqlBackups\BusinessSaasProd_YYYYMMDD.bak'
WITH MOVE N'<actual-data-logical-name>' TO N'D:\SqlData\BusinessSaasRestoreTest.mdf',
     MOVE N'<actual-log-logical-name>' TO N'D:\SqlData\BusinessSaasRestoreTest_log.ldf',
     CHECKSUM, RECOVERY;

DBCC CHECKDB([BusinessSaasRestoreTest]) WITH NO_INFOMSGS;
```

Test access with a restricted staging account; compare business/member counts and stock balances to the source snapshot. Record the recovery time, data-loss window and evidence of a successful restore. `RESTORE VERIFYONLY` alone is **not** a restore test.

## Operational requirements before go-live

- Assign a scheduled backup owner and define recovery-point/recovery-time objectives (RPO/RTO). A daily full backup alone can lose up to a day of transactions.
- Encrypt and access-control backup storage, keep off-host copies and periodically rehearse restoration.
- Configure HTTPS, protected secrets and a persistent encrypted ASP.NET Core Data Protection key ring.
- Login is limited to 30 attempts/minute and development registration to 10 attempts/minute per server-observed remote IP. Limits are **per app instance**, not shared across servers. Behind a reverse proxy, configure an explicit trusted-proxy/forwarded-header policy before using client IPs; never trust arbitrary `X-Forwarded-For` request headers. Shared office/NAT IPs may also share a quota.
- Monitor `/api/health` (process alive) separately from `/api/ready` (DB reachable; migrations applied).
- Confirm email verification, authorization, concurrent write tests, deployment rollback and production monitoring before allowing actual merchants.

The repository CI proves database migrations/integration against disposable SQL Server, **not** a production backup or restore.
