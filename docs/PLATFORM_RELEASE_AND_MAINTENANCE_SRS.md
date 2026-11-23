# Business SaaS — Platform Release, Business Type & Maintenance Control

**Decision:** DR-002, 22 Nov 2026. **Implementation stage (23 Nov 2026):** Functional global Business Type/Module release controls, maintenance scheduling and public notices on `development`. Scoped market/tenant maintenance, localization, support and production rollout acceptance remain pending.
**Owner:** Platform Super Admin; **location:** existing **Platform Settings** sidebar module. Keep exactly eight platform top-level modules.
**Related:** [Master Screen Register](./MASTER_SCREEN_REGISTER.md), [Approved UI/UX](./APPROVED_UI_UX.md), [Frontend-First Tracker](./FRONTEND_FIRST_DESIGN_TRACKER.md).

## Implemented global controls (23 November 2026)

- **New-business eligibility:** existing `BusinessTypes` stores `ReleaseState` and `ReleaseRevision`; only the installed `RETAIL` workflow can be Available. The MFA-protected wizard reads `GET /api/platform/provisioning-eligibility` for eligible types or a blocking reason. `POST /api/platform/businesses` applies the same type, required-module and blocking-maintenance rules inside the provisioning transaction.
- **Modules:** the compiled registry contains Catalog, Purchase, Inventory and development-only POS. `PlatformModulePolicies` stores only overrides; the authorized write middleware blocks disabled modules without deleting historical records. Stock-changing receiving posts are also covered by Inventory release state. This does not replace subscription/RBAC checks.
- **Maintenance:** `PlatformMaintenanceWindows` supports global NoticeOnly/ReadOnly/Unavailable UTC windows (maximum 24 hours, up to 30 days ahead), overlap rejection and audited cancellation. Auth, health, public status and platform recovery remain accessible; existing authorized tenants receive a 503 for affected operations.
- **Announcements:** `PlatformAnnouncements` supports a global, plain-text severity message with UTC window and audited archival. `GET /api/public/platform-availability` returns only currently active public messages. The React public banner renders text safely on page load.
- **Role boundaries:** Super Admin with completed MFA is required for changes. Changes require reasons; Type/Module state updates use revision checks. Existing store data and tenant subscriptions are unchanged by release availability.
- **Deferred:** market-specific type policies, scoped module/tenant maintenance, targeted announcements, push delivery, approvals beyond the MFA operator, production restoration exercises and manual UX sign-off. Platform Settings Languages and Notifications remain previews. These new screens are **Partial**, not DONE, until acceptance gates pass.

## 1. Intent and strict conceptual separation

The platform needs **three independently managed mechanisms**, not one vague `isMaintenance` flag.

| Mechanism | Purpose | Examples | Mutates runtime access? |
| --- | --- | --- | --- |
| **Business type availability** | Decide which business verticals can be newly provisioned and in which approved markets. | General Retail **Available**; Salon **Under Construction**; Repair **Coming Soon** | **Yes**, only after server-side policy is implemented; never controlled by React alone |
| **Module release lifecycle** | Decide whether a compiled/implemented feature can be offered to a particular vertical/version. | Inventory **Available**; Returns **Under Construction** | **Yes**, as an additional release gate on top of roles + entitlements; existing data stays intact |
| **Maintenance windows** | Temporarily announce, freeze writes or make a scoped service unavailable during updates. | Entire site read-only for schema cutover; POS maintenance notice | **Yes** if read-only/offline; clear schedule, audit and recovery required |
| **Public announcements** | Show non-blocking messages without disabling any operation. | “We are updating the site”, “New modules coming soon”, upgrade announcements | **No**; only presentation, never an authorization/feature gate |

The **marketing / public website** may remain available while API business operations are read-only or unavailable. A site-wide information banner must not erroneously turn off POS or tenant stock posting.

A fifth concept remains separate: **business account suspension/subscription**. Never reuse type availability or maintenance to suspend or delete an existing tenant. Plan/role permissions remain enforced by their own server policies.

## 2. Lifecycle statuses and visibility

The Super Admin can inspect and, later, manage the following *release states* for each supported business type and module:

| State | New business of that type | Existing business | User-facing message |
| --- | --- | --- | --- |
| `Available` | Allowed only when market, plan, server readiness and platform authorization all pass | Existing scoped features remain available if allowed by roles and plan | Normal type availability |
| `Coming Soon` | **Blocked** | No automatic deletion or change to already-provisioned data | “Coming soon” |
| `Under Construction` | **Blocked** | Keep prior supported business access; separately mark unimplemented modules and hide unsafe actions | “This business type is under development” |
| `Paused` | **Blocked** | **No blanket tenant outage**; a separate maintenance or support decision handles legacy tenants | “Temporarily unavailable for new businesses” |
| `Retired` | **Blocked** | Existing tenants require an explicit supported migration/retirement plan before service removal | “No longer offered for new businesses” |

