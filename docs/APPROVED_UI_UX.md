# Business SaaS — Approved UI/UX Reference

**Decision date:** 17 November 2026  
**Approved by:** Product owner, in the Business SaaS project conversation  
**Status:** APPROVED VISUAL TARGET for design and future implementation; not a claim that all screens/features are implemented  
**Source:** The four Super Admin reference screenshots supplied in that conversation, saved together in `BusinessSaaS_Approved_SuperAdmin_References.zip` for visual handoff. Refer to the exact screenshots, not a newly invented dashboard aesthetic.

## 1. Design decision — preserve the approved screenshots

The four reference screenshots show the approved **dark-navy / royal-blue / white enterprise SaaS** visual language. Future Super Admin screens should look materially like these references, including:

- Dark navy full-height left sidebar, compact icon-plus-label menu, saturated blue active item, Platform Owner profile at the bottom.
- Dark navy top bar with `Business SaaS / Platform Super Admin`, notification/help icons, profile dropdown.
- Very light cool-grey main canvas, white cards, light borders, restrained radius/shadow, no decorative gradients or oversized widgets.
- Compact, readable KPI cards with outline icons, real totals, subtle green/orange/red state indicators.
- Dashboard information hierarchy: KPI row, two main analytics panels, then expiring-trials table and recent platform activity.
- Blue line/area **Business Growth** chart with period controls and `Businesses / Customers` toggle; `Businesses by Plan` doughnut chart with counts/percentages.
- Searchable, filterable, paginated tables; small badges, clear row `View` actions, details panel/drawer, and consistent spacing.
- Buttons: a single prominent blue primary action per section, bordered secondary controls, explicit cancel/confirm for risky changes.
- Clear text labels and sensible empty/loading/error states. No generic, confusing nested menus.
- Responsive desktop/tablet/mobile behavior; sidebar becomes a drawer on small screens; tables remain usable by horizontal scroll or compact cards. Keyboard and screen-reader controls stay functional.

**Styling implementation:** React + TypeScript + Bootstrap 5 + Lucide outline icons. Reuse the existing layout/styles; keep custom CSS minimal, clearly named and centralized. CBK remains a *general* read-only UX inspiration, but **these four screenshots are the approved Super Admin visual target**. Do not copy private CBK files or assets.

## 2. Canonical Super Admin navigation

Present the following sections in this order. Planned sections must be labeled as unavailable/planned or hidden until working end-to-end; do not show clickable dead actions.

| Section | Expected workflow | Current baseline (17 Nov 2026) |
| --- | --- | --- |
| Overview | KPI cards, Business Growth, Businesses by Plan, trials expiring soon, recent activity, refresh/date scope | Summary KPI API/screen implemented; chart and expiring list planned |
| Customers | Search email, verification/status filters, customer detail, all businesses owned | Basic read-only search/detail implemented; suspension/verification workflows planned |
| Businesses | Business directory, owner/type/country/plan/status, read-only business detail | Basic read-only directory implemented |
| Subscription Plans | Starter, Growth, Professional, Business, Enterprise; price, included features/modules, limits, create/edit/retire, plan version impacts | Planned |
| Trials & Subscriptions | Trial list, status/expiry, extend trial with reason, assign/change plan, activate/renew/suspend/reactivate | Planned; initial trial state already exists |
| Payments & Invoices | Platform subscription payments, paid/unpaid status, tax invoices, refunds and reconciliation | Planned |
| License Management | Offline license create, activate, renew, revoke, transfer, history | Planned |
| Module Management | Retail availability and future Salon/Repair eligibility per plan | Planned |
| Platform Settings | Default trial rules, business types, country and account policies, language support | Planned |
| Security & Audit Logs | MFA status, login audit events, administrative action history | Owner MFA and early read-only audit implemented |
| System Health & Backups | API/DB readiness, error alerts, backup status and restore verification | Planned; basic health endpoints may exist but not the full screen |
| Support | Customer tickets, status, issue history and bounded troubleshooting | Planned |

**Security boundary:** Super Admin controls *the platform*—customers, subscriptions, licensing, settings—not customer shop operational records (products, stock, sales, returns). All server actions require owner authorization, MFA for privileged access, and audit when making sensitive changes.

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

`Account sign-in → My Businesses → Create / Switch Business → Business Dashboard → Products / Purchase / Stock / Staff → Subscription / Settings`.

Business creation should remain a short progressive wizard; switching must clear old BusinessId context and permissions. Role-appropriate operational menus and status badges must remain clear. Owner/Manager/Cashier actions differ. Retail POS gets a dedicated fast checkout layout instead of an admin dashboard grid; stock/receiving is an operational flow, not platform administration. Salon/Repair remain future modules until implemented and verified.

## 6. UX acceptance and implementation constraints

- Check visual fidelity against the approved screenshots at desktop ~1536×864 and 1366×768; test tablet and small Android-phone widths.
- Keep nav labels, card proportions, chart placements and relative density consistent; don't introduce a new purple-first theme or redesign layout without a new explicit decision.
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

**Source of truth:** This document records the user's approved screenshots and requirements. If a later design conflicts with the four supplied reference images, ask for or record an explicit design decision before replacing the agreed direction.
