# Business SaaS — MASTER SCREEN REGISTER & ONE-SCREEN-AT-A-TIME TRACKER

> **Canonical screen inventory v1.0 — 21 November 2026.**
> Target: `dharmendra1031/business_saas`, **`development` branch only**.
> This is a **planning, UX-acceptance and traceability register**. No entry marked **DONE**; source components, API handlers and passing CI alone do not prove real browser/production acceptance.
> This document is the single reference for **which screen belongs to which workspace, its purpose, its flow, current observed implementation and the required quality gate**. Keep screen IDs stable while development progresses.

## Frontend-first design phase (owner update: 22 Nov 2026)

The owner approved designing and visually accepting all released screens **before building their missing backend features**. The per-screen source of truth is still this stable 145-item register; the separate [Frontend-First Design & Visual QA Tracker](./FRONTEND_FIRST_DESIGN_TRACKER.md) tracks frontend-only visual implementation versus API-integrated readiness.

**Iteration 1 committed on `development`:** `SA-01` Dashboard visual hierarchy, `SA-03` Businesses directory and fully navigable eight-module platform shell. Existing backend APIs supply actual dashboard/business records; no backend changes were made. `SA-29..39` Licenses and Platform Settings now have explicitly marked **frontend-only design previews** with working tab navigation but **no live save or license issuance**. These screens remain **Partial/Missing for full product release**, not `DONE`.

**Visual QA remaining:** Compare real authenticated browser captures at 1536×864, 1366×768, 768px and 360px to approved reference images. A matching CSS source and green CI build do **not** certify pixel-level fidelity or owner approval.

### Iteration 3 — Business Owner/Admin frontend batch (22 November 2026)

- `BUS-01` Dashboard: `BusinessOverview.tsx` now implements an enterprise blue/navy role-specific layout with location count, server `hasProducts` state, real owner draft-PO count, subscription status, working management-module action cards, setup guidance and location list.
- `AUTH-11` / `BUS-03` business selection: enhanced assigned-business cards with role/market identity; only Super Admin may create a tenant.
- `BUS-06..08`, `BUS-10..15`, `BUS-17..18`, `BUS-20..25`, `BUS-27..32`: products, categories/units, contacts, purchases and inventory received shared enterprise headers, data tables and responsive styling. Existing real APIs, stock posting and approvals remain unchanged.
- `BUS-35..47`, `BUS-53..55`: **design-preview UI only**, not working sales, returns, register, reports or settings; `BusinessPreview.tsx` makes tab flows visually inspectable with explicit no-backend messaging and no fictitious records, payments or totals.
- Business Owner/Admin and Cashier navigation are separated further: with checkout enabled a Cashier no longer sees Business Dashboard in the sidebar. The full-width approved POS shell is still a separate unfinished frontend milestone.
- **No screen becomes DONE** from this source implementation or passing build alone. Exact image comparison requires authenticated browser screenshots at 1536×864, 1366×768, 768px and 360px and owner sign-off. See [Frontend Design Tracker](./FRONTEND_FIRST_DESIGN_TRACKER.md).

## 0. Non-negotiable product decision and conflict resolution

**Decision DR-001 (latest owner direction, 21 Nov 2026):** Only **Platform Super Admin** creates/provisions a new business and assigns its Business Owner; the Business Owner/Admin logs into a separate business workspace and manages only authorized business operations. A POS/Cashier has a separate login *experience* and dedicated checkout workspace.

- This supersedes the **earlier revisions** of `BUSINESS_SAAS_MASTER_SRS.md` §4 and `APPROVED_UI_UX.md` §5 that allowed owner self-service creation. Those documents have now been textually aligned with this decision; **application code and access-control tests are still pending**. Do not reinstate the superseded self-service path.
- **Current implementation (22 Nov):** The Owner-facing `Create Business` UI was removed; `POST /api/businesses` is now forbidden by default. Only legacy **Development integration fixtures** may opt into that route using `Tests:AllowLegacyBusinessCreation`; remove this test-compatibility path when fixtures migrate. Platform MFA-only `POST /api/platform/businesses` creates a Starter trial business with an existing verified Owner, first location, audit and idempotency. **P0 gaps**: email verification/invitation delivery, wider plan/entitlement selection, browser UX acceptance and retired fixture compatibility.
- Super Admin manages SaaS-level businesses, customer accounts, plans/billing, licensing, settings, security and operations **but never edits a tenant's stock, retail sales or POS transactions**. "Only Super Admin creates business" means **exclusive business creation permission**, not removal of all other platform-management functions.
- Login entry pages are visually separate for Super Admin, Owner/Admin and Cashier, with **one underlying secure authentication system** and server-verified roles/MFA. A role picker does not grant permissions. One ASP.NET API, one React frontend, one underlying business/tenant authorization model; **no duplicate applications or over-engineering**.
- **Super Admin has exactly eight canonical main sidebar modules**: **Dashboard, Customers, Businesses, Plans & Billing, Licenses, Platform Settings, Security & Audit, Operations**. The mockups' standalone Users, Subscriptions, Payments, Invoices, Reports, Announcements and System Health are **sub-screens/tabs within these modules**, NOT extra top-level navigation. `Customers` refers to SaaS account owners, while `Business Customers` are that shop's retail customers.
- Supported Retail V1 first. **Salon, Repair, custom branding, dedicated DB and offline licensing are deferred** until separately scoped and verifiable; do not expose their placeholder controls as working.
- No invented prices, metrics, customers, stock, tax, payment methods or receipt numbers. Development-only POS endpoints are **not production-ready**.

