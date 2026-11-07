# Salon SaaS — Salon Business Workflows

**Status:** Product specification / proposed acceptance rules; not implemented  
**Date:** 2026-11-07  
**Repository:** `dharmendra1031/salon_saas` ONLY  
**Reference:** Kaifac Salon `development` is read-only; this document does not authorize copying private code into the public repository.  
**Related:** [Master Plan](./SALON_SAAS_MASTER_PLAN.md), [UX Standards](./UX_STANDARDS.md), [Appointment & Financial Rules](./APPOINTMENT_FINANCIAL_RULES.md), [Pilot Tests](./PILOT_ACCEPTANCE_TESTS.md)

## 1. Product promise and boundaries

Salon SaaS is a multi-tenant salon operating system, not just an invoicing or ERP dashboard. Its primary jobs are:
- Complete daily reception, booking, staff and cashier work with minimal training.
- Prevent lost appointments, duplicate bookings, incorrect charges and stock losses.
- Enable reliable repeat visits, retention and business insight without forcing expensive marketing add-ons.
- Give salon owners control of their data, branches, roles, operating costs and subscriptions.

**First recommended customer segment:** conventional hair salon / barbershop. Confirm launch country and real pilot salon needs before signing off tax, payments, receipt, privacy and labor rules. Spa, nail, medspa and franchise workflows require separate discovery and cannot be assumed identical.

**Scope boundaries:** A tenant = one salon business/account; it may own several branches. Platform Admin, Salon Admin and POS have separate experiences and roles. SalonId is the tenant scope. All persisted business entity IDs use numeric SQL IDENTITY values, not UUID/GUID.

## 2. Real users and their everyday jobs

| Role | Start of day | Frequent tasks | End of day |
| --- | --- | --- | --- |
| Owner | Review yesterday's close, today's schedule, exceptions | Pricing, staff, sales, business performance, approval | Revenue and reconciliation overview |
| Branch Manager | Confirm staff and resources, inspect pending bookings | Reassign appointments, authorize adjustments, purchase/stock exceptions | Verify register close and stock exceptions |
| Receptionist | Review appointment calendar, walk-in queue and unconfirmed clients | Find/create customer, book/reschedule/cancel, check-in, take deposit | Hand off no-shows and next-day reminders |
| Stylist/Staff | Review work schedule, availability and service notes as permitted | Begin/finish service, note product use, record treatment info with consent | Complete visit/commission-eligible records |
| Cashier | Open register and verify payment terminal | Find service/product, bill walk-in/appointment, collect payment, print | Cash count, variance and close |
| Customer | Book or arrive as walk-in | Confirm, reschedule, pay permitted deposit | Receive receipt and optional rebooking |

All users should see only relevant actions and branch-scoped data. Advanced functions must be discoverable without crowding daily screens.

## 3. First-run salon activation

**Path:** SaaS signup/plan/trial -> create salon owner -> salon profile -> primary branch -> business hours/timezone/currency -> basic services and prices -> staff -> appointment preferences -> payment/cash register setup -> first booking/sale.

Acceptance:
- A salon is never provisioned halfway without a clear recover/resume path.
- Duplicate signup/retry cannot generate duplicate tenant/owner/subscription.
- Show a progress checklist and allow permitted steps to be skipped and completed later.
- Do not assume a pricing rule/tax setting without an explicit configured default and preview.
- Expose each branch's hours, closure dates and staff assignment independently.
- Trial and plan/entitlement changes are enforced on backend, not only in UI.

## 4. Appointment booking and calendar

**Path:** Choose branch -> customer or new client -> one/multiple services -> eligible staff/preferred staff -> proposed date/time -> validate staff AND resources -> optional deposit/policy -> confirm -> reminders -> check in -> perform -> checkout or cancel/no-show.

Required:
- Book multiple services in one visit while preserving their individual durations, eligible staff, price and required resources.
- Distinguish stylist's active work time from processing/waiting and finish/cleanup buffers when the service needs it.
- Track chairs, treatment rooms, basins and machines as optional branch resources; prevent conflicting exclusive reservations.
- Respect staff shifts, leave, breaks, business hours, public holidays and time zone/DST rules.
- Draft a clear policy for double-booking: do not silently allow it; only constrained, valid processing overlaps if configured.
- Use server-side concurrency checks at confirmation/reschedule time; a stale UI availability preview does not reserve a slot.
- Allow receptionist to see day/week schedule, statuses, resource conflicts and real availability.
- For walk-ins, show staff capacity and estimate waiting time only when based on reliable data.

### Proposed appointment lifecycle

`Draft/Provisional -> Confirmed -> CheckedIn -> InService -> Completed`, with `Cancelled` and `NoShow` terminal or policy-defined states. Any `Rescheduled` action must preserve audit/history; never silently rewrite history.

- Define cancellation cutoff and responsibility (customer, staff, salon).
- Payment/deposit status is separate from appointment status.
- A cancelled slot can enter a **manual waitlist** process at launch.
- Automated waitlist notifications and gap-filling are an enhancement after messaging consent, rate limits and scheduling correctness are tested.
- Booking link/QR should use salon/branch branding and guard against guessing another salon's booking context.

## 5. Customer management and relationship

**Path:** Search by phone/name -> identify existing customer with duplicate-warning -> create or open profile -> relevant visit history -> preferences and consents -> booking -> checkout -> rebook.

