# Salon Vertical — Appointment and Financial Invariants

> **Scope notice:** **Salon-only appointment/financial rules**, not a generic Retail booking feature. Financial safeguards (idempotency, refund authority, stock ledger) also apply where relevant to other businesses. New platform tenancy uses `BusinessId`, not historical `SalonId`. See [Master SRS](./BUSINESS_SAAS_MASTER_SRS.md) and [Retail V1 SRS](./RETAIL_V1_SRS.md).

**Status:** Proposed business specification. No database migrations or production flows implemented.  
**Scope:** Booking, resources, deposits, payments, refunds, credits, cash register and inventory consistency.  
**Repository:** `dharmendra1031/business_saas` ONLY  
**References:** [Salon Workflows](./SALON_BUSINESS_WORKFLOWS.md) · [Engineering Guardrails](./ENGINEERING_GUARDRAILS.md)

## 1. Non-negotiable rules

1. **Tenant isolation:** every operation uses trusted BusinessId and authorized BusinessLocationId. An entity ID from another salon must never expose or change records.
2. **No double-booking of exclusive resources:** final booking/reschedule checks staff and configured chair/room/equipment overlap under concurrency.
3. **No duplicate sale or capture:** retried requests and payment callbacks identify the same logical operation and reconcile state before any second attempt.
4. **Server is financial authority:** do not trust UI subtotals, discounts, tax, tips, balances or eligibility decisions.
5. **Posted records are auditable:** use reversible postings or compensating ledger entries, not silent edits to final financial/stock records.
6. **Money is decimal:** define currency precision and one rounding policy per market; no floating-point money arithmetic.
7. **Booking is not payment:** status, due amount, captured funds, refundable balance and no-show policy have distinct lifecycles.
8. **Unknown network outcome is not failure:** user must be able to check transaction state before retry.
9. **Registration/plan changes cannot alter existing completed invoices or balances retroactively.**
10. **Printed output is not transaction posting:** reprint/print failure does not change the posted financial outcome.

## 2. Scheduling model

A booking contains one or more **service segments**, with assigned eligible staff and resource reservations. The booking's visible time range may contain:
- preparation/setup
- active staff service
- optional processing/wait period
- finishing/rinse
- cleanup/buffer

### Resource and staff occupancy

- A service defines the staff skill requirement, duration components and required resources.
- Exclusive rooms/chairs/basins/machines cannot overlap another reserved exclusive interval.
- Staff cannot be double-booked while actively required; a processing-only gap may free the stylist if configured and tested.
- Never assume a processing gap frees the chair; make resource release/hold part of service configuration.
- Staff leave, breaks, clock hours, business hours, holiday closures and cross-midnight services must be handled.
- Use branch timezone for calendar display and UTC timestamps for storage where appropriate; handle DST ambiguity in markets that observe it.
- Bookings from POS, Reception, public booking page and API share one validation and concurrency rule.

### Example: hair colour service

One example, **not a global default**: 30 min application + 45 min processing + 30 min finish. Another booking can consume available stylist time during the 45 min processing only if resources and staff availability permit. Preserve the original appointment's finishing allocation so it cannot be stolen by a new booking.

### Booking status and concurrency acceptance

- `Draft/Provisional`: editable, not necessarily capacity-reserving unless hold expiry is explicitly configured.
- `Confirmed`: staff and resource occupancy committed; confirmation message is a separate task.
- `CheckedIn`, `InService`, `Completed`: lifecycle with accountable transition times/actors.
- `Cancelled`, `NoShow`: terminal operational states, with financial policy handled separately.
- Reschedule keeps history of previous slot, actor, reason and notification status.
- Two concurrent requests for the same exclusive resource/time cannot both confirm.
- Time-slot confirmation/rebooking validates the freshest committed data, not only stale availability search.
- Failure must return an understandable alternative-slot / resource-conflict message.

## 3. Cancellation, waitlist, deposits and no-shows

- Salon config determines permitted cancellation windows and any charge policy, subject to market law and payment provider capabilities.
- Deposits have their own payment transactions and are linked to booking and later invoice.
- On service completion, applied deposit reduces the new amount collectible; it is **not counted as new cash twice**.
- Cancellation/no-show may yield retained deposit, refundable deposit, transferable credit or provider-authorized fee per policy.
- No automatic payment capture for no-show unless provider supports the action and the customer has provided valid authorization.
- The first-release waitlist is **manual**: staff can offer newly available slots and confirm reservations before the slot is assigned.
- Automated offers must have hold-expiry, opt-in/opt-out, idempotency and race-condition rules before enabling.
- A waitlisted customer is not an appointment; do not consume resources until a booking is confirmed or a timed hold explicitly exists.

## 4. Sale calculation and ledger accounting

For a sale, separate:
- Service and retail line item subtotals.
- Discounts (line-level and invoice-level with defined allocation).
- Tax and applicable inclusive/exclusive handling.
- Tips paid to staff and tips due for payout.
- Prepaid deposits, redeemable store credit, gift cards and package entitlements.
- Payments by tender (cash, external card, integrated card, other approved methods).
- Outstanding amount, refunds and net paid.