## 1. Approved visual source-of-truth

| Workspace | Approved/reference treatment | Actual source |
| --- | --- | --- |
| **Super Admin** | Dark-navy/royal-blue/white enterprise interface; approved screen designs from the conversation, clean sidebar + dense real-data tables; use eight-module IA | `docs/APPROVED_UI_UX.md`; final Super Admin concept montage in the project conversation is **visual reference**, not implemented UI or a verified GitHub image artifact |
| **Business Owner/Admin** | Separate premium business-management dashboard, current business/location identity, operations/stock/purchases/staff; no platform menus | Project's business dashboard concept as directional reference; detailed UX sign-off per screen |
| **POS/Cashier** | **Explicitly approved** Ibdaa-inspired navy/indigo POS, **full width, no permanent sidebar**, **61% catalogue / 39% basket**, **Payment Popup** | `docs/APPROVED_POS_UI_UX.md`; original approved image in project conversation; `ibdaa-atelier-pos` development reference is read-only |
| **Auth & shared** | Distinct role entry/login look; consistent design tokens/validation and role-aware navigation | `docs/UX_STANDARDS.md`, `docs/BUSINESS_SAAS_MASTER_SRS.md` |

The mockup pictures display illustrative controls (Card, UPI, Split, Services, discount, coupons, reports, screenshots of sample paid invoices). **Only expose them when corresponding real APIs, permissions and transaction rules exist.**

## 2. Status / priority definitions

- **Partial** = some matching current UI/backend code exists, but UX, role separation, complete flow or screen acceptance is **not verified**. It does **not** mean functional completion.
- **Missing** = no independently usable end-to-end screen/feature verified in the inspected `development` source.
- **Deferred** = documented potential or approved future area, **not in current Retail V1 delivery**.
- **DONE** = only after UI fidelity, correct API/business behavior, role & tenant authorization, automated tests, real browser states, mobile layouts, and owner sign-off all pass; **currently zero screens are certified DONE in this register**.
- **P0** = fix before the next credible user journey; **P1** = finish Retail V1; **P2** = later platform/retail enhancement; **P3** = deferred future scope.
- **Page / Form / Tab / Modal / Wizard step / Panel / State / Flow / Family** describe UI work items. **These IDs do not require one React file or route per row**. Prefer inline forms/panels where usable; no code duplication.
- Per-screen status reflects **source-code review on 21 Nov 2026**, not browser screenshots, staging QA or a feature-complete claim.

## 3. Complete screen inventory (145 tracked screens, sub-screens, states, dialogs, and future families)

| Group | Count |
| --- | ---: |
| Entry, authentication and cross-workspace navigation | 13 |
| Platform Super Admin | 47 |
| Business Owner / Admin / Location Manager | 55 |
| Cashier / POS | 18 |
| Deferred product families | 12 |
| **TOTAL** | **145** |

| Current evidence status | Count |
| --- | ---: |
| Missing | 61 |
| Partial | 70 |
| Deferred | 14 |

### A. Entry, authentication and cross-workspace navigation

**Who sees this:** Public / authenticated role-aware.

| ID | Screen / interaction | Type | Current | Priority | Required outcome / guardrail |
| --- | --- | --- | --- | --- | --- |
| AUTH-01 | Workspace entry / role gateway | Page | Partial | P0 | Gateway screen and distinct login links implemented; server validates roles. Browser UX/visual acceptance pending. |
| AUTH-02 | Super Admin sign-in | Page | Partial | P0 | Premium platform-specific login; platform owner + MFA; never enter shop operations. |
| AUTH-03 | Business Owner / Manager sign-in | Page | Partial | P0 | Separate branded entry; open authorized business dashboard, no super-admin controls. |
| AUTH-04 | Cashier sign-in | Page | Partial | P0 | Separate compact entry; open assigned location's POS, no admin sidebar. |
| AUTH-05 | Platform MFA setup and recovery codes | Page | Partial | P0 | MFA enrolment verified against server; securely handle recovery codes. |
| AUTH-06 | MFA verification challenge | State | Partial | P0 | Recoverable invalid/expired code and attempt limits; no bypass. |
| AUTH-07 | Business Owner invitation / activation | Flow | Missing | P0 | Super Admin assigns an existing verified account or sends expiring owner invitation. |
| AUTH-08 | Invitation acceptance / join business | Page | Missing | P1 | Display exact business, role, expiry and acceptance consequences; no silent duplicate account. |
| AUTH-09 | Password recovery / secure reset | Page | Missing | P1 | Generic response to prevent enumeration, expiring token, existing-session safety. |
| AUTH-10 | Email/phone verification | Page | Missing | P1 | Verified contact before privileged activation; no fake verified flag. |
| AUTH-11 | My Businesses / secure switcher | Page | Partial | P0 | Only authorized memberships; switching clears old tenant context; NO Create Business for owners. |
| AUTH-12 | Session expired / forbidden / unavailable | State | Partial | P0 | Separate expiry, permission denial and outage actions; no cross-role menu leakage. |
| AUTH-13 | My Profile / credential settings | Page | Missing | P2 | Account preferences and password changes, never global role editing. |

