# Super Admin — Businesses and Creation Wizard: Approved-Image UI/UX Comparison

**Reviewed:** 22 November 2026
**Target:** `development` branch, `SA-03` through `SA-08`.
**Approved image:** the user's **final 21 November Super Admin 12-screen composite** in the Business SaaS project conversation (tile **2. Businesses**, tile **3. Create Business (Step-by-Step)**); the approved dark-navy/violet visual language is scoped to Platform Admin.
**Source:** `frontend/web/src/PlatformAdmin.tsx`, `PlatformCreateBusiness.tsx`, `styles.css`, `backend/BusinessSaas.Api/Features/PlatformBusinessProvisioning.cs`.
**Method:** reviewed original reference image and actual repository source/CSS. **No live browser screenshot comparison or owner visual sign-off has occurred.** Code-level style/markup comparison is not pixel-level verification.

## Comparison to FINAL image

| Reference feature | Implemented source | Result | Remaining gap |
| --- | --- | --- | --- |
| Navy top bar and compact fixed platform sidebar | Shared app shell; scoped `.platform-shell` navy | Source match | Actual desktop/mobile screenshots to compare |
| Violet-blue selected Businesses navigation and primary action | Platform-only CSS scoped violet; `Create Business` button in directory | Source match | Browser visual/contrast verification |
| Clear Businesses page header + primary `Create Business` CTA | `PlatformBusinesses` | Source match | Hosted browser visual check |
| Searchable/paginated business rows, owner, plan and status | Server-side name and owner email search, type, plan and subscription status filters, and filtered paging | Partial | Hosted browser visual check |
| 4-step horizontal Create Business progress bar | `provision-steps` and 4 numbered stages | Source match | Stepper screenshot and keyboard/mobile checks |
| Step 1: Business details, business type, market, currency/timezone | Real API-backed form in `PlatformCreateBusiness` | Source match | Exact field size/spacing and error handling browser QA |
| Step 2: Owner & Access | Exact email account lookup with verified-owner enforcement | Partial | No account invitation/activation/email verification delivery yet |
| Step 3: Plan & Location | Server-configured Starter trial duration and first location input. Confirmation shows the persisted expiry in the business timezone. | Partial | Published plan selection and entitlement display |
| Step 4: Review + controlled provision | Review, audited provisioning, confirmation with Business ID, and same-request retry after an uncertain outcome | Partial | Hosted workflow and approved-image comparison |
| White compact cards, mild borders/shadow and responsive adaptation | `provision-form`, `provision-grid`, breakpoints 1050/680 | Source match | Must test 1536/1366/768/360 actual browser screenshots |
| Illustrative businesses/prices in reference | UI uses only API values; no mock table values | Correct policy | No fake revenue, dummy owner or sample transaction values |

## Real workflow and security review

1. `POST /api/platform/businesses` and `GET /api/platform/owner-lookup` check `PlatformOwner.IsOwner`, which requires authenticated configured Super Admin plus MFA.
2. Owner must exist and have `EmailConfirmed=true`; current public signup doesn't offer verified email delivery yet. **This is an actual dependency, not a silently working invitation feature.**
3. Current business schema's `CreatedByUserId` is used as the owner association in existing queries. Provisioning assigns it to the verified business Owner and records **actual Super Admin actor** in `PlatformAuditEvents` in the same transaction. Migrate field semantics explicitly in a future planned schema change, not by renaming silently.
4. The former Owner self-service POST route returns Forbidden for authenticated callers in every environment. Integration tests use the real platform route for provisioning and seed existing tenants for separate feature tests.
5. Amounts/plans: only **Starter trial** is supported by the new wizard; no invoice, paid activation, transfer or multi-plan selection is claimed.
6. The frontend Owner portal no longer exposes `Create Business`. The server remains the authority; no role selection in the gateway grants permission.

## Next acceptance gates — never mark DONE prematurely

- [ ] Render a real logged-in Super Admin browser session and capture desktop screenshots at 1536×864 and 1366×768.
- [ ] Render tablet 768px and Android-like 360px, with complete navigation, stepper and form keyboard/touch checks.
- [ ] Compare placement, background, nav highlight, font hierarchy, card density, whitespace and CTA sizes with the **final** composite, not an unrelated design.
- [ ] Verify owner verification failure, missing/unsupported market, missing types, trial config unavailable, duplicate request and API timeout state; review UX messages.
- [ ] Run frontend build, .NET backend/integration tests and SQL Server tests on the final commit; record CI job URL.
- [ ] Finish verified invite/activation flow and plan-selection/entitlement policy.
- [x] Remove the Development-only legacy fixture path.
- [ ] Product owner signs off the visual/browser result before status becomes **DONE**.

**Screen progress:** SA-03…SA-08 remain **PARTIAL**, not DONE; visual and workflow gaps are explicitly open.

## 22 November follow-up: wizard UI/UX refinement

**Reference checked:** approved final Super Admin Businesses/Create Business design decisions in `APPROVED_UI_UX.md` and the earlier composite comparison above. This pass is **source-to-reference requirements comparison**, not a newly captured browser screenshot or pixel-perfect image diff.

- **Review & Create:** added an explicit owner/business confirmation checkbox; the final submit button is disabled until the operator confirms. Matches the reference's deliberate, single primary action and prevents accidental provisioning.
- **Owner verification:** the wizard now rejects a lookup response unless `verified` is true; server-side verified-owner enforcement remains mandatory.
- **Business details:** the wizard checks the selected business type against the actually loaded API list, not just a positive numeric ID.
- **Network uncertainty:** a warning now explains that an interrupted create request may have succeeded and that retries reuse the same idempotency key. The warning is not a claim that the operation failed.
- **Accessibility:** added a screen-reader-readable step count and a keyboard-focusable, labeled confirmation control.
- **Visual scope:** new confirmation styling is restricted to the platform wizard; it does not change the approved POS layout or Business Owner dashboard.

**Still not accepted:** real-browser screenshot comparison at 1536/1366/768/360, owner invitation delivery, non-Starter plan selection, payment activation, and product-owner sign-off. Keep SA-03…SA-08 **Partial**.

