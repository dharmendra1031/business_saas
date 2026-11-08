# Salon Vertical — Pilot and Production Acceptance Plan

> **Scope notice:** **Salon vertical pilot plan**, planned for the second operational release. It must not be used as evidence that the first General Retail pilot or the platform has been tested. Refer to [Development Roadmap](./DEVELOPMENT_ROADMAP.md) and [Retail V1 SRS](./RETAIL_V1_SRS.md) for the first business type. BusinessId is the platform tenant key.

**Status:** Proposed test plan; no users recruited, pilots run, builds completed or tests executed.  
**Repository:** `dharmendra1031/business_saas` ONLY  
**Scope:** Pilot salons, acceptance scenarios, usability, security, finance, reliability and go-live decision.  
**Related:** [UX Standards](./UX_STANDARDS.md) · [Salon Workflows](./SALON_BUSINESS_WORKFLOWS.md) · [Appointment & Financial Rules](./APPOINTMENT_FINANCIAL_RULES.md) · [Engineering Guardrails](./ENGINEERING_GUARDRAILS.md)

## 1. Pilot strategy

- Start with **3–5 real salons** after choosing one launch market and initial salon segment. The pilot count is a planning target.
- Aim for different salon sizes: one small single-chair team, one multi-staff salon, one branch-managed salon, plus a multi-branch operator if available.
- Observe at least Owner, Receptionist, Cashier and Branch Manager tasks; involve Staff where booking/consumption applies.
- Use consented pilot business data only in authorized environments, with migration rules, masking and retention.
- Do not use real customer's card details or private photos in test fixtures or public repositories.
- Record difficulties, false assumptions, required help, errors and recovery outcomes—not just screen ratings.

## 2. Required pilot tasks

| Scenario | Actor | Pass condition |
| --- | --- | --- |
| New salon self-setup | Owner | Can complete/resume configuration and create first service/branch without support intervention |
| New appointment | Receptionist | Can find/create customer, see true slot availability, confirm and view booking |
| Multi-service booking | Receptionist | Durations, staff, resources and pricing correct, including optional processing gaps |
| Reschedule/cancel | Receptionist | No stale resource holds; history and deposit policy understandable |
| Waitlist and gap-fill | Receptionist | Manual offer/confirmation is correct; no double booking |
| Walk-in arrival | Receptionist | Can place customer into queue/book suitable staff without losing existing appointment |
| Routine sale | Cashier | Correct cart, staff, tax, payment, invoice and receipt with minimal guidance |
| Split tender / deposit | Cashier | Paid/remaining figures correct; deposit never counted twice |
| Held bill and retry | Cashier | Draft returns without becoming extra sale; uncertain payment reconciled safely |
| Refund / void | Manager + Cashier | Correct original invoice, limits, permission, ledger and stock reversal |
| Open/close register | Cashier | Expected cash reconciles; manager approval on variance |
| PO / partial receiving | Manager | Outstanding and inventory ledger correct; cannot over-receive |
| Stock adjustment | Manager | Requires reason and approval as configured; reconciled movement visible |
| Reports | Owner | Correct branch/date totals and export; no client-side paginated mis-sums |
| Plan limits | Owner | Appropriate message and upgrade path when permitted limit reached |
| Module disabled | Staff | Menu/UI reflects entitlement; direct API rejects unauthorized action |
| Language switching | Owner/Cashier | UI remains usable in English, Arabic RTL and a newly uploaded language |
| Import / export | Owner | Preview/dedupe/import counts and export permissions correct |
| Poor network | Cashier | No duplicate payment or invoice; outcome can be checked |
| Support incident | Support Agent | Diagnostics sufficient without default exposure to private tenant records |

## 3. Critical automated regression suites

### Tenant and authorization

- Authenticate distinct users under two salons, possibly same phone/email where supported.
- Attempt all sensitive cross-salon reads/writes for customers, appointments, invoices, branches, employees, stocks, files, reports and plan records.
- Validate role + subscription + module + branch permissions server-side.
- Test unauthorized report exports, cached tenant data, cross-tenant object URLs, forged parameters and disabled-module deep links.
- Ensure platform support doesn't have unrestricted business-data privileges.

### Money and payments

- Verify tax/discount/rounding policy with multiple line items and split tenders.
- Cash with change, integrated/external card reporting, tips, deposits and outstanding amounts.
- Double-click, retry, processor timeout, delayed webhook, duplicate webhook, refund racing with payment confirmation.
- Full/partial refunds, prior refund total, gift card/store credit ledger where released.
- Invoice printing failure must never affect invoice posting or payment reconciliation.

### Appointments and inventory

