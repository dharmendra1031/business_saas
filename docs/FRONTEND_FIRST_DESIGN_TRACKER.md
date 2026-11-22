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