**Invariants:**
- Gross, discount, tax, tip, payable and remaining values reconcile under a documented formula.
- Paid amount cannot exceed allowable totals except valid cash tender with explicit change.
- A tip is not automatically ordinary service revenue; accounting presentation is defined by jurisdiction.
- Gift card/store credit represents a separately tracked liability or credit balance, not an untraceable negative discount.
- A split payment posts exactly the approved tender legs once.
- Authorized discount limit, service cost visibility and refund eligibility are backend rules.
- Use minor-unit/decimal policies appropriate to salon's configured currency, not user interface language.

## 5. Sale/payment state and uncertain outcomes

A minimal conceptual model distinguishes `Draft`, `PaymentPending`, `Posted`, `Void/Refunded` where legal and business semantics permit.

1. User initiates checkout with an operation reference whose server-side identity is durable; external processor references may be strings, but business table IDs stay numeric.
2. The backend computes final totals and validates sale/register/permissions/stock.
3. Payment interaction creates/stores attempt state and safely handles duplicate requests.
4. A known processor success is verified/reconciled before invoice finalization; timeout is `Unknown/PendingReconciliation`, **not automatically failed**.
5. On retry, client checks original status using the stored operation reference; never blindly charge again.
6. Stock consumption, loyalty/membership consumption and invoice posting use explicit transaction boundaries and compensations when an external payment call cannot participate in a database transaction.
7. An already posted sale returns the same invoice/result to repeated requests.
8. Printing follows posting and can be repeated without posting again.

### Payment integration decisions to finalize

- Which markets/currencies/payment gateways? Who is merchant of record for salon POS payments versus SaaS subscriptions?
- Does a terminal report a charge, or is a card receipt recorded manually? Do not call a recorded payment processor-verified.
- What is the reconciliation process for webhook late arrival, reversal, partial success and chargebacks?
- Which payment credentials and settlements belong to salons, and who manages them?

## 6. Refunds, voids and customer credit

- Refund requires original posted invoice and refundable balance; enforce permissions and reason.
- Partial refund cannot exceed the remaining refundable amount across prior refunds.
- Choose whether each refunded sale line restocks inventory and whether staff commission is reversed; apply consistently.
- Cancellation of a booking does not implicitly refund a deposit unless policy explicitly says so.
- Gift cards, service packages, memberships and store credit use an immutable redemption/issuance/adjustment ledger or equivalent auditable history.
- Expiry/fees for prepaid balances require market-specific legal review and explicit customer-facing terms.
- Refund to original tender versus credit is an explicit selectable, permitted workflow, not silent substitution.

## 7. Cash register and reports

- Register opens per branch and intended cashier/session policy; concurrent cashier sharing rules are explicit.
- Posting cash tender contributes to expected drawer cash; card does not.
- Closing compares expected cash with counted cash and shows variance before commit.
- Variance requires authorized approval/reason above salon-configured thresholds.
- Reports distinguish appointment value, invoiced gross, discounts, taxes, tips, refunds, net sales, actual collected payment and outstanding balances.
- A date range uses salon branch business dates and correctly handles shift close crossing midnight.
- Platform subscription revenue must never be mixed with the salon's customer sales.

## 8. Inventory posting and stock invariants

- Purchase order creation/approval does not increase stock; goods receipt posting does.
- Partial goods receipt keeps outstanding quantity visible and prevents over-receipt.
- Retail sale or service consumption decreases appropriate branch inventory according to the finalized posting event.
- Refund/return and stock adjustment have separate, audited posting paths.
- Inter-branch transfer cannot result in stock creation; define source issue, in-transit and destination receipt transitions.
- Race conditions cannot result in negative inventory unless an explicitly authorized policy permits a controlled exception.
- Stock balance reconciles with an immutable movement ledger; never rely on a UI cached quantity as final availability.

## 9. Minimum negative and concurrency test matrix

| Test | Expected |
| --- | --- |
| Two bookings reserve same exclusive chair/time | Exactly one confirms; conflict shown for other |
| Stylist on leave receives appointment | Blocked with reason |
| Colour processing overlap using same chair | Block unless chair explicitly released |
| Salon A attempts Salon B booking/invoice ID | Denied without leaking fields |
| Duplicate checkout click | One posted sale / one charge |
| Checkout timeout after processor success | Reconciliation resolves original; no second charge |
| Card terminal failed but UI assumed success | Invoice not falsely marked paid |
| Deposit applied to bill | No double counting |
| Concurrent partial refunds | Total refunded <= eligible balance |
| Split tender where one leg fails | No invisible partial-post corruption; recoverable state |
| Refund posted sale | Audit and stock/commission reversal rules applied |
| Partial receipt and over-receiving | Remaining accurate; over-receive rejected |
| Two stock transfers compete for last units | No negative stock |
| Register card sale | Does not increase expected cash |
| Print failure / reprint | Invoice number and ledger unchanged |
| Subscription suspended | Business access according to published policy; historic data preserved |

## 10. Decisions required from product owner / jurisdiction

The policy for deposit retention, processing time, no-show fees, tips, gift card expiry, tax, store credit and monetary rounding must be approved for each launch market. They are deliberately **not** silently invented in this document.
