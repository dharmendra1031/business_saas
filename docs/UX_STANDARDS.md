# Business SaaS — Core UX Standards and Salon Screen Examples

> **Scope notice:** The generic usability and accessible UI criteria apply to every Business SaaS app. Salon-specific screen matrices remain **vertical examples**, not menus that Retail and Repair must inherit. The first retail workflow is in [Retail V1 SRS](./RETAIL_V1_SRS.md). CBK is [read-only design reference](./CBK_UI_REFERENCE.md); reimplement in React without copying private assets.

**Status:** Mandatory design and implementation standard; shared feedback component implemented, individual screens still migrating and require acceptance.  
**Date:** 2026-11-25  
**Repository:** `dharmendra1031/business_saas` ONLY  
**Scope:** Platform Super Admin, Retail Business Owner/Manager/Cashier, POS, Salon, Repair and future supported applications  
**Product aim:** Every authorized user can complete daily business tasks quickly, confidently and consistently without intensive training.

## 1. Guiding rule: workflow first

Design and test **tasks**, not individual pages in isolation. Every screen must answer:
1. Where am I? (module, salon, branch, active business day)
2. What is the most important next action?
3. What data or input is required?
4. What will happen when I commit/save/charge?
5. Did it succeed, fail or remain uncertain?
6. How do I fix an issue or return without losing work?

A clean UI is not merely a white/blue theme. It requires familiar language, predictable behavior, visible state, low memory burden, error prevention, accessible controls and recovery from mistakes.

## 2. User roles and jobs to be done

| Role | Primary jobs | UX must prioritize |
| --- | --- | --- |
| Platform Admin | Activate salons, manage plans/modules, resolve billing/support issues | Searchable tenant directory, clear subscription state, secure scoped actions, auditable changes |
| Salon Owner | See operations, manage branches/users/services, understand revenue | Clear business summary, guided setup, direct links to action, role-aware reporting |
| Branch Manager | Appointments, employees, stock, approvals and closing | Branch visibility, pending tasks, exception-first dashboard, concise actions |
| Receptionist | Find customer, create/change appointment, check in | Fast name/phone search, schedule availability, conflict-free bookings |
| Cashier | Scan/select items, take payment, invoice, hold, refund, close register | Speed, totals always visible, keyboard support, reliable payment feedback |
| Staff / Stylist | View schedule and work assignments | Minimal navigation, time-specific tasks, readable status |
| Customer | Book, reschedule, pay (if offered), view benefits | Mobile first, clear times/prices, concise forms, confirmation messages |

Do not force every role through the same feature-rich navigation. **Modules granted by the SaaS Plan and permissions determine which actions appear**, but all access must be enforced server-side.

## 3. Navigation rules

- Separate Platform Admin, Salon Admin and POS experiences. Shared design tokens are allowed; their main navigation does not have to be identical.
- Group by *business task*, not technical tables or implementation folder names.
- No duplicate menu labels/routes with ambiguous meaning; no generic "Manage" or "Tools" as the only path to a common function.
- Keep the primary daily actions prominent; advanced controls live in contextual menus where appropriate and remain discoverable.
- Show current salon, branch, location/business date and role where relevant.
- Search results must navigate to the exact task, not merely open a broad section.
- Preserve the selected tab/filter/scroll where safe and expected; warn about unsaved changes.
- Search for a specific customer, appointment, invoice, item or tenant must be available from its relevant workspace.

## 4. Global screen contract — required for every screen

1. **Header:** clear page name, branch/salon scope, breadcrumb only if useful.
2. **Primary action:** at most one visually dominant action per work area; context-specific actions secondary.
3. **Data:** concise labels, correct units/currency/timezone, meaningful descriptions and consistent formatting.
4. **Search:** predictable, debounced when network-backed, keyboard-focusable, explicit no-results feedback.
5. **Table/list:** server-side pagination for large data, visible counts, sorting/filtering, row actions with readable names.
6. **Forms:** first meaningful input focused, logical tab order, correct edit prefill, required/optional indicator, validation next to the field.
7. **Saving:** loading/disabled repeat submit, server-confirmed outcome, clear success feedback, preserve state on failed save.
8. **Errors:** use human language, precise issue and fix; technical stack traces never reach users.
9. **Empty state:** distinguish "no records" from "no results for these filters" and "could not load data".
10. **Confirmation:** destructive, irreversible or financially sensitive actions explain what will happen and the affected entity.
11. **Permissions:** actions outside permission are absent or explicitly read-only with reason; no dead buttons or false affordances.
12. **Help:** concise contextual explanation for uncommon business concepts, not generic manuals on every page.
13. **Responsiveness:** forms/tables remain usable on desktop, laptop, tablet and small phones. Wide data tables may use a controlled horizontal scroll when necessary; never break the entire page.
14. **Keyboard and assistive tech:** correct labels, dialog focus trap/return, visible focus, escape/close behaviors, status announcements.
15. **Localization:** no clipped labels or hard-coded English; LTR/RTL layout, numbers, dates, money and print views verified.
16. **Design:** one small style system; restraint with decorative cards, animations, gradients and shadows.
17. **Refresh/retry:** show live operation status. A lost connection must not be mistaken for a failed payment or successful write.
18. **Drafts:** sensitive or lengthy edits receive unsaved-change protection or explicit save-as-draft where appropriate.
19. **Authorization:** UI hiding is never the only security control. Verify active module, user permission and branch tenant access on API.
20. **Performance:** do not block checkout on nonessential dashboard/report requests.

