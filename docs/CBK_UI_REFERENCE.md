# Business SaaS — CBK UI/UX Design Reference

**Status:** Approved design reference, implementation not started  
**Source reference:** `dharmendra1031/CBK` — `development` (READ-ONLY)  
**Target repository:** `dharmendra1031/business_saas` ONLY  
**Reviewed reference:** Selected CBK theme, layout, item list, purchase order and stock transfer files (static code review only). No screenshots, browser session or runtime usability testing completed.

## Decision

Use the **CBK application's clear, compact, blue-and-white enterprise UI/UX as the primary visual and interaction reference** for Business SaaS. The business modules and customer-facing journeys remain specific to each industry. CBK's Razor files, assets, styles and private source history must **not be copied or mirrored** into the public `business_saas` repository. Reimplement approved principles using React, TypeScript and Bootstrap with our own components.

## Patterns to preserve

| Observed CBK pattern | SaaS implementation standard |
| --- | --- |
| Separate color theme and screen/layout styles | Small central design-token CSS file; component styles scoped and minimal |
| Blue/white navigation and restrained surfaces | Professional, neutral enterprise theme without excessive gradients, cards or decoration |
| Sidebar + top bar + page title | Consistent navigation shell, compact active context, accessible mobile navigation |
| Separate English / Arabic and RTL stylesheet | Data-driven multi-language packs with RTL for Arabic/Urdu; language switch near user controls |
| Explicit roles and different actions by permissions | Subscription + module + role + branch-aware navigation and backend authorization |
| List toolbar: search, clear, new item | Predictable reusable list header, filter/search state, one primary action |
| Dense, readable item tables with server pagination | Responsive data table, status, clear row actions, search and pagination |
| Distinct empty list vs no matching results | Contextual empty state with relevant next step |
| Compact labeled forms with inline validation | Autofocus, edit prefill, keyboard support, meaningful errors and minimal fields |
| Purchase order header + line editor + totals + action footer | Transaction form sections with line management, validation and sticky/visible totals where useful |
| Stock transfer displays available stock and blocks invalid quantity | Show necessary business context beside input and validate before submission; server rechecks |
| Explains when transfer actually changes stock | Plain-language hints for non-obvious posting and approval rules |

## Differences from CBK (must not blindly reproduce)

1. **CBK is inventory-oriented; Business SaaS is multi-business.** Salon, retail, repair, studio, sports rental and other industries need tailored navigation and journeys.
2. **Tech stack is different:** CBK uses ASP.NET Core MVC/Razor views. Business SaaS frontends use React + TypeScript, Bootstrap 5 and a small custom design system. Do not port Razor files directly.
3. **Customer-facing onboarding** needs much simpler step-by-step registration than an internal enterprise ERP screen.
4. **POS** needs a cashier-optimized screen, catalog/cart, speedy keyboard/scanner entry and safe payment retry; do not reproduce a standard inventory form.
5. **SaaS Super Admin** needs searchable businesses, plans, modules, usage, payments and support, not CBK's stock-oriented menu.
6. **Mobile** layouts must support short workflows and touch users; a desktop-only table cannot be the default experience for all roles.
7. **Language system** is not restricted to English/Arabic; new translation packs must upload/publish without a frontend deployment.
8. **No public use of private CBK logos, screenshots, source snippets or branded assets** unless separately approved.

## Concrete screen templates

- **List:** page title + primary action + inline search/filter + readable table/list + pagination + correct empty/loading/error states.
- **Create/edit:** heading + scope indicator + compact labeled fields + grouped sections + inline errors + clear save/cancel footer.
- **Transaction:** summary/header fields + line editor + visible computed totals + clear posting/approval explanation + safe save/retry states.
- **Dashboard:** only role-relevant operational metrics and exceptions; each card or count opens the corresponding work list.
- **POS:** search/scan/catalog + cart + visible totals + direct cash/card action + optional split payments + reliable invoice/receipt state.
- **Platform setup wizard:** sign up -> business category -> name/country/language -> plan/trial -> first location -> relevant service/product setup.

## Implementation standards

- Prefer Bootstrap responsive grid/forms/utilities, Lucide icons, one cohesive component library and logical CSS properties.
- Consistent inputs, typography, spacing, border treatments, focus indication and semantic status colors.
- No unnecessary gradients, hover-only actions, animations, giant dashboard cards or nested dialogs.
- Keyboard first-input focus, predictable tab flow, edit prefill, visible save state and confirmation only for risky actions.
- Server-side searchable and paginated data for growing datasets; counts and totals always computed accurately.
- Preserve business semantics: available stock, order/receive/remaining quantities, payment balances, staff/room availability, and permission requirements are shown **before** the user commits.
- Check every new screen against [UX Standards](./UX_STANDARDS.md) and [Engineering Guardrails](./ENGINEERING_GUARDRAILS.md).

## Validation and acceptance

1. Create representative new Business SaaS wireframes for Platform Admin, Business Admin and POS **from the above principles**.
2. Compare each against CBK's clarity/compactness by a human reviewer; review actual captured browser layouts, not static code alone.
3. Verify 1366px laptop, tablet and small phone widths; ensure no broken forms or page overflow.
4. Verify English, Arabic RTL and a third uploaded language; check focus, touch target, tables and print.
5. Run representative user tasks and record errors, misclicks and completion; use results to improve, not merely approve visually.
6. Review the Git diff for copied CBK assets/content and unrelated files.
7. **Source remains read-only**. All writes, commits and code implementations happen in `dharmendra1031/business_saas`.

## Decision log

- **UX-REF-001:** CBK `development` is the provisional primary UI/UX reference.
- **UX-REF-002:** Reimplement interaction patterns independently for React, do not copy private source into a public repo.
- **UX-REF-003:** Business workflows and role requirements override cosmetic similarity to CBK.
- **UX-REF-004:** Change this reference only after reviewing real browser screens and usability evidence.
