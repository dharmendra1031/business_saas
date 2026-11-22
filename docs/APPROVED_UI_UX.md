# Business SaaS — Approved UI/UX Reference

**Visual approval:** 17 November 2026; **Final all-screens montage approved:** 21 November 2026 · **Latest workflow decision:** 21 November 2026  
**Approved by:** Product owner, in the Business SaaS project conversation  
**Status:** APPROVED VISUAL TARGET for design and future implementation; not a claim that all screens/features are implemented. **Screen ownership / order:** [Master Screen Register](./MASTER_SCREEN_REGISTER.md).  
**Source:** The four Super Admin reference screenshots supplied in that conversation, saved together in `BusinessSaaS_Approved_SuperAdmin_References.zip` for visual handoff. Refer to the exact screenshots, not a newly invented dashboard aesthetic.

## 1. Design decision — preserve the approved screenshots

The final 21 November Super Admin montage supersedes earlier palette differences: it uses **dark navy / violet-blue active navigation / white enterprise SaaS** styling. The prior four screenshots remain a structural inspiration but the final montage is the newer visual target. See [Businesses wizard visual QA](./SUPER_ADMIN_BUSINESS_VISUAL_QA.md). Future Super Admin screens should look materially like these references, including:

- Dark navy full-height left sidebar, compact icon-plus-label menu, **violet-blue active item**, Platform Owner identity at the bottom.
- Dark navy top bar with `Business SaaS / Platform Super Admin`, notification/help icons, profile dropdown.
- Very light cool-grey main canvas, white cards, light borders, restrained radius/shadow, no decorative gradients or oversized widgets.
- Compact, readable KPI cards with outline icons, real totals, subtle green/orange/red state indicators.
- Dashboard information hierarchy: KPI row, two main analytics panels, then expiring-trials table and recent platform activity.
- Blue line/area **Business Growth** chart with period controls and `Businesses / Customers` toggle; `Businesses by Plan` doughnut chart with counts/percentages.
- Searchable, filterable, paginated tables; small badges, clear row `View` actions, details panel/drawer, and consistent spacing.
- Buttons: a single prominent **violet-blue** primary action per section, bordered secondary controls, explicit cancel/confirm for risky changes.
- Clear text labels and sensible empty/loading/error states. No generic, confusing nested menus.
- Responsive desktop/tablet/mobile behavior; sidebar becomes a drawer on small screens; tables remain usable by horizontal scroll or compact cards. Keyboard and screen-reader controls stay functional.

**Styling implementation:** React + TypeScript + Bootstrap 5 + Lucide outline icons. Reuse the existing layout/styles; keep custom CSS minimal, clearly named and centralized. CBK remains a *general* read-only UX inspiration, but **these four screenshots are the approved Super Admin visual target**. Do not copy private CBK files or assets.

## 2. Canonical Super Admin navigation — eight modules

Approved on 17 November 2026. The left sidebar has **exactly eight** main modules. Each action has one authoritative location. Unimplemented modules may have clearly labeled **frontend-only design previews** for screen-flow review; no functional Save/Publish/Activate action is exposed until a secure backend exists.

| Module | One authoritative workflow | Current implementation |
| --- | --- | --- |
| Dashboard | KPIs, growth and plan charts, expiring-trial alerts, recent activity, links to business details | Working overview with live data |
| Customers | Customer search/details, verification and owned-business references | Read-only directory |
| Businesses | Business directory/details, audited trial extensions and suspend/reactivate | Working core controls |
| Plans & Billing | Plan drafts, immutable catalog publication, future billing, invoices and payments | Plan controls working; billing unavailable |
| Licenses | Offline license lifecycle and history | Navigable **frontend design preview only**; no real issuance or activation API |
| Platform Settings | Business type release availability, module lifecycle, scoped maintenance, public announcements, regions, languages | Navigable **frontend design preview only**; no live policy/maintenance publishing API yet |
| Security & Audit | MFA, sign-in audit, administrative change history | Working foundation |
| Operations | API/database readiness, future monitoring, backup/restore and support | Readiness working; other controls planned |