## 4.1. Mandatory selection overlay rule (25 November 2026)

- Opening any dropdown, suggestion list, date picker or record lookup must not change the underlying form, row or page height, move surrounding controls, or create document overflow.
- Small fixed choices use native selects. Large master-record selections use the CBK-style search-and-select popup, with a single search field and a result table. Do not render an expanding inline result list that pushes the form down.
- Custom menus must be overlays positioned outside normal layout flow, with viewport collision handling; do not clip them inside table or card overflow containers. Modal record lookups use the native dialog top layer and the existing shared lookup implementation.
- Keep overlays within the visible viewport. Scroll results inside the popup, not the underlying page. Support Escape, keyboard navigation, accessible names and appropriate focus restoration; selection must preserve the current draft.
- Acceptance: compare the underlying form bounds and document scroll height before and while opening the popup with results. Verify desktop, tablet, mobile and RTL layouts. Opening or closing a selection control must cause no layout shift. Do not mark a screen verified solely from a passing build.
- Fix the original control and styles, remove superseded code after checking callers, and do not add duplicate selection libraries or CSS overrides.

## 4A. Enterprise-level shared Save / Update feedback contract (approved 24 November 2026)

All form mutations (Create / Update / Save / Approve / Remove) must use one **consistent visual language**, not copy-pasted popup code in each feature. The shared implementation starts at `frontend/web/src/ActionFeedback.tsx`; it is mounted **once** in `main.tsx` for Platform, Business Owner and Cashier workspaces. New CRUD forms should call `useActionFeedback().run(savingLabel, successLabel, asyncRequest)` and use the shared `ActionButton` for accessible processing/disabled states. Migrate existing forms incrementally with browser QA; do not assert every form is already converted.

| State | UX / safety requirement |
| --- | --- |
| Idle | Explicit verb/action label, only valid and authorized actions enabled; no surprise auto-submit |
| Saving | Immediate visible button spinner and processing label; disable repeated submit, cancel or navigation that could discard transaction context; global non-blocking saving popup; `aria-busy` where applicable |
| Success | Show shared success popup **only after confirmed server response**, readable record/result name and next action; no second redundant success alert; routine successes dismiss automatically after 5 seconds with a manual accessible close |
| Failure / validation | Persistent shared error popup, human-readable server message, manual dismiss, preserve all entered values and selected options; field errors remain next to their fields; no success feedback on failure |
| Network unknown | For purchases, payments, POS/credit posting, refunds and other non-idempotent effects: never say "failed" or "saved" from a lost response alone; retain operation key, check recorded status and reconcile before reattempt |
| Permission / suspended plan | Server denies write; UI disables appropriately, existing history remains read-only where authorized; never rely on hidden button for security |
| Destructive action | Explicit context/confirmation, no silent delete; cancel safely; actor and audit trail where required |

**Interaction quality:** Buttons retain shape/width between idle and saving; focus rings visible; status and error announcements use appropriate ARIA; dismissal is keyboard-accessible. One popup surface per action, no full-screen modal for routine fast CRUD saves. Long-running financial decisions may require an explicit modal with its own progress and outcome states. Mobile 360px layout must not horizontally overflow. Respect `prefers-reduced-motion`, near-black text on white surfaces and high-contrast primary actions. New forms should reuse these primitives rather than adding a second toast library, notification context or spinner component.

**Acceptance tests per migrated screen:** delayed API response (saving state persists), repeated click (one request), success after commit, validation/409 failure (values preserved), lost response without false success, Escape/focus and mobile width, and correct disabled/readonly behavior by role. The parent screen must not disappear unexpectedly after save. Browser fixture QA is separate from authenticated hosted and real-user acceptance.

## 5. Platform Admin — screen-by-screen goals