### B. Platform Super Admin — exactly eight TOP-LEVEL modules

**Who sees this:** Platform Super Admin only, separate platform shell.

| ID | Screen / interaction | Type | Current | Priority | Required outcome / guardrail |
| --- | --- | --- | --- | --- | --- |
| SA-01 | Dashboard — Platform Overview | Page | Partial | P0 | Live KPIs, business growth, plans, expiring trials, recent audited activity; no fake revenue. |
| SA-02 | Dashboard — report drill-down | Panel | Missing | P2 | Date filters and verified platform aggregate detail, NOT another top-level Reports menu. |
| SA-03 | Businesses — directory | Page | Partial | P0 | Search/filter/paginate by name, owner, type, status and plan; one Create Business action. |
| SA-04 | Businesses — Create Business: identity | Wizard step | Partial | P0 | Enter name, business type, country, timezone, currency; supported market only. |
| SA-05 | Businesses — Create Business: owner assignment | Wizard step | Partial | P0 | Find verified user or issue owner invite; business creator is platform, owner is assigned account. |
| SA-06 | Businesses — Create Business: plan and starting location | Wizard step | Partial | P0 | Select published compatible plan/trial and first location; visible entitlements and expiry. |
| SA-07 | Businesses — Create Business: review / provision | Wizard step | Partial | P0 | Validate everything, confirm once, idempotent business/location/membership/subscription creation. |
| SA-08 | Businesses — creation result / provisioning retry | State | Partial | P0 | Show numeric BusinessId, owner invite outcome, retry unknown outcomes without duplicate tenant. |
| SA-09 | Businesses — business details | Page | Partial | P0 | Owner, country, locations summary, trial/subscription, status and audited platform actions; no stock editing. |
| SA-10 | Businesses — extend trial | Modal | Partial | P1 | Expiry before/after, reason, MFA/server permission, confirmed audit ID. |
| SA-11 | Businesses — suspend/reactivate | Modal | Partial | P1 | Scope and impact explained; reason, confirmation and audit; no silent rollback. |
| SA-12 | Businesses — owner reassignment | Modal | Missing | P2 | Explicit ownership proof, safe permission handoff, prior-owner handling and audit. |
| SA-13 | Businesses — subscription status detail | Tab | Partial | P1 | Trial/active/expired state, period, plan version and permitted platform actions; not POS payments. |
| SA-14 | Customers — account directory | Page | Partial | P1 | Read-only user/customer directory with verified filters and owned-business counts. |
| SA-15 | Customers — customer detail | Page | Partial | P1 | Identity/verification and owned businesses; no shop records. |
| SA-16 | Customers — account restriction review | Modal | Missing | P2 | If approved: explicit account-wide impact, reason, audited permission checks. |
| SA-17 | Plans & Billing — plans catalogue | Tab | Partial | P1 | Published plan details, monthly/yearly pricing and compatible module/limit rules from real data. |
| SA-18 | Plans & Billing — plan details/versions | Page | Partial | P1 | Compare draft and published immutable versions; historical subscription terms unchanged. |
| SA-19 | Plans & Billing — edit plan draft | Form | Partial | P1 | Validated plan limits/prices/module compatibility, staged changes only. |
| SA-20 | Plans & Billing — publish plan | Modal | Partial | P1 | Preview old/new terms and confirm atomic publish with auditing. |
| SA-21 | Plans & Billing — subscription directory | Tab | Missing | P2 | Subscription records per business; not a new top-level navigation module. |
| SA-22 | Plans & Billing — billing draft list | Tab | Partial | P1 | Server-paginated genuine drafts and status; no fabricated invoice list. |
| SA-23 | Plans & Billing — billing preview/create | Form | Partial | P1 | Business, cycle, period, tax/currency precision, preview before draft. |
| SA-24 | Plans & Billing — payment request preparation | Form | Partial | P1 | Attach draft, authorized reason, audit trail; never imply payment captured by preparation. |
| SA-25 | Plans & Billing — reconciliation queue | Tab | Partial | P1 | Unknown provider outcomes checked with verified provider/order reference; no double billing. |
| SA-26 | Plans & Billing — reconciliation confirmation | Form | Partial | P1 | Reasoned verified reconciliation; idempotent and audited backend. |
| SA-27 | Plans & Billing — issued invoice history/detail | Tab | Missing | P2 | Only legally issued real documents; separate draft from final invoice. |
| SA-28 | Plans & Billing — refund review | Modal | Missing | P2 | Provider settlement verification, eligible amount, reason and second-charge safeguards. |
| SA-29 | Licenses — license list | Page | Missing | P2 | Hosted platform's offline/on-prem licenses with search/status; do not mix with subscriptions. |
| SA-30 | Licenses — issue / activation import | Wizard | Missing | P2 | Signed license issuance or activation, machine/contract checks and audit. |
| SA-31 | Licenses — renew / transfer / revoke | Modal | Missing | P2 | Lifecycle transitions and historical license validity, permissions and audit. |
| SA-32 | Licenses — license history | Tab | Missing | P2 | Issuer, machine, version, event trail from real data. |
| SA-33 | Platform Settings — landing / policy summary | Page | Missing | P2 | Distinct global settings hub; never tenant retail settings. |
| SA-34 | Platform Settings — supported business types | Tab | Missing | P2 | Publish supported templates only; unsupported types clearly marked. |
| SA-35 | Platform Settings — entitlements / modules | Tab | Missing | P2 | Global plan/module catalog with versions and access constraints. |
| SA-36 | Platform Settings — countries, currency, timezones | Tab | Missing | P2 | Approved market configuration; avoid changing legal financial rules silently. |
| SA-37 | Platform Settings — trial and grace policy | Tab | Missing | P2 | Global policy versions and effective dates, not retroactive silent edits. |
| SA-38 | Platform Settings — languages / translations | Tab | Missing | P2 | Upload, preview, validate and publish permitted language packs; RTL tests. |
| SA-39 | Platform Settings — branding / platform identity | Tab | Missing | P2 | Approved global logo/theme and communication templates; no unverified logos. |
| SA-40 | Security & Audit — overview | Page | Partial | P1 | Platform MFA status and safe overview; no blanket tenant access. |
| SA-41 | Security & Audit — admin actions history | Tab | Partial | P1 | Time/actor/entity/action and old/new values from real audited operations. |
| SA-42 | Security & Audit — sign-in/access history | Tab | Missing | P2 | Login failures and elevated access signals with PII-minimized logs. |
| SA-43 | Operations — API & database readiness | Page | Partial | P1 | Only actual readiness endpoint evidence; no invented uptime. |
| SA-44 | Operations — monitoring / incidents | Tab | Missing | P2 | Genuine health signals, retry and responsible follow-up. |
| SA-45 | Operations — backup and restore verification | Tab | Missing | P2 | Last verified restoration, recovery evidence and protected operator actions. |
| SA-46 | Operations — support and announcements | Tab | Missing | P2 | Support issues and platform messages when backend exists; not standalone top-level menus. |
| SA-47 | Operations — tenant infrastructure / data moves | Tab | Missing | P3 | Approved dedicated SQL provisioning/migration and source/destination verification for Enterprise. |

