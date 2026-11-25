# API performance smoke baseline

**Status:** Script and measurement method available. No production throughput or scale claim has been measured.

From the repository root with an API already running against an isolated staging database:

```powershell
node scripts/api-load-smoke.mjs http://127.0.0.1:5080/api/ready 50 5
```

The script makes **read-only** calls to `/api/health` or `/api/ready` only (50 requests, 5 concurrent workers by default; capped at 500/20). It reports failure count, p50/p95 response time and throughput. It fails if responses are not successful.

`/api/health` measures process/network overhead; `/api/ready` also exercises database connectivity/migration checks. **Neither represents POS checkout, product search, stock posting, or 1,000 concurrent shops.**

## Record a comparable baseline

1. Use the same API commit, staging SQL Server configuration, machine, dataset and network for each run.
2. Record commit SHA, date, endpoint, request/concurrency counts, failures, p50/p95 and host CPU/SQL metrics.
3. Repeat the same test after important changes; inspect regressions rather than enforcing arbitrary latency limits.
4. Later add realistic authenticated Retail flows with approved **synthetic** data and safe isolation. Do not benchmark writes against merchant data.
5. Get explicit operational approval before pointing a load test at production. This script has no built-in test-data creation or transaction replay.

A passing CI build or a localhost smoke test is not a capacity certification.

## Authenticated Retail read baseline

The authenticated script signs into a **disposable staging account** once, then runs bounded read-only requests against either a business Dashboard or paged Product List:

```powershell
$env:BS_SMOKE_EMAIL = 'staging-owner@example.test'
$env:BS_SMOKE_PASSWORD = '<staging-test-password>'
$env:BS_SMOKE_BUSINESS_ID = '7' # Replace with the numeric ID of a disposable Retail business
node scripts/auth-read-smoke.mjs http://127.0.0.1:5080 30 3 dashboard
node scripts/auth-read-smoke.mjs http://127.0.0.1:5080 30 3 products
```

The script refuses remote targets by default. For a **controlled HTTPS staging endpoint only**, explicitly set `BS_SMOKE_REMOTE_STAGING=yes`. Do not run it against production or with a merchant account. Credentials come only from environment variables; they are not printed or stored. Clear the environment variables after testing.

The tool obtains an antiforgery token, signs in once, and sends `GET` requests only for the measured endpoint. It measures p50/p95, failures and observed throughput, with hard limits of **200 requests and 10 concurrent workers**. It performs no database writes beyond normal login bookkeeping (such as login activity or lockout reset). The API and test business must already be provisioned.

The CI test uses a local fake HTTP server to verify cookie and CSRF handling, read-only access and input safety. It does **not** measure real database speed or production capacity.

## Retail index review — 25 November 2026

Migration `20261125170000_RetailReadIndexes` adds three narrow, non-unique
indexes for existing tenant-scoped queries:

| Table | Key columns | Query pattern |
| --- | --- | --- |
| RetailProducts | BusinessId, Name, Id | Product and stock list name ordering |
| RetailCashSales | BusinessId, PostedAtUtc, Id | Dashboard and date-range sales/report filters |
| RetailCashReturns | BusinessId, PostedAtUtc, Id | Return register and daily refund date filters |

Existing unique SKU/barcode, stock balance and request-key indexes are retained.
The new product index does not make arbitrary substring searches index seeks.
Do not add further overlapping indexes or drop existing uniqueness constraints
without representative execution plans, logical reads and write-cost measurements.
The migration can be rolled back by dropping only these three indexes.

SQL Server CI includes a 5,000-product synthetic catalog with first/last/out-of-range
pages, exact SKU lookup and tenant isolation. This is a correctness regression test,
not a latency benchmark. Existing SQL integration tests exercise posted sales and
returns. Compare actual plans, logical reads, CPU, p50/p95 and write throughput on
disposable staging data (e.g. 10k/100k products and 100k/1m sales), with the same
data and filters before/after the migration. Do not infer a production capacity
or speedup from passing CI.

## Sales register and checkout concurrency

The SQL Server retail-flow regression submits an owner cash invoice register read
and a cashier checkout concurrently on independent HTTP requests. It accepts a
retriable conflict, checks a successful report's count and cash total against
the before/after committed states, replays the same checkout key, and verifies
one durable invoice, one stock movement and the correct stock balance.

This is a consistency regression, not a deterministic lock-overlap or latency
benchmark. On disposable staging SQL Server, repeat concurrent report reads
and checkouts with representative invoice volumes. Capture query duration,
logical reads, blocked process reports or lock waits, deadlocks, checkout
p95 and conflict/retry rate. Keep posting transactions and request-key constraints
unchanged. Do not use NOLOCK or downgrade report isolation solely to reduce
blocking; evaluate transaction-level snapshot isolation only with explicit
database configuration, version-store capacity and consistency testing.

## 10,000-invoice read baseline

The SQL Server integration suite includes a separate synthetic business with
10,000 invoice headers, 10,000 lines and two returns. It checks full and daily
cash totals, first/last pages, return-status filters and tenant access. Fixtures
are inserted directly in batches of 500; they do not represent checkout,
inventory posting or accounting throughput. The test records individual
end-to-end in-process API request durations via xUnit output. These are CI
observations, not production p95 or a before/after benchmark.

To repeat on disposable SQL Server with the existing test connection configured:

```sh
dotnet test backend/BusinessSaas.Tests/BusinessSaas.Tests.csproj --configuration Release --filter "FullyQualifiedName~Large_invoice_register" --logger "console;verbosity=detailed"
```

Actual SQL execution plans, logical reads, lock waits, representative concurrency
and the 100k/1m invoice targets remain separate measurement work.

## Customer outstanding read baseline

The SQL Server regression seeds 100 synthetic customers, 1,000 credit invoice
headers and lines, and 1,500 payment records. Half the invoices are fully settled;
the others have two partial payments and span all five aging categories. It checks
currency totals, overdue-only and customer filters, inactive customer debt,
stable pagination and cross-tenant denial. The test logs individual request times.
These fixtures bypass posting and are not a collections throughput benchmark.

Repeat using the isolated SQL Server test connection:

```sh
dotnet test backend/BusinessSaas.Tests/BusinessSaas.Tests.csproj --configuration Release --filter "FullyQualifiedName~Large_customer_outstanding_report" --logger "console;verbosity=detailed"
```

Keep the existing serializable report transaction and financial aggregation until
representative execution plans, logical reads and concurrent collection tests
support a specific change. Do not add indexes solely from query shape.
