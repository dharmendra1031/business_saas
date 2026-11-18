# Business SaaS — Subscription Plans and Module Entitlements

**Status:** Proposed commercial structure for implementation design. Actual prices, feature inclusions and quantitative limits remain configurable and require commercial approval.  
**Repository:** `dharmendra1031/business_saas` only  
**Reference:** [Canonical Master SRS](./BUSINESS_SAAS_MASTER_SRS.md)  
**Last updated:** 2026-11-07

**Scope:** Business-wide SaaS plan catalog. Module inclusions vary by published business template and immutable plan version; the examples are not universal hardcoded entitlements. Global platform models use **BusinessId**, not SalonId.

## 1. Plan catalog

The catalog contains **four public subscription plans** and **one custom Enterprise plan**. **Free Trial is not a separately paid plan**; it is a configurable subscription phase.

| Plan code | Display name | Intended business | Proposed features |
| --- | --- | --- | --- |
| `STARTER` | Starter | Small / single location | Basic operational catalog and POS or booking, only when the business template supports it |
| `GROWTH` | Growth | Growing business | Expanded template-compatible inventory, purchase or scheduling modules and reports |
| `PROFESSIONAL` | Professional | Established business | Template-compatible staff, customer engagement and advanced reports |
| `BUSINESS` | Business | Multi-location business | More locations, transfers where supported, consolidated reports and advanced permissions |
| `ENTERPRISE` | Enterprise | Chain / custom account | Negotiated modules, limits, white-label domain/branding, optional dedicated/customer-managed database and support after approved provisioning |

- Starter, Growth, Professional and Business are shown in the public plan picker.
- Enterprise uses **Contact Sales / Custom Pricing**, with an explicitly approved contract/quote. White-label brand, custom domain, managed dedicated DB, BYO/customer-hosted DB and isolated deployment are **separately scoped options**, not automatic free features. Only tested supported engines may be quoted.
- Business-facing customer memberships (e.g., salon packages) are distinct from the business's SaaS subscription.
- Plan names/codes should remain stable identifiers in configuration. Display text is localized (English/Arabic) and may be edited without changing IDs.
- The initial module matrix is a **starting configuration**, not a hardcoded product rule.

## 2. Plan administration (Platform Super Admin only)

The SaaS Panel owns:

1. Create, edit, archive, activate and reorder plans.
2. Configure plan title, localized description, visibility and supported billing cycles.
3. Set separate prices for **monthly, quarterly and yearly** billing and the associated currency/tax settings.
4. Set trial eligibility and trial days (including zero).
5. Assign modules/features to plans and enforce module dependencies.
6. Set account limits: branch count, active users, POS terminals, storage and any measured paid-usage quota.
7. Configure optional **add-ons**: extra branches, POS terminals, additional users, storage and WhatsApp/message credits.
8. Grant temporary or permanent salon-specific entitlements/overrides with reason, actor, effective dates and audit log.
9. Activate, suspend or retire a plan; retiring a plan must not silently modify an existing paid subscription.
10. View subscriptions, renewals, payment failures, invoices, credits and usage without unrestricted browsing of salon business records.

Public prices, exact quotas and provider charges **are not yet finalized**. Do not use illustrative numbers in schema or API logic as fixed limits.

## 3. Module catalog and default grouping

| Group | Modules / capabilities |
| --- | --- |
| Core | Business profile, location settings, owner account, users, roles and relevant customer/catalog master |
| Booking | Appointments, calendar, staff availability |
| Sales | POS, payments, receipts, cash register, sale returns/voids |
| Inventory | Products, stock balances, adjustments, transfers |
| Purchase | Suppliers, purchase orders, goods receipts |
| Staff | Employees, scheduling, attendance, commissions, payroll |
| Engagement | Customer memberships, service packages, loyalty, offers |
| Reporting | Basic reports, advanced reports, consolidated multi-branch reports |
| Messaging | Email/SMS/WhatsApp notifications and associated paid credits |
| Integrations | External APIs or integrations when provisioned |

A paid plan, add-on or override must never bypass platform security or remove a required core prerequisite. For instance, POS needs the relevant service/product catalog and safe checkout permissions; consolidated multi-branch reports require the business to have access to the selected branches.

## 4. Entitlement resolution

**Effective access = subscription state AND module entitlement AND usage limit AND authenticated role/permission AND authorized branch within the same business.**