### C. Business Owner / Admin / Location Manager — Retail V1

**Who sees this:** Business-specific shell; Owner/Manager scope and location policy.

| ID | Screen / interaction | Type | Current | Priority | Required outcome / guardrail |
| --- | --- | --- | --- | --- | --- |
| BUS-01 | Business Dashboard | Page | Partial | P0 | Real business/location sales, stock alerts, pending POs, quick actions and no platform KPIs. |
| BUS-02 | First-run business setup checklist | Page | Missing | P0 | Provisioned business readiness: first location, products, opening stock, receipt settings; resume safely. |
| BUS-03 | Business selector / context switch | Page | Partial | P0 | Membership-filtered existing businesses, clear active role/location; owners cannot create tenants. |
| BUS-04 | Locations — list and active location | Page | Partial | P1 | Only authorized locations; branch status and current scope clear. |
| BUS-05 | Locations — add / edit | Form | Missing | P1 | Permission and entitlement limits; no unauthorized branch access. |
| BUS-06 | Products — list / search | Page | Partial | P0 | Server paging, SKU/name filters, categories, active status, real pricing. |
| BUS-07 | Products — create | Form | Partial | P0 | Name, SKU/barcode, category, unit, sale price, tax classification, validation. |
| BUS-08 | Products — edit | Form | Partial | P1 | Prefill, meaningful update, no stock reset; protected duplicate-SKU checks. |
| BUS-09 | Products — detail | Panel | Missing | P2 | Product identity, current status and inventory links; no fabricated movement history. |
| BUS-10 | Categories — list | Tab | Partial | P1 | Real business-scoped categories and search. |
| BUS-11 | Categories — create/edit | Form | Partial | P1 | Prevent duplicates and invalid reassignment; preserve product relationships. |
| BUS-12 | Units — list | Tab | Partial | P1 | Real business-scoped stock units. |
| BUS-13 | Units — create/edit | Form | Partial | P1 | Stable unit identity; conversions only after separate approved rule. |
| BUS-14 | Customers — list | Page | Partial | P1 | Business retail customers (not platform customer accounts); search and pagination. |
| BUS-15 | Customers — create/edit | Form | Partial | P1 | Tenant scope, identity dedupe and validation. |
| BUS-16 | Customers — detail / purchase history | Panel | Missing | P2 | Only authorized customer sales/returns. |
| BUS-17 | Suppliers — list | Page | Partial | P1 | Supplier contact/search/pagination for this business. |
| BUS-18 | Suppliers — create/edit | Form | Partial | P1 | Business-scoped data, duplicate handling. |
| BUS-19 | Suppliers — details and purchase history | Panel | Missing | P2 | Only related purchase documents; no imaginary account balance. |
| BUS-20 | Purchases — purchase order list | Page | Partial | P1 | Draft/pending/approved/partially received/closed states, filters and totals. |
| BUS-21 | Purchases — create purchase order | Form | Partial | P1 | Supplier, location, products, unit costs and quantities; save does not post stock. |
| BUS-22 | Purchases — PO details / approval | Page | Partial | P1 | Ordered vs received vs remaining; role-checked approval with reason where required. |
| BUS-23 | Purchases — goods receipt draft | Form | Partial | P1 | Receive against remaining quantities; reject over-receive. |
| BUS-24 | Purchases — receipt review/post | State | Partial | P1 | Explicit post action updates stock exactly once, retry-safe. |
| BUS-25 | Purchases — receiving history | Tab | Partial | P1 | Real posted/pending receipts and outstanding balance. |
| BUS-26 | Purchases — reversal / correction | Form | Missing | P2 | Compensating transaction with actor/reason; no silent stock overwrite. |
| BUS-27 | Inventory — stock by location | Page | Partial | P1 | Branch-specific available stock, product search, accurate movement-backed quantity. |
| BUS-28 | Inventory — stock movement ledger | Tab | Partial | P1 | Origin, actor, time and quantity delta; correct tenant/branch filter. |
| BUS-29 | Inventory — opening stock | Form | Partial | P1 | Authorized one-time posting and duplicate prevention. |
| BUS-30 | Inventory — adjustment request | Form | Partial | P1 | Reason, current balance, proposed variance and negative-stock safeguards. |
| BUS-31 | Inventory — approval queue | Tab | Partial | P1 | Location/role-specific approvals, no unauthorized posting. |
| BUS-32 | Inventory — adjustment detail/history | Panel | Partial | P1 | Full before/after and posting outcome with audit. |
| BUS-33 | Inventory — low stock / reorder | Page | Missing | P2 | Configured reorder points and real counts, no invented thresholds. |
| BUS-34 | Inventory — branch transfer | Flow | Deferred | P3 | Optional V1: source issue, in-transit, destination receive and audit. |
| BUS-35 | Sales — posted invoice list | Page | Missing | P1 | Business and location access, search and genuine posted receipts. |
| BUS-36 | Sales — invoice detail / reprint | Page | Partial | P1 | Receipt from persisted immutable sale; reprint never reposts. |
| BUS-37 | Sales — return / refund list | Page | Missing | P1 | Search original sale and return status without over-refund. |
| BUS-38 | Sales — return eligibility and lines | Form | Missing | P1 | Original quantities, already returned, reason and allowed amount; immutable history. |
| BUS-39 | Sales — return/refund confirmation | Modal | Missing | P1 | Role, stock reversal and settlement verification, idempotency. |
| BUS-40 | Sales — exchange | Flow | Missing | P2 | Linked return + new sale; difference explicit, no editing posted invoice. |
| BUS-41 | Cash Register — sessions and drawer summary | Page | Missing | P1 | Cashier/location sessions, posted movements and opening float. |
| BUS-42 | Cash Register — variance approval | Modal | Missing | P1 | Counted vs expected, approval threshold, reason and immutable close. |
| BUS-43 | Reports — reports hub | Page | Missing | P1 | Choose sales, stock, purchasing or cash report; business date/branch control. |
| BUS-44 | Reports — daily sales | Page | Missing | P1 | Gross, discounts, tax, refunds, net and payments reconciled to ledger. |
| BUS-45 | Reports — stock balances/movements | Page | Missing | P1 | Current balances from movement ledger, valuation only when approved. |
| BUS-46 | Reports — purchase/receiving | Page | Missing | P2 | Ordered vs actually received and outstanding. |
| BUS-47 | Reports — cash and register | Page | Missing | P2 | Tender, refunds and variance by authorized shift, no SaaS revenue leakage. |
| BUS-48 | Staff & Access — team list | Page | Partial | P1 | Business memberships and roles from server, not platform accounts. |
| BUS-49 | Staff & Access — invite/add/revoke | Form | Partial | P1 | Verified identity/eligibility, audit, scoped role and safe revocation. |
| BUS-50 | Staff & Access — location assignment | Form | Missing | P1 | Manager/cashier access only to assigned location(s). |
| BUS-51 | Subscription — status and entitlements | Page | Partial | P1 | Plan/version, trial/grace, limits and billing status for selected business. |
| BUS-52 | Subscription — approved payment / renewal | Flow | Partial | P1 | Real provider outcomes, secure checkout and verified reconciliation. |
| BUS-53 | Business Settings — general / receipt policy | Tab | Missing | P2 | Legal name, timezone, store details and receipt options with audit. |
| BUS-54 | Business Settings — tax and payment policy | Tab | Missing | P1 | Market-configured tax/payment rules; cannot fake Card/UPI capability. |
| BUS-55 | Business Settings — language and preferences | Tab | Missing | P2 | Published locale pack, correct currency and RTL independent of language. |