There are **no separate top-level** Trials, Subscriptions, Payments, Invoices, Modules, Support or System Health screens. Trials and subscription statuses are managed in Business Details, catalog/prices in Plans & Billing, licensing in Licenses, entitlement policies under Platform Settings, and monitoring/backups/support under Operations.

Dashboard alerts only navigate to the existing business or audit details; Customers can link to owned businesses but cannot change operational records. Prices, payments, backup success, system health and support tickets must never use fabricated data.

**Security boundary:** Super Admin controls the platform—not a shop's products, stock, sales or returns. Sensitive writes require MFA-authorized server checks, an explicit reason and transactional audit.

## 2A. Platform release & maintenance management — owner decision DR-002 (22 November 2026)

The user requested centralized, long-lived controls for which business types may be created, which types/modules are **Under Construction / Coming Soon**, when the platform is under maintenance and what customer-facing update banners are displayed. This is **part of Platform Settings**, not another top-level sidebar item. Keep **exactly eight** Platform Super Admin modules.

Nested design tabs: Business Types, Modules & Access, **Maintenance**, **Announcements** (on-screen banners), General, Languages, Notifications (transactional emails). Separate **Business Type availability**, **Module release status**, **maintenance availability**, and **notice publication**. A banner must never accidentally disable a real module, and pausing new provisioned types must not disable existing tenants. The Super Admin must have a review, impact summary, reason, confirmed publish, audit history and safe rollback.

The current `PlatformAvailabilityPreview.tsx` only shows interactive, *unsaved local drafts*; the business-type list is obtained from the existing catalogue API and no availability policy is yet supplied. No settings or maintenance window are live. See [Platform Release & Maintenance SRS](./PLATFORM_RELEASE_AND_MAINTENANCE_SRS.md) for stable states, server-side authority, role/market/plan prerequisites, message scheduling and safety tests.

## 3. Approved Overview structure (match reference images)

**Header:** `Platform Overview` + a concise subtitle; date scope + `Refresh` at right.

**First row:** `Total Customers`, `Total Businesses`, `Active Subscriptions`, `Active Trials`, `Expired Trials`; `Monthly Revenue` may appear as the sixth KPI or its own card depending on space. Trend arrows/percentage only when genuine historical data is available.

**Middle row:**
- `Business Growth`: real counts over time, optional Customers/Businesses switch.
- `Businesses by Subscription Plan`: doughnut chart and plan breakdown by Starter/Growth/Professional/Business/Enterprise.
- `Monthly Revenue`: display **Not available** until a verified subscription payment ledger exists; do not invent amounts or hardcode ₹0.

**Bottom row:**
- `Trials Expiring Soon`: businesses within a real configurable horizon (initial UX: next seven days), Business ID, name, customer, type, trial end, days remaining, status and `View`. `Extend Trial` only after its audited backend flow is implemented.
- `Recent Platform Activity`: read-only records from real platform audit data, with timestamp, action, actor and affected entity; show a truthful empty state if none.

**No sample data in live UI:** All numbers, chart dates, status labels and customer emails shown in the screenshots are mockup examples, **not actual platform data**. Do not repeat 104/267/312/128 or historical 2025 dates as real values.

## 4. Customers, Businesses, Plans, and sensitive actions

**Customers:** compact search/filter toolbar, verification badge, account state, count of owned businesses, one `View` action, server pagination, separate customer details panel with owned businesses. A future `Suspend/Reactivate` action must show the exact scope and impact, request a reason, require confirmation and write an audit record.

**Businesses:** same list pattern; owner/customer email, business type, country, current plan, subscription status. Details include business identifier, owner, plan and trial expiry. The Super Admin cannot enter a shop to edit its operational data.

