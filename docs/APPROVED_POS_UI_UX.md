# APPROVED — Business SaaS Retail POS UI & Payment Modal

**Approval date:** 21 November 2026  
**Status:** APPROVED VISUAL DESIGN TARGET; implementation not yet complete  
**Approved by:** Business SaaS product owner in the project conversation  
**Target repository/branch:** `dharmendra1031/business_saas` / `development`  
**Reference image:** `BusinessSaaS_POS_Approved_2026-11-21.png` (original approved PNG retained with the conversation; do not treat this Markdown document as embedding the image)  
**Reference PNG SHA-256:** `b0bab91781b7bb2eb285a3813e863399f21855a32621f7e2e7aefcf7c003bc53`  
**Layout reference:** `dharmendra1031/ibdaa-atelier-pos` `development`, `docs/design-reference/pos.png` and `src/IbdaaAtelier.Web/Views/Pos/Index.cshtml`. Design reference only; do not copy private source or assets.

## 1. Approved visual structure (freeze)

The product owner approved the generated POS mockup shown on 21 November 2026 and explicitly said: "Ye thik hai ise save kr lo same aisi hi pos hogi." Preserve its layout, hierarchy, density, field placement and enterprise navy / indigo / white visual language. A later instruction explicitly adds a **Payment Popup (modal)**.

- **POS is a distinct cashier workspace**, not the Super Admin dashboard and not the Business Owner dashboard. Remove the persistent admin sidebar from the POS layout. Keep a compact navy top header with product branding, current business and location, cashier identity, and real supported shortcuts.
- **Exactly two primary desktop content areas:** catalogue roughly **61% left** and current basket/checkout roughly **39% right**. Full-width within the viewport; proportional responsive adaptation at smaller widths. A modal temporarily overlays this layout during payment.
- **Left catalogue:** clear Products heading, search/barcode entry, available category filters, real product-card grid with product image (honest fallback if absent), name, code, configured sale price and real location-specific stock. Click/tap product or keyboard selection to add. Do not require an additional generic product dropdown for normal scanning. Efficient scrolling stays within the catalogue, preserving an accessible cart.
- **Right basket:** New Sale heading, customer/walk-in selector if supported, current items with name/code, quantity change and remove, item totals, subtotal, server-authoritative discount/tax when implemented, total payable, and one dominant **Pay / Checkout** action that opens the modal. Show current branch and correct currency.
- **Visual polish:** clear spacing and consistent field heights; compact useful controls, not giant decorative cards; restrained borders, accessible contrast, visible focus states, predictable loading/empty/error states; use the application's existing React/TypeScript/Bootstrap and Lucide stack. No duplicate business logic.
- **Do not copy mock data:** pictured garment items, illustrations, stock numbers, rupee amounts, discounts, GST percentage, invoice identifiers, terminal identifiers and shortcut labels are examples only. Use real API/SQL values, configured currency and permissions. In Retail V1, unsupported tailoring Services, promotions, variants, discounts, UPI/card/split payments, hold bills, or terminals must not appear as working controls.

## 2. Payment Popup — approved additional requirement

**Trigger:** The cashier selects Pay / Checkout from the basket when it has valid sellable items. The modal is not a separate page and does not alter or post the sale merely by opening.

**Presentation:** Premium centered dialog with backdrop; compact enough for ordinary desktop, scrollable on short/mobile screens; focus is trapped while open, Escape/Cancel safely closes it, and focus returns to Pay / Checkout. Cart context remains intact. Place the payment modal above the cashier's two-column POS, not on top of a generic admin dashboard.

**Information hierarchy:**
1. Header: **Complete Payment**, reference to the current sale as a draft, close button.
2. Prominent **Total Payable** in the selected business currency; selected items/summary accessible without hiding the amount.
3. Payment method selector: **Cash**, and **Card / UPI / Split** only when their complete authorized backend and transactional rules exist. Unsupported methods must be absent or explicitly disabled with honest labels, never simulated as successful.
4. **Cash received** input (with appropriate monetary precision), large **Change to return** for overpayment; insufficient tender is blocked. For later authorized partial/split methods, show **Amount Paid** and **Balance Due** with precise validated rules, but do not invent an unpaid/credit flow.
5. Footer: **Cancel / Back to Cart** and one prominent **Confirm Payment** action; after verified server commit, show success, receipt number and an explicit **Print Receipt / New Sale** continuation.