**Business Types ≠ arbitrary uploaded software.** The platform may expose only server-known, code-registered types with tested workflows. An admin cannot make a new vertical operational by typing a label or editing a database row. `Available` is publishable only when required backend modules, schema, provisioning and E2E checks are complete.

**Market-specific policies:** Business type `RETAIL` may be available for one approved country and under construction for another; do not infer support from currency alone. The provisioning wizard uses the **same authoritative eligibility response** as the final server-side create action.

## 3. New Platform Settings screen hierarchy (no sidebar sprawl)

Existing **Platform Settings** remains one of eight main tabs. Nested tabs:

1. **General** — existing identity and region defaults.
2. **Business Types** — searchable registered type catalogue, lifecycle, approved country scope, “Can create new business?” computed label, public availability copy, review and publish.
3. **Modules & Access** — registered module family, lifecycle, eligible business types, plan-entitlement dependencies and release readiness; do not override permissions.
4. **Maintenance** — scope, mode, start/end time, customer-visible heading/body, reason, approval, schedule, active/ended/cancelled log.
5. **Announcements** — notice severity, audience (public/owner/cashier/type), localization, draft/preview, schedule, publish/unpublish/expiry, history.
6. **Languages** — publish approved language packs.
7. **Notifications** — transactional email/push like owner invitations and subscription reminders. Distinct from on-screen announcements.

**Frontend now:** `frontend/web/src/PlatformAvailabilityPreview.tsx` supplies interactive local draft/preview forms, real catalogue lookup for Business Types, illustrative module list and message/maintenance previews; **it cannot save or activate**. Source styling lives in scoped `styles.css`. No fake success buttons or guessed live maintenance states.

**Future UI:** Change actions require preview → impact summary (affected markets/types/modules/tenants) → confirm + reason → durable audited write → verified readback. Show a real affected-tenant count only when the API supplies it; do not calculate from illustration values. A countdown or “active right now” badge must use server time, not client date alone.

## 4. Minimal backend/domain model (integration phase, not present today)

Avoid a generic unbounded key/value settings engine, workflow designer, multiple services or duplicate availability flags in every module. Extend the existing ASP.NET Core API + EF Core SQL Server and platform audit infrastructure.

### A. Code-side registries — *authoritative installed capabilities*

- Use existing `BusinessTypes` seeded catalogue: stable `Code`, ID, name. Add verticals **only** with valid source/permissions/migrations/tests. The registry cannot be created through UI.
- A small typed `ModuleRegistry` in the backend identifies supported module codes, required business type/version and readiness. No request may grant execution of an unknown module.
- `Available` in policy never overrides a missing compiled capability or unsupported market.

### B. Three durable tables only when integration begins

1. **`PlatformReleasePolicies`**: primary identity; `ScopeKind` (`BusinessType`|`Module`), `ScopeCode`, optional `MarketCode`, `ReleaseState`, `MessageKey`/localized copy reference, `UpdatedAtUtc`, `UpdatedByUserId`, SQL row-version; unique scope/market key. Derived `CanProvision` is *not* a second independently editable flag.
2. **`PlatformMaintenanceWindows`**: scope (`Platform`|`BusinessType`|`Module`), scope code, `Mode` (`NoticeOnly`|`ReadOnly`|`Unavailable`), `StartUtc`, `EndUtc`, `Reason`, `NoticeId`, `Status` (`Draft`|`Scheduled`|`Cancelled`|`Ended`), actor and row-version. Require `StartUtc < EndUtc`. Avoid contradictory overlaps or compute the most restrictive mode deterministically.
3. **`PlatformAnnouncements`**: audience/scope, severity, safe plain-text localized title/body, `StartUtc`, `EndUtc`, `State` (`Draft`|`Scheduled`|`Published`|`Archived`), actor and row-version. Separate from transactional notification/email delivery; never place credentials or private tenant information in a public notice.

Use existing platform audit records for *previous/new state, actor, reason, target, time*. No fourth audit table. Query current UTC windows on reads; do not require a new scheduler service merely to expire a banner.

### C. Narrow endpoints and stable contracts (proposed)

| Route | Role | Behavior |
| --- | --- | --- |
| `GET /api/public/platform-availability` | Anonymous or session | Small, safe, localized business type/module availability & notices for the requested audience; no internal reasons/actor IDs/tenant counts |
| `GET /api/platform/release-controls` | Platform Super Admin + MFA | Registered types/modules plus published policies and versions; bounded/paginated |
| `PUT /api/platform/release-controls/{scope}/{code}` | Platform Super Admin + MFA | State transition with row-version/ETag, required reason, validation against installed registry and audited response |
| `GET/POST/PATCH /api/platform/maintenance` | Platform Super Admin + MFA | List/schedule/update/cancel scoped maintenance; validate overlapping windows and impact |
| `GET/POST/PATCH /api/platform/announcements` | Platform Super Admin + MFA | Draft, preview, publish, archive; sanitization, schedule and immutable audit |
| `GET /api/platform/provisioning-eligibility` | Platform Super Admin + MFA | Live global eligibility returns eligible types or a blocking reason. Querying policy for a specific market remains proposed. |

