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