1. Authenticate and resolve a **trusted BusinessId**.
2. Evaluate subscription state according to the published grace/suspension policy.
3. Resolve effective enabled modules from plan + active add-ons + approved salon overrides.
4. Check limits before *creating* a branch, activating a user, registering a terminal, etc.
5. Check role permissions for the specific action and allowed branches.
6. Enforce the result on **every relevant backend API**, not only in navigation.
7. Cache with BusinessId-scoped keys and explicit invalidation on plan/override/permission changes.
8. Keep read-only access to historical records/export under an explicit policy when access is downgraded or expires. Never delete data on module disable.

If a plan changes, existing subscribers should retain the agreed entitlement version until a defined renewal/migration event; do not retroactively downgrade customers by editing a shared plan row.

## 5. Trial and subscription lifecycle

- **Trial:** a configurable duration and allowed features, with clear expiry date and upgrade path.
- **Active:** normal entitlements and limits.
- **Grace:** published warning and limited-time access rules following payment failure.
- **Past Due:** payment-recovery and support rules; choose whether Grace applies.
- **Suspended:** disallow restricted writes while preserving applicable billing, support and export access.
- **Cancelled / Expired:** apply the documented retention and reactivation process; do not automatically delete financial history.

Invoices, payment callbacks/webhooks, renewals, credits and state transitions must be idempotent, auditable and reconciled with the payment provider. A network error is not proof a payment failed; do not duplicate charges automatically.

## 6. Data model (conceptual)

All entity primary keys must be **numeric SQL Server IDENTITY** (e.g. BIGINT IDENTITY). No GUID/UUID entity IDs.

| Table | Key fields / purpose |
| --- | --- |
| `Modules` | Id, Code, NameEnglish, NameArabic, IsActive |
| `SubscriptionPlans` | Id, Code, NameEnglish, NameArabic, IsPublic, IsActive |
| `PlanVersions` | Id, SubscriptionPlanId, VersionNumber, EffectiveFrom, Status |\n| `PlanVersionModules` | Id, PlanVersionId, ModuleId |
| `PlanPrices` | Id, PlanVersionId, BillingPeriod, CurrencyCode, Amount, IsActive |
| `PlanVersionLimits` | Id, PlanVersionId, LimitCode, LimitValue |
| `BusinessSubscriptions` | Id, BusinessId, PlanVersionId, Status, StartedAt, EndsAt |
| `BusinessModuleOverrides` | Id, BusinessId, ModuleId, IsEnabled, Reason, EffectiveFrom, EffectiveTo |
| `BusinessAddOns` | Id, BusinessSubscriptionId, AddOnId, Quantity, Status |
| `SubscriptionInvoices` | Id, BusinessSubscriptionId, InvoiceNumber, Total, Status |
| `SubscriptionPayments` | Id, SubscriptionInvoiceId, Amount, Status, ProviderReference |
| `BusinessUsage` | Id, BusinessId, UsageCode, Quantity, MeasuredAt |
| `SubscriptionAuditLogs` | Id, BusinessId, ActorUserId, Action, ChangeSummary, CreatedAt |

These are **design candidates**, not a migration request. Review duplicate entities and actual query patterns before introducing tables; reuse existing models when appropriate. External payment references or secure idempotency tokens are **not entity IDs**.

## 7. Implementation order and acceptance

1. Finalize module codes, dependencies, plan limits and expiry policy.
2. Implement Platform Super Admin plan/module configuration in `business_saas`.
3. Implement trusted tenant-scoped subscription and entitlement resolution in backend.
4. Apply checks at APIs and roles/branches, then render allowed modules in Business Admin and relevant POS/Operations.
5. Implement trials, lifecycle billing, add-ons, renewals and webhook handling.
6. Test plan upgrade/downgrade, expiry, paid grace, exceeded limits, permissions and cross-business access.

**Acceptance tests:** module disabled => UI hidden **and API denied**; plan quota reached => relevant create denied; authorized branch only; expiry preserves historic data; renewal/payment retry is idempotent; user permissions cannot exceed subscribed entitlements.

## 8. Decisions

- **SP-001:** 4 public plans — Starter, Growth, Professional, Business.
- **SP-002:** 1 sales-led custom plan — Enterprise.
- **SP-003:** Free Trial is a subscription phase, not an additional paid plan.
- **SP-004:** Plan inclusions, prices and quotas are data-driven through Platform Super Admin, not hardcoded.
- **SP-005:** Module visibility is not authorization; API, branch and tenant checks are mandatory.
- **SP-006:** Any private Kaifac/CBK source remains read-only and is not copied to this public repository.