The implementation notes above identify the live endpoints and their current scope. `GET /api/business-types` remains available for existing callers. `POST /api/platform/businesses` checks the current global policy inside the provisioning transaction.

## 5. Enforcement and precedence

Server eligibility for new provisioning:

`Authenticated Platform Super Admin + MFA` **AND** installed/registered business type **AND** allowed market **AND** release state Available **AND** required modules ready **AND** plan/provisioning contract valid **AND** no applicable maintenance restriction **AND** idempotent request.

**Never** treat a hidden menu, client toggle, banner text or `isPlatformOwner` UI flag as server authorization. Auth and business tenant scoping remain mandatory for every API.

Maintenance precedence: **most restrictive applicable mode wins**, `Unavailable > ReadOnly > NoticeOnly`, across Platform, Business Type and Module scopes; fixed matching and time bounds. A module-specific window must not disable unrelated modules. When read-only, GET/read-only pages remain available; the backend prevents applicable writes. When unavailable, return a consistent `503` maintenance response with `Retry-After` when known, preserve health/status/auth/logout and carefully authorized recovery routes. Business-type-not-available during create is a **domain conflict**, not blanket HTTP 503; return a machine-readable reason (e.g. `BUSINESS_TYPE_NOT_AVAILABLE`).

Maintain a **safe platform recovery path**, MFA and explicitly audited emergency override; never create a hidden public bypass. Use optimistic concurrency for admin changes, immutable prior-state audit and server-side validation to prevent two operators silently overwriting each other.

If release configuration is unavailable: do **not** silently grant new business provisioning or unknown modules; keep existing established tenant reads according to previously valid access rules, and show an operator diagnostic. Until rollout, maintain the known existing provisioning behavior with safe migration sequencing—do not strand all customers by shipping a default-deny migration without approved initial policies.

## 6. Public presentation and copy

Examples are **design text only, not currently published**:

- **Business type — Under Construction:** “Salon Management is being developed. New salon workspaces are not available yet.”
- **Module — Coming Soon:** “Returns & Refunds is coming soon. Your existing sales and stock are unaffected.”
- **Site information banner:** “We’re updating Business SaaS. You can continue working normally.”
- **Scoped read-only maintenance:** “Inventory is undergoing scheduled maintenance. Viewing stock is available; stock-changing actions are temporarily paused.”
- **Temporary unavailability:** “We’re updating this service. Please try again after the scheduled maintenance window.”

Show non-blocking notices as a dismissible accessible banner (persist dismissal only until the next notice version). Show a proper full-screen `status / maintenance` view only when the **server** says the required service is unavailable. Preserve diagnostics for operator support. Never claim “backups complete”, “payment successful”, “your data is safe” or a specific restart time without verified evidence.

## 7. Engineering/release checklist

- Stable `BusinessTypeCode`, `ModuleCode`, `MarketCode`; no duplicated bool flags in components or SQL.
- Policy state machine with allowed transitions, transition reasons, audit, concurrency and safe rollback.
- Provisioning endpoint, business type selector and onboarding source eligibility all from same server implementation.
- Feature execution requires role + business/location scope + plan entitlement + installed capability + release/maintenance gate; no unplanned cross-tenant side effects.
- Existing active customers unaffected by `Coming Soon`, `Paused`, `Retired` *for new provisioning*; only explicitly scoped maintenance may suspend their operations, without deleting transactions.
- Maintenance blocks writes at API/service boundary and accounts for jobs/queues/posting retries, not solely React buttons.
- UTC time windows with explicit operator timezone; DST and clock skew tests; schedule and cancellation return verified readback.
- Plain-text safe messages; localization fallbacks, XSS/HTML injection and size-limit tests, role-specific audience, deterministic overlap precedence.
- Accessibility at 1536, 1366, 768, 360 widths; keyboard, screen-reader, severity text beyond color, no dead controls.
- Tests: public anonymous vs Super Admin, forbidden Owner/Cashier mutation, market restriction, unknown type, idempotent duplicate provisioning, overlapping maintenance, old/new policy version conflicts, read-only invoice/stock POST rejection, non-target module still available, scheduled expiry, network failure, emergency rollback.
- **Do not mark as DONE** before real-browser screenshot comparison with approved visual reference, server integration, migrations and negative E2E cases. Frontend compile alone is not design approval.

## 8. Suggested release order

1. **Now, frontend-first:** UI tab architecture, local preview and responsive styling; document and review operator-facing copy and precedence.
2. **After design freeze:** Implement typed registry/policy data model and read/public availability APIs with role-safe tests.
3. **Then:** enforce release eligibility inside Super Admin provisioning and affected module endpoints; verify legacy tenant behavior.
4. **Then:** scheduled maintenance/read-only and notices, review/confirm/audit/recovery, targeted messaging and operational runbook.
5. **Finally:** real authenticated visual tests, E2E blocked/allowed scenarios, rollout/rollback and production release.

The implemented global controls are listed at the top of this document. Scoped policies and release acceptance remain pending.