### D. Cashier / POS — approved Ibdaa-inspired independent 61/39 layout

**Who sees this:** Cashier; Owner/Manager only if explicitly authorized; no permanent sidebar.

| ID | Screen / interaction | Type | Current | Priority | Required outcome / guardrail |
| --- | --- | --- | --- | --- | --- |
| POS-01 | New Sale — catalogue + basket | Page | Partial | P0 | FULL-WIDTH: 61% photo product/category/search grid left, 39% cart/customer/total right; no admin sidebar. |
| POS-02 | Product quick detail / selection | Modal | Missing | P1 | Only if required by real product configuration; use actual price/unit/stock, no tailoring-only fictional controls. |
| POS-03 | Customer search / walk-in selector | Panel | Missing | P1 | Visible on basket, business-scoped customer lookup; optional only if retail policy permits. |
| POS-04 | Quick add customer | Modal | Missing | P2 | Create customer without losing current cart, with duplicate checks. |
| POS-05 | Cart quantity / remove / clear | State | Partial | P0 | Inline fast edit, exact money and stock checks; no extra page. |
| POS-06 | Payment Popup — Cash first | Modal | Missing | P0 | Pay button opens centered accessible dialog: total, received, change, Confirm, Cancel, no posting on open. |
| POS-07 | Payment Popup — processing / unknown outcome | State | Missing | P0 | Disable double submit; reconcile original request before retry, never fake failure/success. |
| POS-08 | Payment Popup — Card/UPI/Split | Tab | Deferred | P2 | Only show/enabled after verified backend and configured payment capability. |
| POS-09 | Sale complete / committed receipt | State | Partial | P0 | Persisted sale ID, exact totals, Print / New Sale, original basket safely cleared. |
| POS-10 | Receipt print / reprint | Page | Partial | P1 | Print ONLY a committed receipt; printer failure does not repost stock/money. |
| POS-11 | Find invoice / sale history | Page | Partial | P1 | Existing latest receipt only; searchable invoice list still missing. |
| POS-12 | Held bills — list / resume / cancel | Page | Missing | P2 | Draft only; no stock posting/payment until later real completed sale. |
| POS-13 | Return/refund lookup | Flow | Missing | P1 | Read eligibility against posted invoice; show actions only to permitted role. |
| POS-14 | Cash Register — open shift | Modal | Missing | P1 | Authorized location, opening float and drawer rules; audit. |
| POS-15 | Cash Register — shift summary | Page | Missing | P1 | Posted cash movements, expected vs counted, business date. |
| POS-16 | Cash Register — close shift | Modal | Missing | P1 | Count, variance and approval, no silent cash mutation. |
| POS-17 | Insufficient stock / changed price review | Modal | Missing | P0 | Server reprice/revalidate; explain change before charging, preserve basket. |
| POS-18 | Outage / pending-sale recovery | State | Partial | P0 | Existing pending/replay path; make status understandable and block duplicate posting. |

