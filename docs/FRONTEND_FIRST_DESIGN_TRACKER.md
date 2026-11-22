# Business SaaS — Frontend-First Design & Visual QA Tracker

**Adopted:** 22 November 2026 (owner instruction: design every screen first, integrate missing backend second).
**Branch:** `development` ONLY; do not edit `main`.
**Canonical screen IDs:** [Master Screen Register](./MASTER_SCREEN_REGISTER.md).
**Scope:** Super Admin, Business Owner/Admin, POS/Cashier; Retail V1 before deferred Salon/Repair.

## 1. Delivery sequence — design before missing backend

1. **Frontend architecture and visuals:** One React/Vite app; distinct Platform Admin, Business Admin and full-width POS shells. Preserve approved branding and screen flow; reuse components only when truly shared. No separate duplicated apps.
2. **Complete each screen's real navigation and interactive UX:** Responsive layout, dropdown/tab behavior, accessible forms, popups, screen hierarchy, table/filter states, permission/empty/error/loading variants. Existing APIs can be reused read-only, but **new backend work is postponed** until approved design freeze.
3. **Visual QA against exact approved screenshots:** Desktop 1536×864 and 1366×768; tablet 768px; phone 360px. Compare hierarchy, header/sidebar, spacing, dense cards, CTA positions, grids, form heights and scrolling. Test clicks, Tab/Enter/Escape and focus. Record screenshot path and comparison result, not just "looks fine".
4. **Owner design freeze:** Mark `DESIGN APPROVED` *only* after browser screenshots and user confirmation. Approval of a mockup does not equal implementation approval.
5. **Backend integration second:** Connect missing ASP.NET Core + SQL Server APIs one verified feature at a time with role/tenant scoping, entitlements, idempotency and audit. Retain working backend and migrations unchanged during the frontend design stage.
6. **Release acceptance third:** Real data/roles, SQL Server integration, printer/payment tests and negative cases must pass before a screen becomes `DONE` in the Master Screen Register.

### Stage states (independent of DONE)

- `Design planned`: screen ID/requirements exist, but frontend layout not prepared.
- `UI source implemented`: TypeScript/JSX + responsive CSS committed; **no visual acceptance yet**.
- `Preview only`: interactive look and tab navigation exist, but corresponding backend isn't wired. Label preview clearly; no fake money, simulated saves or pretend success.
- `Visual QA pending`: exact approved-reference comparison with actual browser capture still required.
- `Design approved`: user approves visually verified UI at desktop and mobile.
- `Integration pending`: design ready, but APIs/security/tests not complete.
- `DONE`: defined by Master Screen Register, after actual business flow and all tests.

## First-screen simplification (22 November 2026)

- Removed the redundant three-workspace selection gateway and role-specific login copies. There is **one** sign-in screen for Platform Owner, Business Owner/Manager and Cashier; access is determined from the authenticated server session.
- A Platform Owner goes to MFA setup or the Super Admin shell. Other users see their real assigned businesses; a sole business opens automatically and its role determines the Owner/Manager dashboard or Cashier POS.
- Users assigned multiple businesses choose one *after* signing in; no role-choice form or login-then-logout on mismatched roles.
- Reused the existing identity/MFA APIs and business selection. Removed obsolete gateway code and CSS; no new library or backend changes.
- **Auth/session resilience:** session bootstrap exposes a Retry state on network/server failure instead of treating every error as logout; a successful login transitions to a single session loader, with distinct business loading/retry (no password reentry on transient business API errors).
- **Unnecessary access call removed:** no redundant authenticated `/platform/access` request or stale workspace query cleanup. `/auth/me` remains the authoritative owner/MFA identity source; backend authorization is unchanged.
- **Visual QA pending:** confirm responsive layout in browser and final user approval before marking this screen complete.

## AUTH-01 — actual browser screenshot QA evidence (22 November 2026)