- Simultaneous bookings for same stylist/resource; only one valid winner.
- Processing-stage resource reservation, shift/leave checks and special holiday hours.
- Multi-service timing, appointment cancellation/reschedule, no-show deposit outcome.
- Partial receiving, damaged stock, returns, transfers and concurrent stock deduction.
- Compare inventory ledger to balance after repeated posting/retries.

### SaaS subscriptions and languages

- Trial start/end, grace, renewal, upgrade/downgrade, past due, reactivation and cancellation.
- Plan/module override effective dates, limit exhaustion, historical data access during suspension.
- Invalid translation upload, missing placeholders, locale fallback, publish/rollback and RTL handling.
- Rate limit, file upload validation, missing/expired token and sensitive log masking.

## 4. Usability quality gates

1. **Task completion:** pilot participant can find the correct workflow and complete it without a developer walking them through every step.
2. **Error recovery:** deliberate failures show human-readable next steps and preserve user input.
3. **Consistency:** one action/location/name for the same business concept; no duplicate misleading menus.
4. **Efficiency:** measured baseline task times and misclicks; a prepared-basket routine sale target is median active interaction under 30 seconds for trained cashier, excluding network and printer wait. **Target, not a measured result.**
5. **Accessibility:** target WCAG 2.2 AA; automated checks **plus manual keyboard and assistive technology tests**.
6. **Responsive:** phone/tablet/laptop/desktop layouts do not block business tasks; data tables may scroll within their container.
7. **Localization:** English, Arabic RTL and at least one uploaded third locale; text expansion, number/currency formatting and receipts.
8. **Real-world equipment:** barcode scanner, receipt printer, cash drawer and relevant payment terminal integrations according to launch market.
9. **Environment:** test slow network/reconnect, multiple tabs/sessions and stale appointment slots.
10. **Feedback log:** record severity, reproducing steps, frequency, user role, screen and resolved status.

A failed critical task or unresolved financial/tenant leak blocks rollout even if code compiles or unit tests pass.

## 5. Quality and release gates

| Gate | Required evidence |
| --- | --- |
| G0 — Product | Initial market/segment, workflows, rights, pricing and jurisdiction policies signed off |
| G1 — Architecture | Repository privacy approved; threat model, tenant boundaries, migrations, basic operational design |
| G2 — Function | Feature tests and end-to-end task tests; no unresolved blocker |
| G3 — Financial integrity | Reconciliation, refunds, tax, stock and idempotent retry tests passing |
| G4 — Security | No tenant leakage; secret scan; auth/permissions; vulnerability review |
| G5 — Usability | Observed pilot tasks, keyboard/RTL/responsive/print checks, critical fixes |
| G6 — Operations | Monitoring, alerts, tested restore, payment reconciliation, rollback, incident process |
| G7 — Performance | Representative load test with real workload assumptions; no unsupported 10k-concurrent-user claim |
| G8 — Launch | Approval, staged rollout, customer support, incident response and documentation |

Do not claim PASS unless evidence exists. Maintain the run date, environment, tool/command, result, links and remaining issues for every gate.

## 6. Proposed go / no-go decisions

**NO-GO** if:
- Another salon's data can be accessed by changing ID or domain.
- Checkout can double-charge or produce a duplicate invoice on retry.
- Paid/refunded amounts or stock ledger cannot reconcile.
- Confirmed bookings can double-book a restricted staff/resource slot.
- Backup has never been successfully restored in an isolated environment.
- Critical cashier tasks fail with keyboard/mobile/Arabic RTL or a basic printer outage.
- An undisclosed paid fee, unworkable data export policy or missing cancellation rule blocks a pilot customer.
- A release depends on copying private Kaifac code or real data to a public repository without approval.

**GO** only after critical workflows, tenant security, money accuracy, monitored operations, privacy and customer success gates have actual evidence.

## 7. Phase-driven acceptance

**Early foundation:** tenant signup/auth, plan entitlements, branch boundaries, language fallback, service/customer setup and booking structure.

**First operational release:** appointments, walk-ins, staff/resources, POS/payment/receipt, register, cancellations, optional deposits, manual waitlist, essential purchase/stock, report reconciliation, safe import/export.

**Next release:** advanced loyalty/membership, gift card/store credit, complex treatment notes, product consumption recipes, retention analytics, automated waitlist/reminders where consented.

**Later:** AI receptionist, marketing automation, franchise console and dedicated tenant infrastructure where justified.

## 8. Artifacts to keep with each pilot/test cycle

- User roles, task scripts and consent protocol.
- Reproducible fixtures with synthetic or approved masked data.
- Task duration, completion, misclick, error and issue list.
- Backend/browser/SQL authorization and concurrency test reports.
- Version, deployment environment, migration script, restore test and rollback evidence.
- Issue owner, priority, accepted risk and release decision.

**Current verification:** This is a planning document only; no actual pilot study, runtime test, penetration test or load test is claimed.