### E. Deferred product families — SCREEN IDs reserved, NOT Retail V1 commitments

**Who sees this:** Only when vertical/Enterprise feature is separately approved and fully supported.

| ID | Screen / interaction | Type | Current | Priority | Required outcome / guardrail |
| --- | --- | --- | --- | --- | --- |
| FUT-01 | Salon — business setup and locations | Family | Deferred | P3 | Services, working hours, locations, dedicated salon template. |
| FUT-02 | Salon — service and resource masters | Family | Deferred | P3 | Services, staff, resource capacity, pricing. |
| FUT-03 | Salon — appointments calendar and booking | Family | Deferred | P3 | Real availability, timezone, resource overlap and status. |
| FUT-04 | Salon — customer / visit history | Family | Deferred | P3 | Access-controlled client records and service history. |
| FUT-05 | Salon — checkout and payments | Family | Deferred | P3 | Service-linked, appointment-aware sale and receipt. |
| FUT-06 | Repair — job intake and customer | Family | Deferred | P3 | Job card, equipment/customer details and consent. |
| FUT-07 | Repair — technicians, workflow/status | Family | Deferred | P3 | Assignment, work queue, approval and turnaround. |
| FUT-08 | Repair — parts, stock and repairs billing | Family | Deferred | P3 | Parts movement, estimate, invoice and warranty rules after definition. |
| FUT-09 | Public customer portal | Family | Deferred | P3 | Only if vertical publishes customer booking/order journey. |
| FUT-10 | White-label / verified domain onboarding | Family | Deferred | P3 | Enterprise contract, brand review, DNS/TLS verification and rollback. |
| FUT-11 | Dedicated SQL Server tenant provisioning | Family | Deferred | P3 | Platform-only data assignment, backup, migration/restore tests. |
| FUT-12 | On-premises offline installation and activation | Family | Deferred | P3 | Local bootstrap, license import, no cloud dependency; separate release gate. |