## 9. Implemented Super Admin draft and suspension controls (17 Nov 2026)

This section describes the current implemented slice; the commercial proposal above is not a declaration that every feature is live.

- The **Subscription Plans** page lists five approved plan codes: STARTER, GROWTH, PROFESSIONAL, BUSINESS and ENTERPRISE, with real business-assignment counts. Enterprise remains custom / contact sales.
- A verified Super Admin can save **draft** currency, monthly/quarterly/yearly prices, maximum locations, users and products. Blank fields remain unconfigured; numbers are not approved commercial rates. A numeric auto-increment `SubscriptionPlanDraft.Id` stores each plan's current draft and integer `Revision` prevents stale saves. Each revision requires a written reason and produces a numeric platform audit event, with a summary of old/new draft terms.
- **Drafts do not publish, activate, charge or enforce prices, quotas or modules.** Existing `BusinessSubscription.PlanCode` records and contractual entitlements are not modified by draft changes. Do not advertise drafts as active pricing.
- Only MFA-verified Super Admin may **Suspend** a Trial/Active subscription or **Reactivate** a Suspended subscription. Each transition requires the expected current status and a 10–200 character reason. A conditional SQL update and audit event are committed together. Stale or replayed status-change requests return a conflict.
- `StatusBeforeSuspension` preserves the precise previous state. Reactivating a trial does **not** extend its expiry; an already expired trial remains expired. Read-only views remain scoped and available, while restricted writes, including shop staff-management writes, are denied during suspension.
- Direct upgrade/activation of Trial to Active is **not implemented** without the future verified payment/subscription workflow. No subscription can be fabricated as paid through these controls.
- SQL migration: `20261117200000_AddPlanDraftsAndSubscriptionSuspension`. Review/apply it against the intended SQL Server after an appropriate backup; do not claim that a local production database has already migrated.
- Regression tests cover MFA/tenant access, valid and invalid draft values, revision conflict, unchanged existing PlanCode, suspension/replay/reactivation, owner write denial and SQL migrations.

**Still pending:** approved public rates; genuine published immutable plan versions, module/usage enforcement, subscription payment ledger, invoices, renewals, paid activation and grace/dunning policies; actual browser visual/interaction comparisons against approved screenshots.

## 10. Immutable catalog publication (17 Nov 2026)

The Super Admin can publish reviewed **public** plan drafts as immutable catalog versions. This is a catalog-recording capability, **not a public payment/checkout launch**.

- A publish request requires an MFA-verified Super Admin, 10–200 character approval reason, exact expected draft revision and expected currently published version.
- Starter, Growth, Professional and Business may be published only when the draft contains a three-letter currency, **positive** monthly/quarterly/yearly prices and all three positive quantity limits. Enterprise remains bespoke (contact sales) and cannot be published from this endpoint.
- The new numeric-ID `PublishedPlanVersions` table stores a complete snapshot and `Version` per `PlanCode`; its unique `(PlanCode, Version)` constraint prevents duplicate version numbers. Previous versions remain immutable and queryable.
- The `Plan.Published` audit event records the published snapshot's numeric ID and operator's reason in the same transaction. Repeated or stale publish attempts are rejected; no existing subscription terms are silently updated.
- The Super Admin Plans screen shows `Published Catalog`, plus a review-and-confirm flow. It does not present catalog prices as a chargeable product until the payment/subscription lifecycle is delivered.
- The schema migration is `20261117210000_AddPublishedPlanVersions`. Review the intended SQL Server environment and backup/restore plan before production migration.

**Not implemented by catalog publication:** payment provider integration, tax invoice issuance, payment ledger, plan assignment/checkout, new customer paid activation, immutable plan-version binding on a business subscription, and server-enforced plan/module quota resolution. Until those are built and tested, customers retain the existing subscription behavior without commercial enforcement.

## 11. Internal billing draft foundation (18 November 2026)

The `Plans & Billing` screen now has **Plan Catalog** and **Billing Drafts** tabs. The latter prepares an internal price snapshot without creating a legal invoice, charging a customer, or activating a paid subscription.