**Plans:** clear plan summary cards and/or dense plan table: name, monthly/yearly pricing as configured, applicable modules, user/business/location/product limits, trial/grace rules, and status. The screenshots' ₹499/₹999/etc. are illustrative—not approved prices. Changing a plan must not silently modify historical subscribed terms.

**Trials:** searchable expiring-trials list, `Extend Trial` action with a small focused modal, required reason, before/after expiry preview, confirmation, server-side permission checks and *transactional* audit. Plan change, suspension/reactivation and refunds follow the same guardrails.

**Payments, License, Support and Monitoring:** preserve the same layout and interaction style when actually built. No fake activity, healthy-status badges, fake payments, fabricated support tickets or nonfunctional buttons.

## 5. Business Owner / Customer portal under the same visual system

Customer accounts may own multiple independent businesses. Their experience should match the approved blue/white design language but **must not reuse Super Admin control menus**:

`Business Owner/Admin sign-in → My Assigned Businesses → Select/Switch Business → Business Dashboard → Products / Purchase / Stock / Staff → Subscription / Settings`.

**Latest product decision (21 Nov 2026):** Business creation is **exclusively** a Platform Super Admin operation, under `Businesses → Create Business → Assign Owner → Plan/Location → Review/Provision`. Owner/Admin cannot create business from this portal. This supersedes the former self-service creation text and requires backend authorization changes, not just hiding a button. See `AUTH-*`, `SA-04…SA-08` and `BUS-*` in the screen register.

**Super Admin** business creation should remain a short progressive wizard; Business Owner switching must clear old BusinessId context and permissions. Role-appropriate operational menus and status badges must remain clear. Owner/Manager/Cashier actions differ. Retail POS gets a dedicated fast checkout layout instead of an admin dashboard grid; stock/receiving is an operational flow, not platform administration. Salon/Repair remain future modules until implemented and verified.

## 6. UX acceptance and implementation constraints

- Check visual fidelity against the approved screenshots at desktop ~1536×864 and 1366×768; test tablet and small Android-phone widths.
- Keep nav labels, card proportions, chart placements and relative density consistent; apply the **explicitly approved final montage's scoped violet-blue Platform accent**. Business Admin and approved POS keep independent workspace palettes; do not redesign other layouts without approval.
- Only real API/SQL counts, dates, plan limits, payment totals and audit history should reach production screens.
- Use neutral loading skeletons/spinners, useful empty states, inline validation, no silent errors; searchable tables paginate server-side.
- Confirm sensitive edits with affected entity, prior/new value, clear reason, explicit action button, safe retries and audited server transaction.
- Follow accessibility: contrast, keyboard focus, labels, logical tab order, mobile touch target sizes; design for multilingual LTR/RTL.
- Keep a single .NET API + one React app, numeric auto-increment entity IDs (no UUID/GUID), strict tenant isolation and no excessive new CSS/components.
- Existing code is not automatically `DONE` because a design screenshot displays a feature. Every planned control needs real API + UI + tests and honest state labels.

## 7. Implementation order

1. Preserve existing Overview/Customers/Businesses/Security baseline. Match its look to the approved reference.
2. Add real, tested `Business Growth`, `Businesses by Plan`, `Expiring Trials`, `Recent Activity` panels.
3. Add read-only plan list/plan details before edit flows; then plan CRUD with validation and subscription impact safeguards.
4. Add audited trial extension and activation/suspension/reactivation flows; only then expose relevant UI buttons.
5. Subscription ledger, payments/invoices, licenses, modules, settings, support, monitoring and enterprise capabilities by product priority, with tests.

**Source of truth:** This document defines the approved Super Admin visual direction and exactly eight top-level modules. The **21 Nov** final concept montage is the latest approved visual reference; illustrative standalone Reports, Payments, Subscriptions, Users and Announcements panels belong under those eight modules rather than becoming duplicated top-level menus. The [Master Screen Register](./MASTER_SCREEN_REGISTER.md) defines all role-separated screen IDs, current progress, P0 conflicts and completion requirements. Never replace the approved look without another explicit decision.