| Screen | Most important user path | Anti-confusion checks |
| --- | --- | --- |
| Dashboard | Spot trials expiring, renewal failures, platform exceptions | Actionable counts linked to matching filtered records |
| Salons | Find salon by name/code/contact, view status, act | No accidental cross-salon data view, visible current plan and renewal |
| Salon Details | View subscription, branches/usage, billing, entitlements | Sections labeled by purpose; auditable overrides |
| Plan Master | Add/edit/retire plan, map modules and usage limits | Preserve existing contract versions; diff before publish |
| Module Master | Enable/disable module eligibility and dependencies | Show impact and dependent modules before change |
| Subscriptions | Trial/active/grace/past-due/suspended state transitions | Effective date, customer impact and reason displayed |
| Invoices & Payments | Search, reconcile, review failed payment | Provider status, safe retries; no duplicate charges |
| Add-ons & Usage | See limits, add branch/terminal credits | Current usage vs limit clearly separated |
| Language Management | Upload, validate, preview, publish, rollback language pack | No publication when placeholders invalid; RTL preview |
| Support & Audit | Inspect ticket and authorized change history | Escalation without blanket access to tenant data |
| Monitoring | Find failures and affected salons | Alerts with actionable links and timestamps |

## 6. Salon Admin — screen-by-screen goals

| Screen | Primary workflow | Anti-confusion checks |
| --- | --- | --- |
| Dashboard | Today's bookings, sales, staffing alerts, incomplete tasks | Summary counts accurate to selected branch/date |
| First-run setup | Branch > hours > services/prices > staff > payment methods | Step progress, optional vs required, resume safely |
| Branches | List/add/edit branch and business hours | Clearly show active branch and impacts of switching |
| Users & Roles | Create user, assign branches and least privileges | Preview resulting access; clear missing permissions |
| Customers | Search/create/open customer record and history | No forced duplicate records, phone normalization |
| Services & Pricing | Create service/category, set duration, branch price | No ambiguous "Packages" route; price preview |
| Appointments | Calendar/list > available slot > book > confirm | Availability shown before save; conflict explanation |
| Employees | Profile > skills > shifts > branch assignment | Valid shifts/skills, compact forms, correct edit state |
| Attendance | Check-in/out and corrections with reason | Current status/date/branch visible |
| Products | Search > stock > edit > status | Unit/tax/branch context visible |
| Purchase Orders | Supplier > items > draft > submit/approve | Stock not updated until defined receipt posting |
| Goods Receipt | PO > remaining qty > partial/full receipt | Remaining quantities and posting results shown |
| Stock & Transfers | Balance > request > approve > ship/receive | Available quantity visible; prevent over-transfer |
| Membership & Loyalty | Plan/benefit set up > issue/redeem/return | Entitlements clear, no accidental double redemption |
| Reports | Select report > branch/date/status > preview/export | Totals server-side, filters retained, accurate timezone |
| Settings | Taxes, methods, language, templates, subscription | Separate operational settings from SaaS plan billing |

## 7. POS — optimize the default flow

**Primary workflow:** search/scan/select service or product -> choose staff when required -> customer or walk-in -> review clear total -> select payment -> server-confirmed sale -> receipt.

### Checkout requirements

- Search box focused by default, supports scanner/keyboard; service/product categories clearly distinguished.
- Cart visible on desktop; narrow screens use predictable catalog/cart switching with a visible cart total.
- Show line quantity, stylist/employee, price, discounts, tax and total. No hidden pricing surprises.
- Existing customer lookup by phone/name with simple opt-in for create-new.
- **Cash and card** provide the shortest sensible standard flow. Split payment remains deliberate and clear.
- Show bill amount, remaining amount, tendered cash and change without requiring mental math.
- Discounts/override require visible reason and authorization as appropriate.
- Register closed/locked state discovered *before* the final pay action, with an immediate guided remedy.
- When payment is processing, lock only conflicting actions and show progress.
- When network outcome is unknown, show **"Checking payment / sale status"**, not "Failed, pay again"; reconcile with server before retry.
- "Held Bills", "History", "Cash Register" and "Refund" accessible by clear labels, not buried in a generic multi-tab tool dialog.
- Receipt printing is separate from sale success. Printer failure must not create a duplicate invoice.
- Refund/void flow identifies original invoice, eligibility, permitted amount, stock return and confirmation.
- Handle multiple cashier sessions, branch switches and held bill restoration deliberately.
- Do not silently truncate catalog or invoice history to a fixed client-side cap.
- Consistent bilingual labels; POS must work with 80-mm printing, keyboard, touch and RTL.