- The MFA-verified Super Admin chooses a numeric business ID and monthly/quarterly/yearly cycle. The preview derives the matching **latest published** plan version from the business's current `PlanCode`.
- The business currency must match the published catalog currency; otherwise the preview/draft is rejected. The amount comes only from that immutable published plan version, not a user-supplied amount.
- The admin confirms a required reason before a numeric `SubscriptionBillingDraft.Id` is generated. A separate `RequestKey` protects against retries; it is an operation token, **not an entity ID**. The database enforces unique `(BusinessId, RequestKey)`.
- Each draft stores the numeric business and published plan version IDs, plan code, billing cycle, currency, monetary amount, actor ID and timestamp; `Billing.DraftCreated` is recorded in the **same transaction**.
- Admins can review paginated billing draft history. The record is always `Draft`, not `Paid`. No public business workspace or non-MFA user can create drafts.
- **No payment provider, payment collection, payment ledger, GST/tax invoice, credit note, refund, statutory invoice number, due date, billing period or paid activation has been implemented.** Drafts have no external customer-facing invoice or payment obligation. The platform's revenue KPI must continue to show *Not available*.
- Database migration: `20261118100000_AddSubscriptionBillingDrafts`. Apply only after review/backup of the intended SQL Server database.
- Tests cover anonymous and customer denial, MFA-only access, preview validation, stale published version, replay request key, unmodified Trial subscription and SQL migration.

**Next safe slice:** Confirm pricing/tax requirements, add period-specific invoices and immutable verified payment events, integrate a trusted payment provider/webhook, and only then introduce paid activation and revenue totals. Never use a draft amount as collected revenue.

## 12. Billing period integrity (18 November 2026)

The internal draft workflow now records explicit **start-inclusive / end-exclusive** subscription service dates. This correction prepares the existing billing foundation for later invoice safety, without creating another billing module.

- On preview, the admin supplies the business, selected published plan cycle and intended start date; the end date is derived by adding 1, 3 or 12 calendar months. Any valid start day is accepted, including anniversary dates; there is no unsupported first-of-month rule.
- The draft persists nullable `PeriodStartDate` and `PeriodEndDate` (SQL `date`). Existing historical drafts keep null dates rather than receiving invented periods.
- Existing drafts with known dates must **not overlap** another draft for the same business, even if request keys or cycles differ. The API checks interval overlap with a serializable transaction; a unique filtered index on `(BusinessId, PeriodStartDate)` also guards simultaneous same-start attempts.
- Replayed request keys return the original draft only if business, published plan version, cycle and period start match; otherwise they fail with a conflict.
- The Super Admin preview, confirmation and draft history display the service dates. There is still no payment collection, tax invoice, statutory invoice number, automatic recurring charge or paid activation.
- SQL migration: `20261118113000_AddBillingDraftPeriods`. Apply with the intended environment's standard backup and deployment review.
- Regression coverage includes valid anniversary starts, invalid date/missing period, duplicate overlapping cycles, unchanged trial status and SQL Server migration/model snapshot parity.

Before introducing real invoices, confirm issuer tax jurisdiction, invoicing/tax rules and payment gateway. Period-aware drafts are **not** legal or tax invoices.

## 13. Payment preparation, without charging (18 November 2026)

This first payment-safety batch adds a **preparation record**, not a gateway order or collected payment. Payment provider, issuer and tax treatment must be approved before live collection.

- `SubscriptionPaymentRequests` uses numeric auto-increment IDs, a unique numeric `BillingDraftId`, numeric `BusinessId`, immutable amount/currency snapshot, actor, timestamp and `RequestKey`. The request key is a retry token, **not an entity ID**.
- MFA-verified Super Admin can prepare **one** payment request for a valid service-period Billing Draft. A retry with the same key returns the existing numeric ID; a different key cannot create a duplicate.
- Historical drafts without a service period cannot be prepared. All requests have status `AwaitingGateway` with no endpoint to mark `Paid` or change a subscription to `Active`.
- The existing **Plans & Billing** page shows preparation history and an administrative action requiring a reason. No customer checkout, payment link or collection is exposed.
- Audit action `Billing.PaymentPrepared` is committed atomically. SQL migration `20261118140000_AddSubscriptionPaymentRequests` requires a reviewed backup and deployment.
- Tests cover MFA/tenant denial, invalid inputs, idempotent retry, conflicting duplicates, frozen amount, unchanged Trial subscription and SQL migration.

**Not implemented:** gateway order creation, verified webhook/payment capture, receipt or tax invoice, paid activation/renewal, refunds or revenue reporting. Never count `AwaitingGateway` as collected money.