- **Source:** React/Vite development commit [45241cb](https://github.com/dharmendra1031/business_saas/commit/45241cb85348aab2c74912b7ae2f0361ef1608ab), validated in [Chromium Browser QA run 45014299057](https://github.com/dharmendra1031/business_saas/actions/runs/45014299057) (**PASS**).
- Actual browser screenshots: 1536×864 desktop, 1366×768 desktop, 768×1024 tablet, 360×780 mobile; archived in the run's auth-browser-screenshots artifact. Tested no horizontal overflow on login, keyboard Tab order and Enter-to-submit, outage + retry, authenticated business API failure + retry, Owner/Manager routes, multi-business picker, Cashier direct POS with no admin sidebar, Super Admin dashboard and MFA paths. Uncaught browser JavaScript errors were checked.
- Browser scenarios used **controlled API fixtures**, not actual application SQL Server users, so they verify implemented UI routing and presentation only. Existing backend/SQL CI checks remain separate. A screenshot with fixture data does not certify real role enforcement.
- **Visual inspection:** shared centered login is compact without unnecessary workspace-entry cards; 360px fields and CTA fit. Removed redundant explanatory footer after screenshot review.
- **Screen state: Partial / Visual QA evidence captured; not DONE.** Still required: genuine authenticated environment with test accounts for each role, owner review of the actual images, browser verification of server policy denials and accessible session recovery across deployment.

## AUTH-02 — Super Admin login branch QA (22 November 2026)

- **Implementation:** [MFA form fixes](https://github.com/dharmendra1031/business_saas/commit/68fe5c7148c1035f29c4c1e85a65d7f42a9e105e) and [masked recovery code input](https://github.com/dharmendra1031/business_saas/commit/ff2b6e701e3fa2f4e7c875b4ba79f363899b8144). Only the existing shared `AuthPage.tsx` was changed; no extra Super Admin page, library, backend endpoint or new architecture.
- **Browser checks:** [Chromium run 45015039409](https://github.com/dharmendra1031/business_saas/actions/runs/45015039409) **PASS** using isolated API fixtures: invalid password -> retry, lockout response, MFA cannot be skipped, malformed 6-digit code validation, bad code -> valid code, recovery-code flow and reset to authenticator when restarting password sign-in. Captured 360px MFA challenge and 1366px recovery screenshot, including input masking. The run retains other shared-login role/mobile tests.
- **Source/build verification:** [Build run 45015039397](https://github.com/dharmendra1031/business_saas/actions/runs/45015039397) **PASS** (frontend, backend, SQL Server jobs). The browser fixture does not constitute production server MFA enforcement testing.
- **Status:** `AUTH-02 = Partial`, browser UI QA evidenced; still needs real SQL-backed Super Admin credentials, server-side deny/lockout cases in authenticated E2E, and product owner approval of the actual browser captures. Do not mark DONE.

## Business Owner/Admin — compact module headers (22 November 2026)

- The screenshot showing **Quick access** is older than the current `development` UI; verify local Git branch/commit and restart Vite to compare.
- Products, Purchase Orders, Inventory, Categories & Units, Suppliers and Customers previously rendered three title layers: sidebar, shared page hero with business/role/trial details, and a second oversized module heading. The shared retail page hero and repeated trial line have been removed. Each actual module keeps **one compact heading and its real controls** (Add Product, New Purchase Order, stock location selector, category tabs, search, forms, tables and permission states).
- Staff & Access no longer adds an extra top margin. Retail topbar has a short section name; sidebar retains authorized business identity, without the repeated role footer. Changes are scoped to Business Admin; Platform Super Admin/POS layouts are not changed.
- The preview-only Sales/Returns, Cash Register, Reports and Business Settings tabs retain working tab selection and genuine read-only business identity when available. Decorative preview welcome cards, empty chart-like panels, and developer-facing implementation guardrails were removed. Missing features remain visibly **Under construction**, with no fake numbers or saves.
- No backend, SQL, permissions, new UI library or routing framework added. Real browser testing at 1366px/360px and user acceptance are separate; **not DONE** based on source changes alone.

## BUS-01 — owner-approved dashboard direction (22 November 2026)

- Established stores: **Business Overview**, **Attention Needed**, **Recent Activity**. Maximum four compact KPI slots and no duplicated sidebar navigation. New stores: a separate first-run setup panel with working Categories/Units and Products links; stock comes after products and receipt settings is marked unavailable.
- **No fabricated metrics:** The current dashboard API does not provide Today's Sales, Bill Count, low-stock thresholds or a recent-activity feed. These show explicit unavailable states, not false zeros or invented transactions. The only verified operational count currently available is owner Draft Purchase Orders; drafts are not presented as approved purchases.
- Billing/payment module is preserved inside the existing collapsed disclosure but now **loads only when opened**, preventing unnecessary background checkout requests. Suspended subscription access is handled distinctly.
- No backend/SQL migrations or new libraries. Frontend-first design state; browser QA and owner visual acceptance remain separate from integration and DONE.

## BUS-01 — Business Owner dashboard density cleanup (22 November 2026)

- Removed repeat business-name welcome hero, repeated role badges and four tall status cards. The workspace/sidebar already contains module navigation, so the next simplification removed six redundant Quick Access cards too. One short status line remains, with a three-step onboarding checklist only while products are not yet added. Multi-location details stay collapsed.
- Preserved the existing six working management navigation actions, Owner-only Staff & Access, Development-only POS entry where available, Owner-only draft purchase review when nonzero, and optional multi-location details. The platform subscription and billing section remains unchanged.
- Subscription/plan state appears once in the compact status line on BUS-01; single-location counts are omitted when already visible in the workspace identity. The mobile topbar uses a short Dashboard title. Other business pages keep their existing labels; no extra API calls or fabricated metrics.
- [Chromium Browser QA 45015650859](https://github.com/dharmendra1031/business_saas/actions/runs/45015650859) and [Build 45015650864](https://github.com/dharmendra1031/business_saas/actions/runs/45015650864) passed, including the compact zero-duplicate navigation, desktop/mobile screenshot capture and 360px overflow check. API responses are test fixtures; real authenticated server verification and product-owner visual sign-off remain pending.

## 2. Exact approved-image references examined

The following original images are available as **conversation-attached design references**, not currently GitHub-tracked assets. Do not claim the images are committed to the repo without an explicit verified binary upload.

| Reference | SHA-256 | Used for |
| --- | --- | --- |
| `a_high_resolution_modern_saas_super_admin_dashb.png` | `bb15fe0ca7588d6aa54a288d9afcba9808a2501ad5dfb7aca7b1d168f8285ebe` | Navy full-width header, compact Super Admin sidebar, five KPI cards, charts, recent businesses/activity/quick stats |
| `a_high_resolution_composite_ui_mockup_dashboard_mo.png` | `16d0b8d4e06c2fc65fc132dc7f43c0c56aadf08314c3be9235d145bab96a2a37` | Final all-screen Super Admin montage; Businesses directory, create wizard, plans/settings page positioning |
| `a_high_resolution_enterprise_saas_admin_dashboard.png` | `b912ae20d27e2a591a482ecbb72092c9cd39e2d21a1f1a0486706741d26e505d` | Platform Settings and Audit Logs visual hierarchy |
| `BusinessSaaS_POS_Approved_2026-11-21.png` | `b0bab91781b7bb2eb285a3813e863399f21855a32621f7e2e7aefcf7c003bc53` | **Separate approved POS layout**, 61% catalogue / 39% basket + Payment Popup, no permanent sidebar |

**Design priority:** Final 21 Nov Super Admin montage overrides older platform palette, but the stable **eight top-level platform modules** in [Approved UI/UX](./APPROVED_UI_UX.md) govern navigation. Referenced fake companies, finance figures and dates in images are illustration only. Business operations must remain tenant-scoped.

## 3. First frontend design pass — code-level reference comparison

| Screen | Target from approved reference | Frontend source implemented | Honest stage / remaining work |
| --- | --- | --- | --- |
| `SA-01` Platform Dashboard | Navy spanning header; violet highlight; five compact KPIs; chart split; Recent Businesses / Activity / Quick Stats lower triptych | `App.tsx`, `PlatformOverview.tsx`, scoped `styles.css`. Five real-data KPIs; business growth instead of **unverifiable** revenue trend, actual plan breakdown, real paginated recent-business lookup, activity, quick stats and trials list. | **UI source implemented; visual QA pending.** Actual browser screenshots / mobile and visual owner sign-off not done. |
| `SA-03` Businesses Directory | Create Business CTA, compact search, owner/plan/status table, clearly paginated rows | `PlatformAdmin.tsx`, `styles.css` improve table density, status chips, avatars, search, count, empty/error/loading. Existing server-side business-name search and pagination remain. | **UI source implemented; visual QA pending.** Plan/status filters and created dates are not backed by current API; intentionally not faked. |
| `SA-04..08` Create Business Wizard | Four steps, clean forms, explicit review/confirmation, owner assignment | Existing `PlatformCreateBusiness.tsx`, recent validation/retry/confirmation UX. Uses existing real platform provisioning API. | **Partial; visual QA pending**. Verified invitation delivery, extra plans and screenshot comparison missing. |
| `SA-29..32` Licenses | Platform license screen with directory, issue/activation, lifecycle, history sections | `PlatformModulePreview.tsx` creates responsive navigable section layouts. | **Preview only.** No issuance, activation, license data or fake success. Backend integration deferred. |
| `SA-33..39` Platform Settings | Global configuration, business templates, modules, localization and notifications | `PlatformModulePreview.tsx` creates navigable, clearly read-only design tabs. | **Preview only.** No saving, fake global settings, unpublished translation upload or notification delivery. |
| `SA-14..15` Customers | Approved compact account directory, verification status, business count and customer details | `PlatformCustomers.tsx` redesigned with server-paginated search, status badges, customer profile, assigned-business links to existing platform business detail. | **UI source implemented; visual QA pending.** Backend does not supply customer names, phone numbers or account suspension, so the mockup's fields/actions are NOT invented. `SA-16` account restriction remains deferred. |
| `SA-17..20` Plans & Billing | Approved plan-card pricing grid, limit rows, versioned published catalog and edit/publish actions | `PlatformPlans.tsx` now shows responsive API-backed plan cards, published prices/limits/status, existing audited draft/publish forms with scroll-to-panel UX; `PlatformBilling.tsx` retains real draft/payment/reconciliation UI. | **UI source implemented; visual QA pending.** Plans without published pricing say Not configured. Real subscription directory/invoices/refunds (`SA-21..28`) remain partial or missing; mock paid prices are NOT used. |
| `SA-40..46` Audit/Operations | Premium audit logs, MFA status, real readiness and six-card operations layout | `PlatformAudit.tsx` now reads actual `/auth/mfa/status`, shows audit table with expand-to-read reason/raw before/after values; `PlatformOperations.tsx` displays genuine `/ready` and six cards with an actual Audit navigation action. | **UI source implemented; visual QA pending.** Search/date filters, backup/monitoring/job data and announcements are not available, so those remain labeled design previews. Fixed `Invalid Date` bug for non-date audit values. |
| `BUS-* / POS-*` | Separate Business Owner/Admin and Ibdaa-style full-width Cashier POS | Existing partial screens retained untouched during this first platform batch | **Next frontend batches**, then visual acceptance and backend gaps. |

### Scope and implementation safeguards

- Only frontend source/doc changes in this batch; **no backend/database changes**.
- Header search navigates and uses the **existing real `GET /platform/businesses?search=`**. It does *not* pretend to search customers, invoices or full platform records.
- Create Business dashboard CTA navigates to the existing MFA-authorized Super Admin wizard; do not duplicate business-creation logic.
- Revenue is marked unavailable without a real settlement ledger. Existing growth chart describes *business creation*, not revenue.
- All 8 platform menu entries are now navigable, but **Licenses and Platform Settings explicitly say "Frontend design preview"** and cannot save changes.
- No frontend screen should be marked `DONE` based only on a green build. User review and authenticated real browser screenshots remain mandatory.
- Keep no new CSS framework, fake API server, duplicate auth application, speculative microservice or new database table.

## 4. Next frontend-only design order

1. Refine `SA-01`, `SA-03`, `SA-04..08` using real browser screenshots at all four widths; compare directly with final approved reference.
2. **Second platform frontend batch implemented in source:** Customers directory/details (`SA-14..15`), real priced plan cards/catalog (`SA-17..20`) and Audit/Operations (`SA-40..46`). Next visually QA at 1536/1366/768/360 before approving; add remaining nested billing flows and UX states without fictional data.
3. Improve `SA-29..39` Licenses/Settings preview tabs and remaining `SA-16`, `SA-21..28` customer/billing UX only when relevant contract is clear; do not present preview or missing APIs as working.
4. Complete `BUS-01..55` Retail Owner/Admin UI: dashboard, catalog/masters, suppliers, purchase, stock, reports, settings.
5. Complete `POS-01..18` using exact approved **61/39** image, Payment Popup, held bills/history/returns/register design; do not change transaction backend yet.
6. Obtain design sign-off for all released screens, then begin backend integration and E2E tests.

This is a visual scope tracker, **not** a statement that all frontend screens are finished.


## 5. Second frontend batch — approved-image gap check (22 Nov 2026)

| Reference tile | Visual hierarchy implemented in source | Backend-limited omissions — intentionally not faked | Browser acceptance |
| --- | --- | --- | --- |
| **4: Customers (Owners)** | Enterprise header, compact email search, verification pills, server pagination, customer profile, owned business table with real Details links | Mockup shows name/mobile/active-suspended status; current API exposes email, emailConfirmed and owned-business count only. No fake account status, no Add Customer button. | **Pending** real captured screenshot and mobile/keyboard tests |
| **5: Plans & Pricing** | Responsive plan cards, published monthly currency prices, assigned business/limits, documented draft/immutable publish action, tabs for Catalog & Billing Drafts | Mockup prices/tier features unverified. No invented ₹499, UPI, subscriptions payment capture or disabled fake toggles. | **Pending** |
| **9: Operations** | Six-card grid matching visual density; actual readiness status and Audit Trail link | Tenant databases, backup/restore, jobs, incident monitoring, announcements deliberately tagged Design preview; no fake healthy status. | **Pending** |
| **11: Activity Logs** | Security header, real MFA state, audited events with expandable reason/previous/new data and pagination | No backend global search, date/action/user filters or fabricated named actors. Fixed prior logic that incorrectly formatted non-date old/new values as timestamps. | **Pending** |

**Quality gates:** React TypeScript build + .NET/SQL CI is a necessary build gate, not visual proof. Actual approved-reference **image-versus-browser screenshots**, keyboard/tab/mobile checks and product-owner design acceptance are still required. No image file or screenshot of the running app is asserted here.

**Code batch CI evidence (22 Nov):** [Actions run 45008520111](https://github.com/dharmendra1031/business_saas/actions/runs/45008520111) — **frontend TypeScript/build PASS, backend integration PASS, isolated SQL Server PASS** on frontend code commit `9982ba7f`. These results verify build/integration checks only; browser visual/image comparison is still pending.

## Consistent readability and card geometry (22 November 2026)

- Adjusted scoped Business Owner/Admin, Platform Super Admin and POS styles for more readable body/secondary labels and consistent heights for comparable cards within a single grid. Increased visibility of dashboard status text, metric descriptions, table headings/rows and module toolbar labels.
- Business module header rows now have a shared minimum height, and business-selection cards, KPI tiles, platform metric/plan cards and POS product tiles stretch evenly within each row. This does NOT force forms, tables or different-sized charts into equal fixed heights. POS 61/39 panel geometry retained.
- No new CSS library, no new component abstraction, no business logic/API/database changes. Dashboard structure unchanged. Chromium screenshots and owner sign-off required before any screen is marked final.

## 6. Business Owner/Manager frontend batch — 22 November 2026

**Scope:** `BUS-01..04`, `AUTH-11`, `BUS-06..08`, `BUS-10..15`, `BUS-17..18`, `BUS-20..25`, `BUS-27..32` UI refinements, plus frontend design navigation for previously missing sales/register/report/settings screens. No backend/database files were changed.

| Area / screen IDs | Implemented frontend work | Reference and logical UX check | Honest stage |
| --- | --- | --- | --- |
| **Business shell / BUS-01 / AUTH-11 / BUS-03** | `App.tsx` has a role-specific `business-shell` blue/navy sidebar/top bar, active modules, business-and-location page heading. Assigned business cards show actual name, type, country and assigned role; Owner cannot self-create businesses. | Distinct from violet Super Admin and the separately approved Ibdaa-style POS; correct business switcher without fake provisioning controls. | **UI source implemented**. Desktop/tablet/phone screenshot fidelity and keyboard verification pending. |
| **Dashboard / BUS-01 / BUS-02** | Two data states: first-run onboarding when products are absent, or established-store Business Overview (up to four KPIs), Attention Needed and Recent Activity. Draft PO review uses server count; missing Sales/Bills/Stock Alerts/activity stay explicitly unavailable until APIs exist. Billing fetch begins only when owner expands the panel. | No decorative hero, duplicate navigation or fabricated data; stock setup completion is not inferred from the product-exists boolean. | **UI source committed; latest browser screenshots, real authenticated E2E and owner visual approval pending.** Not DONE. |
| **Products / BUS-06..08** | `RetailProducts.tsx` now uses premium compact header, real filtered count, functional Add Product, search and category/status filters, truthfully badged active state, disabled/read-only and empty/error variants. | Product creation/editing/back-end validation unchanged. No fake stock quantity or POS support claim. | **UI source implemented**; browser acceptance pending. |
| **Inventory / BUS-27..32** | `RetailStock.tsx` now has location-identity header, real location selector, search, accurate quantity/not-initialized states, existing opening/adjustment/history preserved. | Never conflate total business stock with selected-location stock. Existing posting guards kept. | **UI source implemented**; browser acceptance and negative-case checks pending. |
| **Master/Contact/Purchase / BUS-10..15, BUS-17..18, BUS-20..25** | `RetailMasters.tsx`, `RetailContacts.tsx`, `RetailPurchases.tsx` share the new enterprise heading, responsive tables/forms/filters and coherent action styling, but retain all original business APIs and role checks. | Only one canonical UI action per flow; no duplicated purchase or stock logic. | **UI source implemented / source-level UI pass**, visual QA pending. |
| **Sales / BUS-35..40; Register / BUS-41..42; Reports / BUS-43..47; Settings / BUS-53..55** | `BusinessPreview.tsx` provides keyboard-focusable tabs and design-only panels, now reachable from permitted Owner/Manager sidebars. Owner-only settings view displays actual business identity/country/currency/timezone from the authorized dashboard. | The absent sales ledger, receipt history, return/refund posting, register sessions/day closing, report totals, tax/payment/receipt settings are **not implemented**; clearly bannered as **Frontend design preview**. No fake data, Print/Save or payment action. | **Design preview only**; leave canonical screen IDs Missing or Partial until backend + E2E. |
| **Cashier separation** | Updated sidebar filter so a Cashier with enabled checkout no longer receives the Business Dashboard menu entry. | Dedicated full-width POS still requires separate approved `61% catalogue / 39% basket` implementation; not claimed finished. | **Partial**, POS-specific work pending. |

### Acceptance and actual-reference limitations

- Approved **Business Owner** look is directional blue/white enterprise UX; existing current context does not provide a versioned exact pixel-level approved screenshot of every retail sub-screen. Do not claim a perfect visual match from code alone.
- Verify every screen at 1536×864, 1366×768, 768px and 360px in an authenticated browser session, including role switch, subscription-inactive/read-only, empty/error/loading, form focus, page overflow, and receipt/stock transaction non-regression.
- Existing read/write APIs are preserved. **Not** working yet: posted-sales lists, daily cash register closure, reports API, receipt/tax/payment settings writes; these are visibly preview-only.
- Catalog and business overview checks are **source-level comparison with the approved design decisions**, not new browser screenshots or owner visual sign-off.
- Next focus: review mobile visual fidelity and continue the **dedicated POS** based on approved Ibdaa inspiration, without enabling unintegrated payment options.

**Business UI batch CI evidence (22 Nov):** [GitHub Actions run 45009702082](https://github.com/dharmendra1031/business_saas/actions/runs/45009702082) at code commit `218ef9c`: frontend TypeScript and Vite production build **PASSED**, backend tests **PASSED**, isolated SQL Server integration **PASSED**. Earlier failed runs in this batch were TypeScript route narrowing errors; the final `isRetailSection` guard fixed them. CI does not substitute for screenshot/pixel-level visual acceptance.

## 7. DR-002 — Release Policy, Business Type Availability & Maintenance UI (22 Nov 2026)

- **New approved capability:** Platform Super Admin can eventually control which registered Business Types can be provisioned; distinguish Available, Coming Soon, Under Construction, Paused and Retired; roll out modules independently; schedule scoped maintenance modes and publish targeted notices.
- **No new top-level sidebar item:** Under existing **Platform Settings**, tab menu adds **Maintenance** and **Announcements** to General, Business Types, Modules & Access, Languages and transactional Notifications.
- **Source:** `frontend/web/src/PlatformAvailabilityPreview.tsx` (new), `PlatformModulePreview.tsx` (integrated tabs), scoped `styles.css`.
- **Frontend behavior today:** Business Types tab reads `GET /api/business-types` (catalogue list only); UI can locally choose a sample state, module family and notice/maintenance copy, scope, operating mode and times to preview the layout. **None of these values are saved, activated or published.** No server release state or maintenance-window APIs exist.
- **Architecture contract:** [Platform Release & Maintenance SRS](./PLATFORM_RELEASE_AND_MAINTENANCE_SRS.md). No duplicated booleans, fake demo enabled statuses or arbitrary DB-defined business types. Future server-side provisioning and module execution gates must validate state; use existing platform audit, typed registered scopes and row-version concurrency.
- **Visual QA:** Based on approved navy/violet Super Admin visual system (compact cards, state labels, responsive forms). **Actual 1536/1366/768/360 browser screenshot comparison and product-owner approval pending.** Do not mark `SA-33..39` as DONE.

**DR-002 code CI:** [GitHub Actions run 45010167667](https://github.com/dharmendra1031/business_saas/actions/runs/45010167667) on CSS/frontend commit `f67a70f`: frontend TypeScript/build, backend checks and SQL Server integration all **PASSED**. The latest documentation commits do not alter app code. Browser screenshots and actual operator/visitor policy enforcement are still **not verified or implemented**.


## 8. Clean-code / over-engineering audit (22 Nov 2026)

**Reviewed:** `App.tsx`, `PlatformAvailabilityPreview.tsx`, `PlatformModulePreview.tsx`, `BusinessOverview.tsx`, `BusinessPreview.tsx`, styles, connected API contracts and GitHub CI.

**Findings and corrections actually committed:**

- **CSS monolith (High, mitigated):** Original `styles.css` was **102,589 characters / 1,373 lines** with styles for all roles and growing appended screen-specific overrides. Split into exactly **five scope-based files**: shared `styles.css` (21,332 chars), `styles.auth.css` (7,306), `styles.platform.css` (40,025), `styles.business.css` (26,962), `styles.release.css` (6,637). `main.tsx` imports in the **original cascade order**. No framework, runtime CSS injector or new styling dependency. Removed 327 characters of proven obsolete `.nav-entry-planned` rules; total CSS character count is now **102,262**, *not a claim of bundle shrinkage*. Future cleanup can target actual duplicate selectors only after browser comparisons.
- **App/UI coupling (High, mitigated):** Original `App.tsx` was 670 lines and mixed global workspace coordination with sign-in form/MFA UI. Extracted existing auth form unchanged in behavior into **`AuthPage.tsx`**, leaving `App.tsx` at **521 lines**. Remaining platform/business/cashier navigation and state coordination should be split only along real stable role boundaries after screen flow approval, not replaced with an abstract router/config framework by default.
- **Frontend-only policy previews (Medium, intentional temporary):** `PlatformAvailabilityPreview.tsx` is a four-section local design preview. It reads only the registered type catalogue and has **no Save, Publish or enforcement**. When backend integration begins, remove superseded draft-only placeholders rather than leaving a second source of truth.
- **Dead code (Low, partially addressed):** Removed obsolete planned-navigation styling. Do not declare the entire CSS/source tree free of dead rules without automated unused-class analysis plus runtime coverage; dynamic conditional classes complicate static inspection.
- **Architecture (acceptable for current stage):** One React app, one ASP.NET Core API, SQL Server, existing auth and existing platform audit. Proposed registries/policy endpoints/tables for future availability and maintenance are **not implemented**; resist microservices, multi-app duplication, generic plugin engines, arbitrary database-defined modules and duplicate policy flags.

**Clean-code rule:** Each working action uses one source of truth. UI-only previews remain clearly labeled. Do not invent data, save handlers, API responses or deployment health. Enforce business roles/tenant scoping in the API. Any cleanup must preserve behavior, approved responsive layout, accessibility and existing production-protected transaction logic.

**Verified CI:** [Actions run 45010747303](https://github.com/dharmendra1031/business_saas/actions/runs/45010747303), commit `1967df76`, finished with **frontend TypeScript/Vite PASS, backend PASS, isolated SQL Server PASS** after CSS/AuthPage extraction. **Still pending:** browser screenshot comparison across 1536/1366/768/360 and full role-specific E2E. A green compiler is **not visual approval**. Do not describe the project as finished or the full tree as zero-duplication yet.


## 9. POS frontend implementation against final approved 21-Nov reference (22 November 2026)

**Source-level design pass:** `CashCheckout.tsx`, `PosCashPaymentDialog.tsx`, `App.tsx`, `styles.pos.css`, `main.tsx`.
**Approved reference:** [APPROVED POS UI/UX](./APPROVED_POS_UI_UX.md), exact `BusinessSaaS_POS_Approved_2026-11-21.png` SHA `b0bab91781b7bb2eb285a3813e863399f21855a32621f7e2e7aefcf7c003bc53`.
**Frontend only:** No database, server payment gateway, refund, stock movement or authorization behavior changed.

| Approved surface | Implementation from real source | Unfinished / acceptance condition |
| --- | --- | --- |
| **POS-01** Full-width cashier navigation | `App.tsx` uses `isPosView` to remove the permanent admin sidebar, render a navy cashier header with actual business/location and user, and give POS full viewport width. | Actual browser compare at 1536×864, 1366×768, 768px and 360px **pending**. |
| **POS-01** 61/39 catalogue/basket | Scoped `styles.pos.css` gives grid `61fr / 39fr` desktop, responsive tablet/mobile stack, independent catalogue scroll and sticky basket on desktop. | Visual/pixel QA **pending**. Product photographs and categories intentionally absent: existing `/cash-sales/products` API supplies name, SKU, barcode, salePrice and location available stock, not image/category. |
| **POS-01, POS-05** Product selection/cart | Product card buttons use actual product API, honest icon fallback, real price/stock badges, exact barcode/SKU scan action, basket quantity stepper/editor, remove and guarded Clear. Subtotal/total calculated from real priced line items. | No fictional sample prices or stock; product filters, photos, customer picker require server contract. Real browser keyboard/barcode/stock tests **pending**. |
| **POS-06** Cash Payment Popup | `PosCashPaymentDialog.tsx` adds centered modal with amount due, cash received, change, cash-only indicator, Confirm and Back to Cart, focus containment, Escape and background-scroll lock. Modal opens only on Pay/Checkout and does not post on open. | Full browser accessibility + payment E2E **pending**; no fake Card/UPI/Split. |
| **POS-07, POS-09, POS-10, POS-18** Transaction status and receipt | Existing `recordSale`, original requestKey persistence, receipt lookup for pending/unknown sale, committed receipt print and New Sale reset preserved; cash modal calls the original `recordSale` only after validation. Cash tender is reset when changing basket contents. Owner/Manager can return to their business dashboard, Cashier can return to assigned-business selection; a confirmed unsaved basket discard is required. | Cash API is Development-only, real production launch requires approved backend integration, server/SQL test evidence, receipt printer validation and complete error-state browser QA. Do not mark DONE. |

**Observed API limitation:** `RetailCashSales.Search` returns up to 30 active, tax-classification-compatible products at a time. Do not invent pagination or category filter controls without an authoritative backend contract. This product card grid is suitable for real search and scan entry at present; a complete large-catalog browsing contract remains for integration.

**Frontend performance/engineering:** Kept one `CashCheckout` money/pending-sale source of truth; payment modal is purely presentational and returns a confirmed user action to the existing posting handler. Isolated POS styles into one scoped CSS file rather than adding another design library or building duplicate checkout logic.

**Visual QA still required:** load an authenticated cashier account with controlled real products; screenshot capture at the four approved widths; verify basket scroll, full-width page without admin sidebar, Enter scan, tab/shift-tab/Escape, cashier with inactive subscription, modal focus restoration, totals and correct printer output. Approval remains **Partial**, not `DONE`.