## 8. User-friendly forms and interaction

- Prefer short, grouped forms. Required information first; less frequent settings collapsed with clear headings.
- Auto-populate only from trusted settings/current record, and explain editable defaults.
- Prevent double submit, misplaced focus, ambiguous date inputs and losing edits.
- Inline error explains which field and why; preserve all user input on server validation failure.
- Buttons: "Save Service", "Collect Payment", "Approve Transfer" instead of a generic "Submit".
- Use clear statuses: Draft, Pending, Approved, Partially Received, Completed, Cancelled, Refund Pending. Translate consistently.
- Avoid unexpected navigation, random pop-ups and invisible hover-only actions.
- Define required keyboard shortcuts, show them near actions and allow shortcuts to be disabled if conflicting.

## 9. Accessibility, language and RTL

Target **WCAG 2.2 AA** as a practical baseline for web interfaces; do not claim conformance without actual testing.

- Contrast, labels, role semantics, keyboard operability and focus visibility.
- Touch target sizing and spacing; small screens and touch POS hardware.
- Screen readers receive informative success/failure/status announcements.
- English, Arabic, Hindi and other published languages load via the separate Language Management system.
- Validate typography and bidirectional numbers within mixed-script input, receipts and PDF reports.
- Avoid text embedded in images; ensure translation expands without overlap or clipped buttons.
- Show user locale separately from branch currency/business timezone.

References:
- Nielsen Norman Group usability heuristics: https://www.nngroup.com/articles/ten-usability-heuristics/
- W3C WCAG 2.2: https://www.w3.org/TR/WCAG22/

## 10. Measurable UX quality gates (proposed launch targets)

These are **targets to test with representative users**, not claims that current Kaifac or Salon SaaS meets them.

| Journey | Working target | Verification |
| --- | --- | --- |
| First-time cashier, basic sale | Successful cash sale without trainer after short orientation | Moderated 5-user usability test + end-to-end test |
| Experienced cashier, basic sale | Median active interaction time under 30 seconds for a prepared basket (exclude network/printer latency) | Timed task runs, defined scenario |
| Returning customer lookup | Correct match from a visible search path without full-page navigation | Usability test, duplicate-name scenario |
| Appointment booking | User sees slot conflicts before committing, can correct without reentering all data | Browser test + representative user test |
| Goods receipt | User sees ordered, received and remaining quantity on same task screen | Moderated test + integration fixture |
| Refund | Correct invoice and eligible amount identified before submit | Scenario test + cashier/manager permission checks |
| Module disabled | No misleading clickable entry; direct API denies action | Browser + authorization suite |
| Branch change | Current branch remains visible; unsaved-change risk explained | Browser role/branch suite |
| Language switch | No broken RTL or lost form state in top workflows | English + Arabic screenshots/keyboard test |
| Accessibility | WCAG 2.2 AA prioritized, no critical keyboard/focus blockers in core journeys | Automated scans + manual assistive-tech review |
| Performance | Keep common UI responsive; set actual API latency budgets after baseline | Measured browser/API performance runs |

No arbitrary "3-click maximum" across every workflow: high-risk financial actions may need extra confirmation. Optimize **cognitive load and correctness**, not minimum clicks at any cost.

## 11. Required UX acceptance per screen

Before marking a screen DONE:

1. Identify role, task and expected outcome.
2. Review wireframe/interaction flow before coding.
3. Test real create/edit/view/search/filter/empty/error/loading states.
4. Test keyboard navigation, focus, first edit field, dialog close, and destructive action warnings.
5. Test slow API, duplicate submit, permission denied and recovery.
6. Test representative phone/tablet/desktop widths and RTL.
7. Confirm API contract, persistence, error handling and no financial side effects.
8. Run automated functional tests and a task-based manual/usability check; record outcomes and remaining issues.
9. Refuse "done" if user cannot identify the next action, error recovery is unclear or business data is inaccurate.

**Never report screen quality as verified based only on code review or unit-test success.** Browser experience and real user testing are distinct gates.

## 12. Initial implementation priorities

P0: POS checkout/payment/register correctness and onboarding, SaaS tenant isolation, critical permissions.  
P1: Admin navigation, task-based forms, appointments, customers, products, inventory/purchase and reporting usability.  
P2: Progressive onboarding, shortcuts, in-product contextual help, mobile refinements and low-frequency analytics.  

## Decision

The project deliberately prioritizes **simple and trustworthy daily tasks over flashy enterprise-looking screens**. Usability is a release requirement and will be verified with representative salon owner, manager and cashier scenarios. Work remains exclusively in `dharmendra1031/business_saas`.