## 3A. Implementation evidence / screen in progress

| Screen ID | Changes on `development` | Evidence | Gate |
| --- | --- | --- | --- |
| **AUTH-01** | `WorkspaceEntry.tsx` adds three premium entry cards; `App.tsx` wires separate portal query/direct login and filters business membership by role; `styles.css` adds responsive entry/login layouts. | [Gateway commit](https://github.com/dharmendra1031/business_saas/commit/0dca155852858d47c96c940c6be8b3f16f0c7a57) · [Login commit](https://github.com/dharmendra1031/business_saas/commit/5ae9fd65a015300e4706e1e8105529060ff02ffd) · [UI build passed](https://github.com/dharmendra1031/business_saas/actions/runs/44918699661) | **Partial**, not DONE: requires real browser at desktop/tablet/360px, keyboard, authenticated Super Admin/Owner/Cashier checks, Owner business creation path removal with P0 backend provisioning. |

**Important:** This change does not yet implement Super Admin-only business creation; the prior Owner creation path and API must be replaced in **SA-04 through SA-08** with server-enforced authorization and safe owner assignment.

## 3B. Super Admin visual implementation, evidence and remaining gaps (22 Nov)

**Reference:** User-approved final 12-screen Super Admin montage from 21 Nov 2026. **Detailed, itemized UI comparison:** [Businesses and Create Business Visual QA](./SUPER_ADMIN_BUSINESS_VISUAL_QA.md).

- `SA-03`: Business directory includes real server-paginated table and new Create Business CTA; plan/status filters and browser screenshot sign-off pending.
- `SA-04`: business name, type, country, currency and timezone wizard step built.
- `SA-05`: verified existing owner lookup built; **owner invitation/email verification flow missing**.
- `SA-06`: Starter trial + named first location built; general published-plan selection missing.
- `SA-07`: review screen and MFA-required transactional platform API built; server event audit and duplicate-request protection tested.
- `SA-08`: newly created business opens real details; dedicated success/recovery state and browser visual check pending.

All these entries remain **PARTIAL, not DONE**. Frontend/backend/SQL CI passed for provisioning and security test code on [run 44988100644](https://github.com/dharmendra1031/business_saas/actions/runs/44988100644); later style/docs commits require their own final checks. Real browser screenshots have not been compared pixel by pixel.

## 4. Critical end-to-end journeys — do not lose these

### Journey J-01: Super Admin provisions a business (P0)

`AUTH-02 → AUTH-05/06 → SA-01 → SA-03 → SA-04 → SA-05 → SA-06 → SA-07 → SA-08 → AUTH-07 → AUTH-03 → BUS-02 → BUS-01`

**Check:** Only server-authorized Super Admin can create; owner cannot. Validated country/template and plan; fresh numeric BusinessId, correct real Owner membership, default location and trial/subscription; audited, idempotent and safely retryable; owner invitation can be fulfilled.

### Journey J-02: Business Owner runs a store (P0/P1)

`AUTH-03 → AUTH-11 / BUS-03 → BUS-01 → BUS-06 → BUS-07 → BUS-27 / BUS-29 → BUS-20 → BUS-21 → BUS-23 → BUS-24 → BUS-27`

**Check:** business and branch identity always visible; PO approval alone doesn't increase stock; posting Goods Receipt increases stock once; search/filter/forms are understandable.

### Journey J-03: Cashier makes an actual sale (P0)

`AUTH-04 → POS-01 → POS-05 → POS-06 → POS-07 (if unknown) / POS-09 (if verified posted) → POS-10 → POS-01`

**Check:** Full-width Ibdaa 61/39 design, no admin sidebar; real product prices & location stock; modal cash tender/change and cancel; one authenticated idempotent server sale; receipt only after commit; no repeated stock deduction on retry/print; on uncertainty reconcile original request. Card/UPI/Split is disabled until actually integrated.

### Journey J-04: Return, shift close and reporting (P1)

`BUS-35 / POS-11 → BUS-38 → BUS-39 → BUS-37 → POS-15 → POS-16 → BUS-43 → BUS-44 / BUS-47`

**Check:** No over-return/refund; posted stock/financial movements reconcile; counted cash/variance and true business date report reconcile; SaaS subscription revenue never mixes into shop sales.

### Journey J-05: Platform subscription control (P1)

`SA-17 → SA-18 → SA-19 → SA-20 → SA-09 / SA-13 → SA-22 → SA-23 → SA-24 → SA-25`

**Check:** Versioned plans and historical terms, prepared request is not marked paid, actual provider/ledger proof and audited reconciliation. Distinguish SaaS payments from customer checkout.

## 5. One-screen-at-a-time completion protocol

For **each ID**, update a single row and short screen note (link a test or commit). Do not claim DONE when only a layout screenshot, empty form, migration or unit test exists:

1. **Pick ID:** confirm owning workspace, role, flow, baseline screenshot, source file and whether API exists.
2. **UX design:** approved exact component hierarchy, default/empty/loading/error/offline/forbidden states, mobile behavior, keyboard/dialog focus and visible location/business context. POS should match the approved image rather than a different generated layout.
3. **Small implementation:** build only the page's real operations (no dead buttons, duplicate services, speculative infrastructure or unrelated CSS); hide truly unsupported functions honestly.
4. **Backend authorization:** enforce platform vs owner vs manager vs cashier, BusinessId/LocationId, subscription entitlement, transaction idempotency and audit where relevant.
5. **Verification:** backend + SQL Server integration + frontend typecheck/build, real authenticated browser journey at **1536×864, 1366×768, 768px and 360px**, keyboard/touch and if relevant printer/payment recovery; compare screenshot with approved target.
6. **Record outcome:** status becomes DONE only after passed tests and product-owner UX acceptance. Record source commit, CI URL, browser evidence and known gaps. **Then** start the next ID.

### Per-screen check record (copy for each accepted ID)

```text
Screen ID:
Route / tab / modal:
Role(s), BusinessId / LocationId:
UX reference image / specific acceptance:
Backend and data contract:
Empty / loading / error / permission / mobile / keyboard states:
Commit:
CI/backend/SQL/frontend proof:
Real browser and 80mm printer/payment proof (if applicable):
Security / tenant boundary:
Reviewer approval + date:
Status: Partial | Missing | Deferred | DONE
```

## 6. Known source-code mismatches to fix first

| Mismatch | Source checked | Correct outcome |
| --- | --- | --- |
| Owner create was present in UI/API | `App.tsx` now hides/removes it; `BusinessEndpoints.cs` denies it by default, integration tests use temporary Development flag | P0: finish legacy fixture migration, verified owner invite and end-to-end login/provision tests |
| Shared sidebar persists in POS | `frontend/web/src/App.tsx` | P0: dedicated cashier shell, no admin sidebar; approved 61/39 design |
| Product-search dropdown instead of approved catalogue cards | `frontend/web/src/CashCheckout.tsx` | P0: real image/category cards, scan/search and exact prices |
| Cash tender inside page instead of approved Payment Popup | `frontend/web/src/CashCheckout.tsx` | P0: accessible modal and safe cash submit/retry |
| Owner dashboard mostly setup actions, not business insights | `frontend/web/src/App.tsx` | P0: real KPI and exception-first dashboard |
| Generic login for all three roles | `frontend/web/src/App.tsx` | P0: three distinct role entry/login experiences using same backend |
| Super Admin eight-menu baseline, but approved montage shows extra illustrative top-level links | `docs/APPROVED_UI_UX.md` and project-approved visuals | Keep eight modules; nest Subscriptions/Payments/Reports under proper groups |
| Development-only cash-sale API | `backend/BusinessSaas.Api/Features/RetailModule.cs` | Do not expose production POS until correct cash/financial/tax tests, rollout and release approval |
| Earlier requirement revisions suggested self-service creation | Historical SRS/UX wording (updated 21 Nov 2026) | Current SRS/UX/roadmap/tracker now reflect DR-001; **backend/UI implementation and tests remain P0** |

## 7. Ordered execution queue (do not jump around)

| Order | ID(s) | Deliverable |
| ---: | --- | --- |
| 1 | **AUTH-01, AUTH-02, AUTH-03, AUTH-04** | Three clear workspace entries and role-verified login navigation |
| 2 | **SA-03 to SA-08** | Platform-only create business, assign owner, provision and owner invitation (backend safe) |
| 3 | **BUS-02, BUS-01, AUTH-11** | Owner sign-in, correct store context and useful premium dashboard |
| 4 | **POS-01, POS-05, POS-06** | Approved full-width photo grid/cart + payment modal |
| 5 | **POS-07, POS-09, POS-10, POS-17, POS-18** | Real sale posting, safe retry/recovery and receipt print |
| 6 | **BUS-06–BUS-08, BUS-10–BUS-15, BUS-17–BUS-18** | Premium shop masters with correct empty/edit flow |
| 7 | **BUS-20–BUS-32** | Purchase/receiving/stock workflow and approvals |
| 8 | **BUS-35–BUS-47, POS-11–POS-16** | Sales history, returns, register, reports |
| 9 | **SA-01–SA-02, SA-09–SA-47** | Finish platform admin tabs, billing, audits and approved later features by priority |
| 10 | **FUT-01–FUT-12** | Deferred unless explicitly re-prioritized |

**Rule:** This is a scope index and execution order, not a promise that each future Family is an independently approved feature. Keep every completed change on `development` and `main` untouched until formally released.