**Transaction and error safeguards:**
- Server owns price, tax, totals, stock availability, permissions and payment state. Recheck on final submission and explain any changed amount.
- Idempotency key and pending/unknown transaction recovery must prevent duplicate invoices, stock deductions or charges on double-click, timeout and retries.
- Disable repeat confirmation while submitting; preserve modal/cart state on errors and show a usable retry/reconciliation path. An unknown outcome must not be labelled failed or successful without verification.
- **Print only from persisted committed receipt/invoice**, and printer failure must not duplicate the sale.
- Never enable live Card/UPI/Split with fake integrations or treat UI button selection as proof of settlement.
- Existing cash-sale backend currently works only in Development and does not yet implement all modal/payment capabilities. Design approval is **not** a claim of implemented functionality.

## 3. Three separately identifiable workspaces

1. **Platform Super Admin:** platform business creation/onboarding responsibilities; separate platform login/authorization, not cashier POS.
2. **Business Owner / Admin:** separate role-appropriate login/workspace for their business's products, inventory, purchase, staff and reporting; not a cashier workspace.
3. **POS / Cashier:** separate cashier-first full-width transaction workspace; role enforcement must happen in backend, not only nav visibility.

The owner said Super Admin should be for business creation and the Owner/Admin should have a separate login. Treat this as a product requirement to reconcile with existing platform controls and authentication before changing backend permissions. Do not silently remove existing security/admin controls on the strength of a mockup.

## Frontend implementation evidence — 22 November 2026

The following source-level changes have been committed on `development` against this target:

- `App.tsx`: POS-specific topbar and full-width cashier route **without permanent admin sidebar**; Owner/Manager can return to the business dashboard, Cashier can switch to assigned businesses, with unposted basket discard confirmation.
- `CashCheckout.tsx`: real products rendered in a grid; barcode/SKU exact-search action; stock-aware card selection, cart quantity/remove/clear, total and payment-modal trigger. Real API supplies up to 30 matching active products per query with SKU, barcode, sale price and selected-location stock.
- `PosCashPaymentDialog.tsx`: cash-only tender dialog, change and total, keyboard Tab/Escape and focus-return behavior, background-scroll lock, cancel without posting and confirmation using the original receipt-posting function.
- `styles.pos.css`: scoped 61/39 desktop grid, compact cards, sticky basket, short-screen/mobile stacking and centered responsive modal.

**Intentional gaps:** the server currently does **not** return product images, category facets or a customer selector for this cash-sale API, so the UI uses plain product icons and does not invent categories, customer data, discounts, Card/UPI/Split or real production availability. Pending-sale reconciliation, idempotent posting and receipt printing reuse existing logic. The API is still Development-only.

**Acceptance status remains not done:** no authenticated desktop/mobile browser screenshot comparison to the approved PNG, live cash transaction E2E or receipt-printer acceptance has been recorded yet. Keep the checklist below unchecked until those actual tests pass.

## 4. Implementation acceptance checklist

- [ ] POS uses the approved full-width **61/39** layout; **no permanent sidebar**.
- [ ] Actual product grid/card heights, search, category navigation and basket match the approved design at desktop 1536×864 and 1366×768.
- [ ] Payment popup opens from Pay / Checkout; Cancel returns to same cart; Escape/focus and screen-reader dialog labels work.
- [ ] Cash amount, change, validation, posting, receipt print, retries, pending recovery and duplicate prevention work end to end with real backend/SQL.
- [ ] Unsupported payment methods, services and faux screen actions remain unavailable until implemented.
- [ ] Layout tested at 360px, 768px and desktop with real cashier workflow; no inaccessible pay button or clipped cart on short screens.
- [ ] Real authenticated browser tests plus backend and SQL Server integration tests PASS; CI and visual acceptance recorded.
- [ ] Super Admin and Business Admin remain clearly separated from Cashier POS and server-enforced permissions reviewed.

**Development policy:** Only change `development` branch. Keep one API and one web app, no speculative UI framework/duplicated checkout code, and do not alter financial logic merely to make the screen match the reference.
