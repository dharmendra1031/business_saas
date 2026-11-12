# Batch 07 — Subscription & Billing (BS-061–BS-070)

**Reviewed:** 12 November 2026. No new schema, project, payment gateway, fabricated data, or approved pricing was introduced.

| Task | Actual result | Status |
| --- | --- | --- |
| BS-061 — Starter trial | Existing new-business STARTER Trial preserved. API/UI now show active trial, expired trial, Active plan and inactive status accurately. Trial end rendered in business timezone | UX REVIEW PENDING |
| BS-062 — Plan limits | Four public plans plus custom Enterprise are proposals; numeric limits, module versions, currencies and prices need commercial approval | DECISION REQUIRED |
| BS-063 — Operation enforcement | `canOperate` is calculated on Dashboard; no Product, Purchase, Stock, POS or other business transaction API exists yet to enforce it | TODO |
| BS-064 — Upgrade/downgrade | No approved pricing, term or proration rules; no fake upgrade workflow added | TODO |
| BS-065 — Provider choice | Launch market/payment provider and supported payment methods are not approved | DECISION REQUIRED |
| BS-066 — Checkout | No redirect/return verification or pending-payment lifecycle exists yet | TODO |
| BS-067 — Webhook security | Signing/duplicate-event policy depends on chosen provider; not implemented | TODO |
| BS-068 — Payment history | No real subscription invoices/payments are created, so no artificial history screen | TODO |
| BS-069 — Failed payment/grace | Retry, support, read/export and grace duration need approval. Currently any unapproved Grace, PastDue, Suspended or Cancelled state is non-operable in Dashboard | DECISION REQUIRED |
| BS-070 — Expiry/renewal tests | Added Trial/TrialExpired/Active/Suspended/PastDue/Grace/Cancelled state tests; renewal and actual backend entitlement denial remain pending | TODO |

## Verified changes

1. The existing business Dashboard API computes `accessStatus` from current subscription state and UTC expiry, and continues returning `canOperate` for compatibility. Only `Trial` within its window or `Active` is operable by this *Dashboard*. This is not a reusable authorization guard for future transaction endpoints.
2. Retail Dashboard no longer uses the ambiguous "Plan required" for an expired trial. It displays an accurate warning, does not falsely turn an inactive state green, and explains that subscription activation is not yet available.
3. Trial end date **and time** use the business's configured IANA timezone, rather than the viewing device's local date.
4. Existing xUnit tests now cover status transitions including expired, Active and non-operable suspension/payment-related states; existing tenant tests remain intact.
5. All IDs remain numeric SQL Server identity IDs; no schema migration or payment operation was introduced.

## CI

- [Subscription status code and test CI — 43149522312](https://github.com/dharmendra1031/business_saas/actions/runs/43149522312): **SUCCESS**; backend 8/8, SQL Server 1/1, frontend PASS.
- [Business timezone display CI — 43149740696](https://github.com/dharmendra1031/business_saas/actions/runs/43149740696): **SUCCESS**; frontend/backend/SQL Server jobs PASS.

## Mandatory UI/UX acceptance

Static review: clear status naming, trial-end date/time, non-green inactive label, no invented payment actions, honest warning and support next step. API-level tenant boundaries were kept.

**Not yet verified in a running browser:** Owner/Manager/Cashier role flows, viewport widths 360/768/1440px, keyboard focus, cross-timezone date display, exact expiry-boundary presentation and screen-reader behavior. Keep BS-061 at **UX REVIEW PENDING**; no task is marked DONE without this evidence.

## Remaining policy and technical blockers

Approve initial sales market, plan prices and quota/version rules, verified gateway and refund policy, signed webhooks/idempotent payment posting, failure/grace lifecycle and historic read/export rules. Future operational endpoints must check BusinessId, membership, branch, subscription state and actual entitlements **server-side**; neither a Dashboard flag nor hiding a button is authorization.

**Next recommended slice:** Start real Retail Catalog/Purchase/Stock APIs and apply the subscription gate to actual business writes as those endpoints are introduced.