- Customer is salon-scoped; the same real person can belong to multiple salons without leaking records.
- Store phone in normalized form plus displayed locale-specific formatting; allow missing phone when business flow permits.
- Duplicate suggestions require human confirmation; do not merge two people automatically.
- Visit timeline should separate completed services, appointments, invoices, refunds, notes and balance usage.
- Treatment notes, allergies, photos and forms have **sensitive-data and consent implications**; access must be restricted and retention defined.
- Marketing consent is separate from booking/service notifications. Respect unsubscribe, channel-specific opt-in and suppression lists.
- Provide import (CSV or approved format) with mapping, validation, preview, duplicate resolution, dry-run summary, audit and rollback strategy.
- Provide permission-checked export of owned customer and business data; never expose records from a different tenant.

## 6. POS, billing and cash management

**Path:** Open register -> select service/product -> assign staff when needed -> customer or walk-in -> optional approved adjustments -> review tax/total -> take cash/card/split payment -> server-confirmed posted sale -> receipt -> optional rebooking -> register close.

- Single-payment checkout must be fast; split payment is accessible but not the forced default.
- Show due, paid, remaining, tendered cash and change correctly.
- Treat tips, discounts, deposits, store credit, memberships, tax and gross/net revenue as distinct financial concepts.
- A payment retry with an unknown outcome must reconcile the original transaction; never create a second invoice.
- Held bill and abandoned draft are not posted sales; authorization and retention for held records must be defined.
- Refund/void occurs through authorized compensating transactions with audit; do not quietly edit posted invoices.
- Cash register opening, cashier handover and closing count require a record of expected vs actual and authorized variance handling.
- Receipt printing failure cannot reverse successful payment or create a new sale.

See [Appointment & Financial Rules](./APPOINTMENT_FINANCIAL_RULES.md) for precise behavior and tests.

## 7. Inventory and service consumption

**Path:** Product/Unit setup -> supplier/PO -> partial or full receiving -> stock ledger -> service/product sale/consumption -> transfers, adjustments and purchase returns -> reconciliation.

- Define exactly when ownership/quantity changes: PO approval alone does not update received stock; posting goods receipt does.
- A single PO may be partially received; show ordered, received, outstanding and damaged/rejected quantities.
- Service-linked consumption should be possible as an extension using recipes/BOM for color, shampoo and other consumables. It must not double-deduct both as a service and as a sold retail item.
- Count/adjustment difference requires reason, actor and branch authorization; never use an unlogged direct balance overwrite.
- Transfers must have defined issue/receipt stages (or explicitly atomic branch-to-branch rule), with shortfalls and in-transit reconciliation.
- Negative stock rules must be explicit (default block except documented override policy).

## 8. Retention and profitability (phased)

| Capability | First release | Subsequent phase |
| --- | --- | --- |
| Booking confirmation and human-managed cancellation | Yes | Automatic re-engagement |
| Waitlist | Manual list and release handling | Automated gap-filling with opt-in |
| Deposits | Basic optional deposit accounting where provider enabled | Automated card-held no-show fee only if supported and legally reviewed |
| Client rebooking | Button at checkout | Personalized rebooking suggestions |
| Loyalty / memberships | Correctly model future entitlements | Full offers, cross-branch redemption and advanced policies |
| Gift cards / store credit | Reserve durable credit ledger design | Sales, redemption, expiry and liability reports |
| Service product consumption | Explicit stock ledger design | Detailed formulas and automatic consumption |
| Reports | Correct sales, refunds, tax and register summaries | Retention, profitability, utilization and cohorts |
| Marketing | Confirmations and consent controls | Segmentation, campaigns and AI assistant |

Do not promise advanced behavior until its financial, privacy and operational contracts are tested.

## 9. Product analytics and business outcomes

Track **platform metrics** separately from **salon operational metrics**.

Platform: signup -> activated salon -> first booking -> first paid invoice -> weekly active salons -> retained salons -> subscription renewals/churn -> support volume.

Salon: booking conversion, no-shows, cancellations, staff utilization, average ticket, customer repeat rate, retail-to-service revenue, refunds, discount rate, stock variance, register close variance.

Use explicit business definitions and denominators. Avoid misleading client-side sums from paginated results. Do not send customer PII to analytics without a legitimate basis.

## 10. UX and business acceptance

For each workflow document: actor, preconditions, permission/scope, inputs, transitions, posted data, failure/recovery, alerts, localized content, printed output and measurable task success.

**Must be validated with 3–5 real pilot salons** from the selected launch market; the number is an initial test target, not a claim that users have been recruited.

## 11. Questions that must be decided before database freeze

1. Which launch country and initial salon segment?
2. How many services/stylists/resources may be attached to one appointment?
3. What rules govern stylist processing gaps and exclusive chair/room usage?
4. How are cancellation fee, deposits, prepayments and refunds handled by local payment provider?
5. Are salon payment terminals provider-integrated or recorded as externally settled card payments?
6. What are permitted negative-stock and cost calculation policies?
7. How are gift card, package and membership balances treated across branches?
8. What is the customer data retention/export policy on salon suspension or closure?
9. Is weak-network **online retry/reconciliation only** enough at first release, or is offline acceptance a hard business requirement?
10. Which local invoice/tax/receipt and employee labor rules apply?

Do not turn unanswered choices into hidden defaults.
