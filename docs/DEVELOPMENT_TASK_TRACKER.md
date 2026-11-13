# Business SaaS — Development Task Tracker (190 Tasks)

**Updated:** 13 November 2026  
**Repository:** `dharmendra1031/business_saas` · **Source of truth:** [Development Roadmap](./DEVELOPMENT_ROADMAP.md), [Master SRS](./BUSINESS_SAAS_MASTER_SRS.md), [UX Standards](./UX_STANDARDS.md)  
**Scope:** Planning and acceptance tracking, not a claim of implementation. The platform is not production-ready.

## Non-negotiable rule — UI/UX review **after EVERY task**

> **No task is DONE until its code and tests pass AND the resulting user flow is checked for clarity, usability and regressions.** A successful build alone is not task completion.

1. Review the task's changed screen **with real workflow data or honest empty state**, never fabricated sales/KPIs. If it is backend-only, smoke-test its affected screens plus login → business switch → dashboard.
2. Check **mobile 360px, tablet 768px and desktop 1440px**, keyboard tab/focus/Enter, screen-reader labels for controls, responsive tables, validation, edit prefill, loading/empty/success/error and authorization states.
3. Ask from the **actual role's perspective**: Is the primary action obvious? Are labels and status understandable? Can someone finish without instructions? Is there any misleading button, confusing navigation, unnecessary scrolling or clutter?
4. Check the entire impacted journey: what happened before, **this action**, next action, cancellation, errors/retry and returning to the list. Do not call a feature usable because it has a pretty screenshot.
5. Check English/Arabic + RTL **once supported**; until then, record locale testing as pending. Verify contrast, readable type, spacing, tap targets and accessible keyboard navigation.
6. Recheck existing critical screens after each task (login, business list/create/switch and currently live Retail flows); test unauthorized user/other business boundaries where relevant.
7. Fix any confusing UI or broken workflow **within the same task** before moving on; record proof (browser/test run, viewport/role, screenshot or concise review notes), remaining risks and commit link.
8. If actual browser/human UX review is not possible, mark **UX REVIEW PENDING** — never mark DONE or claim user friendliness verified.

**Code rules:** Small developer-style changes, simple names and existing code reuse. One .NET API and React app, no unnecessary services, abstraction, dependencies, or duplicate licensing code. SQL primary keys are numeric auto-increment; no UUID/GUID entity IDs. Every sensitive API enforces verified BusinessId and user permissions. Preserve security, financial correctness and tenant isolation.

**Status legend:** `BASELINE` = code/docs exist but full UI/UX gate not verified; `TODO` = pending; `DECISION REQUIRED` = business policy/hardware approval pending; `UX REVIEW PENDING` = tests/static review done but actual browser/role review outstanding; `DONE` = implementation + tests + real user-flow verification. Only DONE counts as complete.

**How to execute:** Work in numeric task order **within a working vertical slice**, respecting dependencies; platform security and limited plan/trial can run in parallel with Retail. Keep future Salon, Repair, BYO database and 1,000-category framework unimplemented until their dependent workflows are proven. For each finished task append commit/CI/UX evidence and change its status to DONE; do not create a new project or table simply because a task appears here.

## At-a-glance phases

| Track | Area | Task IDs | Timing |
| --- | --- | --- | --- |
| A | Product scope & architecture | BS-001–BS-010 | Now |
| B | Core engineering foundation | BS-011–BS-020 | Now |
| C | Accounts and security | BS-021–BS-030 | Now |
| D | Business creation & tenant isolation | BS-031–BS-040 | Now |
| E | Global UI/UX quality system | BS-041–BS-050 | Every phase |
| F | Super Admin platform | BS-051–BS-060 | After access baseline |
| G | Subscriptions & billing | BS-061–BS-070 | Before paid launch |
| H | Retail master data | BS-071–BS-080 | Retail V1 |
| I | Retail stock & inventory | BS-081–BS-090 | Retail V1 |
| J | Purchase & receiving | BS-091–BS-100 | Retail V1 |
| K | Retail POS & invoices | BS-101–BS-110 | Retail V1 |
| L | Returns, refunds & cashier controls | BS-111–BS-120 | Retail V1 |
| M | Retail reporting & daily operations | BS-121–BS-130 | Retail V1 |
| N | Languages, RTL & accessibility | BS-131–BS-140 | In working features |
| O | License Manager & offline Windows | BS-141–BS-150 | After secure admin |
| P | White label & dedicated SQL Server | BS-151–BS-160 | Enterprise after Retail |
| Q | Salon / Barber vertical | BS-161–BS-170 | After Retail release |
| R | Repair Center vertical | BS-171–BS-180 | After Salon readiness |
| S | Production, quality & scale | BS-181–BS-190 | Release gate |

**Batch 01:** [BS-001–BS-010 review and UX evidence](./BATCH_01_REVIEW.md). No task marked DONE without actual UI/UX verification.

**Batch 02:** [BS-011–BS-020 review](./BATCH_02_REVIEW.md).

**Batch 03:** [BS-021–BS-030 account security review](./BATCH_03_REVIEW.md).

**Batch 04:** [BS-031–BS-040 business and staff access review](./BATCH_04_REVIEW.md). Owner staff management is a working first slice, **not** complete invitation or branch assignment.

**Batch 05:** [BS-041–BS-050 UI/UX review](./BATCH_05_REVIEW.md). Static review and TypeScript CI are not a substitute for actual browser/role acceptance.

**Batch 06:** [BS-051–BS-060 Platform Admin review](./BATCH_06_REVIEW.md). The read-only directory is implemented and CI checked; changes and privileged provisioning stay locked until separately audited. Actual browser UX remains pending.

**Batch 07:** [BS-061–BS-070 subscription/billing review](./BATCH_07_REVIEW.md). Trial status code/tests verified; payment, policy, operational entitlement enforcement and live browser UX remain pending.

**Batch 08:** [BS-071–BS-080 retail master-data review](./BATCH_08_REVIEW.md). Category/Unit/Product implementations merged; Supplier/Customer under PR #4 review. Real browser UX verification remains pending.

**Batch 09:** [BS-081–BS-090 stock foundation review](./BATCH_09_REVIEW.md). Opening Stock and movement ledger merged; single-item Stock Adjustment under Draft PR #6. Guarded stock posting SQL contention test passed; UI acceptance remains pending.

**Batch 10:** [BS-091–BS-100 purchase & receiving review](./BATCH_10_REVIEW.md). PO/Partial Receiving code under Draft PR #8; browser and concurrent SQL response tests remain release gates.

## Detailed backlog

### A. Product scope & architecture — Now

| ID | Task / clear deliverable | Mandatory task-specific UI/UX check | Status |
| --- | --- | --- | --- |
| BS-001 | Lock multi-business scope | Owner समझे: एक account से अलग businesses | BASELINE |
| BS-002 | Confirm numeric IDs and BusinessId model | कहीं UUID या confusing tenant identifier न दिखे | BASELINE |
| BS-003 | Review Retail V1 acceptance flow | Login से Sale और Return तक steps स्पष्ट हों | BASELINE |
| BS-004 | Record privacy, security and release gates | Business user को तकनीकी jargon न दिखे | BASELINE |
| BS-005 | Finalize initial launch country and tax policy | Currency और tax labels market से मेल खाएँ | DECISION REQUIRED |
| BS-006 | Agree plan names, trial terms and limits | Plans तुलना करने में ambiguity न हो | DECISION REQUIRED |
| BS-007 | Confirm payment and refund policies | Payment states स्पष्ट समझ आएँ | DECISION REQUIRED |
| BS-008 | Confirm printer and barcode requirements | Cashier को scanner fallback दिखे | DECISION REQUIRED |
| BS-009 | Define supported/unsupported business types | अधूरे templates selectable न हों | UX REVIEW PENDING |
| BS-010 | Approve phase dependencies and no-overengineering rules | Menu सिर्फ usable features दिखाए | TODO |

### B. Core engineering foundation — Now

| ID | Task / clear deliverable | Mandatory task-specific UI/UX check | Status |
| --- | --- | --- | --- |
| BS-011 | Maintain one .NET 10 API and test project | पुराने pages regress न हों | BASELINE |
| BS-012 | Maintain one React TypeScript web app | Navigation consistent रहे | BASELINE |
| BS-013 | Keep SQL Server EF migrations reviewed | Migration के बाद login/setup smoke test | BASELINE |
| BS-014 | Verify numeric Identity and entity IDs | UI में IDs technical clutter न बनें | BASELINE |
| BS-015 | Keep secure environment configuration | Secret fields UI में leak न हों | UX REVIEW PENDING |
| BS-016 | Run backend and SQL integration CI | Core journey browser smoke के लिए तैयार हो | BASELINE |
| BS-017 | Run frontend typecheck and production build | हर existing screen render हो | BASELINE |
| BS-018 | Add request error logging and correlation | User को readable error, internals नहीं | UX REVIEW PENDING |
| BS-019 | Persist auth/data-protection keys for deployment | Restart पर user session predictable हो | UX REVIEW PENDING |
| BS-020 | Document Windows local setup and clean deployment | Fresh installation का first-run सरल हो | TODO |

### C. Accounts and security — Now

| ID | Task / clear deliverable | Mandatory task-specific UI/UX check | Status |
| --- | --- | --- | --- |
| BS-021 | Keep register flow and password validation | Form labels, focus और inline errors साफ हों | BASELINE |
| BS-022 | Keep login and logout flows | Wrong password पर actionable message | UX REVIEW PENDING |
| BS-023 | Complete verified email registration | Verify link के बाद next step साफ हो | TODO |
| BS-024 | Add password reset with expiring token | Recovery flow confusion-free हो | TODO |
| BS-025 | Add production-compatible MFA for privileged users | Admin security prompts understandable हों | TODO |
| BS-026 | Verify lockout and abuse/rate limiting | Lockout duration स्पष्ट दिखे | UX REVIEW PENDING |
| BS-027 | Verify CSRF and secure cookie handling | Normal form submission बाधित न हो | BASELINE |
| BS-028 | Build secure session-expired handling | Session खत्म होने पर clear sign-in redirect | UX REVIEW PENDING |
| BS-029 | Test privilege removal and revoked access | Removed user को सही access denied view | UX REVIEW PENDING |
| BS-030 | Audit login, recovery and security events | User-facing errors में sensitive data न दिखे | TODO |

### D. Business creation & tenant isolation — Now

| ID | Task / clear deliverable | Mandatory task-specific UI/UX check | Status |
| --- | --- | --- | --- |
| BS-031 | Keep business list filtered by membership | दूसरे owner के business न दिखें | BASELINE |
| BS-032 | Keep Create Business transactional | Submit पर duplicate business न बने | BASELINE |
| BS-033 | Keep idempotent business creation retry | Retry में पुराना result आसानी से मिले | BASELINE |
| BS-034 | Keep first location creation | First shop ready state साफ दिखे | BASELINE |
| BS-035 | Keep 14-day trial setup | Trial date और eligibility स्पष्ट हो | BASELINE |
| BS-036 | Verify two accounts and two businesses | Business switch पर पुराने records न दिखें | BASELINE |
| BS-037 | Add membership-based business context guard | Wrong URL पर safe access denied | UX REVIEW PENDING |
| BS-038 | Add Owner/Manager/Cashier permissions | Role के हिसाब से ही menus दिखें | TODO |
| BS-039 | Add location-specific user assignment | Selected branch unmistakable हो | TODO |
| BS-040 | Add invite and deactivate business staff | Invite status, errors, revoke action स्पष्ट हों | TODO |

### E. Global UI/UX quality system — Every phase

| ID | Task / clear deliverable | Mandatory task-specific UI/UX check | Status |
| --- | --- | --- | --- |
| BS-041 | Create one clean navigation hierarchy | Primary actions तुरंत मिलें | UX REVIEW PENDING |
| BS-042 | Define compact typography and spacing | हर viewport पर readable hierarchy | UX REVIEW PENDING |
| BS-043 | Define responsive widths 360/768/1440 | Horizontal overflow न हो | UX REVIEW PENDING |
| BS-044 | Standardize form labels and required fields | Field purpose समझने में guesswork न हो | UX REVIEW PENDING |
| BS-045 | Standardize first-field focus and keyboard order | Tab/Enter से workflow तेज हो | UX REVIEW PENDING |
| BS-046 | Standardize edit prefill and validation | Edit के दौरान data सुरक्षित दिखे | TODO |
| BS-047 | Standardize loading/empty/error states | Empty और failed state अलग हों | UX REVIEW PENDING |
| BS-048 | Standardize search/filter/pagination tables | Filter हटाना और row action सरल हो | TODO |
| BS-049 | Review dialogs, confirms, destructive actions | गलती से delete/void न हो | UX REVIEW PENDING |
| BS-050 | Do cross-role usability walkthrough each task | अगला action बिना explanation समझ आए | TODO |

### F. Super Admin platform — After access baseline

| ID | Task / clear deliverable | Mandatory task-specific UI/UX check | Status |
| --- | --- | --- | --- |
| BS-051 | Keep explicit Platform Admin permission gate | Business Owner को admin navigation न मिले | BASELINE |
| BS-052 | Add secured Platform Admin layout | Admin vs shop workspace स्पष्ट हो | UX REVIEW PENDING |
| BS-053 | Add business directory with paging | एक business ढूँढना आसान हो | UX REVIEW PENDING |
| BS-054 | Add admin business detail summary | Subscription/owner/status अलग-अलग पढ़ें | UX REVIEW PENDING |
| BS-055 | Add supported business type toggles | Unsupported type का effect साफ दिखे | TODO |
| BS-056 | Add simple plan and entitlement editor | Changes की effective date स्पष्ट हो | TODO |
| BS-057 | Add trial status and authorized adjustments | Trial extension के reason दिखें | TODO |
| BS-058 | Add platform audit history | Actor/time/change आसानी से दिखें | TODO |
| BS-059 | Add platform admin account provisioning | किसे platform access है, स्पष्ट हो | TODO |
| BS-060 | Test all platform API cross-role denials | Forbidden states में private details न leak हों | UX REVIEW PENDING |

### G. Subscriptions & billing — Before paid launch

| ID | Task / clear deliverable | Mandatory task-specific UI/UX check | Status |
| --- | --- | --- | --- |
| BS-061 | Keep subscription and Starter trial record | Plan और expiry UI consistent हों | UX REVIEW PENDING |
| BS-062 | Define exact plan/module limits | Limit wording unambiguous हो | DECISION REQUIRED |
| BS-063 | Enforce trial/active status in operation APIs | Disabled action explanatory बने | TODO |
| BS-064 | Add subscription upgrade/downgrade flow | Proration/changes से पहले summary हो | TODO |
| BS-065 | Add payment provider selection for launch market | Supported methods ही दिखें | DECISION REQUIRED |
| BS-066 | Implement secure checkout redirect/return | Pending vs success अलग दिखाई दे | TODO |
| BS-067 | Verify webhook signature and idempotency | Double charge का UI impression न बने | TODO |
| BS-068 | Add invoice and subscription payment history | Customer आसानी से proof ढूँढ सके | TODO |
| BS-069 | Implement failed-payment/retry/grace policy | Next step और deadline साफ हो | DECISION REQUIRED |
| BS-070 | Test expiry, renewal and entitlement denial | Expired user को read/export policy समझ आए | TODO |

### H. Retail master data — Retail V1

| ID | Task / clear deliverable | Mandatory task-specific UI/UX check | Status |
| --- | --- | --- | --- |
| BS-071 | Add Category master and uniqueness rules | New/Edit/List compact हों | UX REVIEW PENDING |
| BS-072 | Add Unit master and validation | Unit selection unambiguous हो | UX REVIEW PENDING |
| BS-073 | Add Product master with scoped SKU | Duplicate SKU inline message दे | UX REVIEW PENDING |
| BS-074 | Add barcode field and lookup | Scanner/manual search दोनों सरल हों | UX REVIEW PENDING |
| BS-075 | Add sale price and tax classification | Money rounding और unit साफ दिखें | UX REVIEW PENDING |
| BS-076 | Add Product Edit/Deactivate actions | Inactive meaning तुरंत समझ आए | UX REVIEW PENDING |
| BS-077 | Add Supplier master | Required contact fields स्पष्ट हों | UX REVIEW PENDING |
| BS-078 | Add Customer master for optional association | Walk-in sale customer बिना भी चले | UX REVIEW PENDING |
| BS-079 | Add product table search/filter/paging | Large catalog में fast search हो | UX REVIEW PENDING |
| BS-080 | Test cross-business master access denial | Wrong ID पर data/metadata leak न हो | UX REVIEW PENDING |

### I. Retail stock & inventory — Retail V1

| ID | Task / clear deliverable | Mandatory task-specific UI/UX check | Status |
| --- | --- | --- | --- |
| BS-081 | Create stock movement ledger design | Stock history user-readable हो | UX REVIEW PENDING |
| BS-082 | Implement opening stock with one-time rules | Opening vs Adjustment अलग दिखें | UX REVIEW PENDING |
| BS-083 | Implement branch-scoped stock balance view | Available quantity prominent हो | UX REVIEW PENDING |
| BS-084 | Implement stock adjustment with reason | Old/New/Delta before save साफ हों | UX REVIEW PENDING |
| BS-085 | Prevent negative stock under concurrency | Insufficient stock तुरंत दिखे | UX REVIEW PENDING |
| BS-086 | Add audited inventory corrections | Correction किसने किया दिखे | TODO |
| BS-087 | Add low-stock thresholds and list | Low vs out-of-stock अलग हों | TODO |
| BS-088 | Add optional location transfer only when needed | Source/destination clearly labeled हों | TODO |
| BS-089 | Verify valuation policy and rounding | Cost vs retail price confuse न हों | TODO |
| BS-090 | Test stock ledger reconciliation | Reports और stock screen totals मिलें | TODO |

### J. Purchase & receiving — Retail V1

| ID | Task / clear deliverable | Mandatory task-specific UI/UX check | Status |
| --- | --- | --- | --- |
| BS-091 | Build Purchase Order create/list | Supplier और items कम clicks में चुनें | BASELINE |
| BS-092 | Add PO draft/approve states | Approval से stock बढ़ने का भ्रम न हो | BASELINE |
| BS-093 | Implement PO item quantities and totals | Ordered/Received/Remaining अलग हों | BASELINE |
| BS-094 | Build partial goods receiving | 40+60 flow स्पष्ट दिखे | BASELINE |
| BS-095 | Block over-receiving | Exceeded quantity inline बताई जाए | BASELINE |
| BS-096 | Post receiving to stock ledger | Posted receipt status unmistakable हो | BASELINE |
| BS-097 | Add direct receiving only when authorized | Receiving type साफ लिखा हो | TODO |
| BS-098 | Implement purchase return/reversal rules | Reversal purpose/amount visible हों | TODO |
| BS-099 | Add supplier outstanding/payment view | Due vs paid स्पष्ट हो | TODO |
| BS-100 | Test concurrency and receipt isolation | Pending spinner duplicate post न करे | TODO |

### K. Retail POS & invoices — Retail V1

| ID | Task / clear deliverable | Mandatory task-specific UI/UX check | Status |
| --- | --- | --- | --- |
| BS-101 | Create cashier-first POS screen | Barcode/search field default focus | TODO |
| BS-102 | Implement fast product lookup | Search results price/unit readable हों | TODO |
| BS-103 | Implement cart quantity/add/remove | Total तत्काल update हो | TODO |
| BS-104 | Calculate discounts and tax on server | Price breakdown transparent हो | TODO |
| BS-105 | Implement cash tender/change | Pay/Change values clearly separated हों | TODO |
| BS-106 | Implement card/external tender records | External payment verified state दिखे | TODO |
| BS-107 | Implement supported split tender | Remaining amount तुरंत दिखे | TODO |
| BS-108 | Post invoice idempotently | Double tap से दूसरा bill न बने | TODO |
| BS-109 | Add hold/resume draft bills | Held vs posted visual distinction हो | TODO |
| BS-110 | Print/reprint receipts without reposting | Print fail पर sale safe और reprint available | TODO |

### L. Returns, refunds & cashier controls — Retail V1

| ID | Task / clear deliverable | Mandatory task-specific UI/UX check | Status |
| --- | --- | --- | --- |
| BS-111 | Build searchable sales history | Invoice खोजने के clear filters हों | TODO |
| BS-112 | Build sale detail/receipt screen | Purchased vs returned amounts अलग हों | TODO |
| BS-113 | Implement full and partial return | Available refundable balance साफ हो | TODO |
| BS-114 | Block over-return by quantity | Excess return actionable message दे | TODO |
| BS-115 | Implement refund policy and approvals | Cashier को permission reason दिखे | TODO |
| BS-116 | Post stock/financial reversal entries | Original invoice immutable दिखे | TODO |
| BS-117 | Build exchange as linked return and sale | Net payable/refund transparent हो | TODO |
| BS-118 | Implement cash register open | Opening float का अर्थ स्पष्ट हो | TODO |
| BS-119 | Implement drawer close/count/variance | Expected vs counted cash अलग हों | TODO |
| BS-120 | Test refund retry/concurrency/reconciliation | Multiple taps duplicate refund न बनाएं | TODO |

### M. Retail reporting & daily operations — Retail V1

| ID | Task / clear deliverable | Mandatory task-specific UI/UX check | Status |
| --- | --- | --- | --- |
| BS-121 | Build real Retail Dashboard actions | Fake KPI या demo data न हों | TODO |
| BS-122 | Build date-filtered daily sales report | Business date/timezone स्पष्ट हो | TODO |
| BS-123 | Build purchase/receiving report | Ordered vs received totals अलग हों | TODO |
| BS-124 | Build stock balance and movement report | Available vs on-hand समझ आए | TODO |
| BS-125 | Build supplier outstanding report | Due amount और payment dates साफ हों | TODO |
| BS-126 | Build cashier register variance report | Cash/card/variance अलग हों | TODO |
| BS-127 | Implement CSV/export under tenant scope | Export में applied filters दिखें | TODO |
| BS-128 | Add pagination and server-side totals | Page subtotal vs grand total अलग हों | TODO |
| BS-129 | Test report arithmetic against ledger | UI और database totals reconcile हों | TODO |
| BS-130 | Run Retail end-to-end owner/cashier pilot | Create→Receive→Sale→Return बिना confusion | TODO |

### N. Languages, RTL & accessibility — In working features

| ID | Task / clear deliverable | Mandatory task-specific UI/UX check | Status |
| --- | --- | --- | --- |
| BS-131 | Prepare simple translation key convention | Hardcoded mixed-language labels न हों | TODO |
| BS-132 | Add English fallback language pack | Missing key की जगह meaningful text | TODO |
| BS-133 | Add Arabic translation and RTL direction | Forms/tables reading order natural हो | TODO |
| BS-134 | Add language selection in UI | Current language visibly selected हो | TODO |
| BS-135 | Persist business/user language preference | Refresh पर language न बदले | TODO |
| BS-136 | Build admin language file upload | Allowed file format साफ दिखे | TODO |
| BS-137 | Validate placeholder/key consistency | Upload errors exact line/key बताएं | TODO |
| BS-138 | Add preview and publish/rollback controls | Preview और live language अलग दिखें | TODO |
| BS-139 | Check keyboard/screen-reader focus labels | Every action operable without mouse | TODO |
| BS-140 | Audit all release screens in mobile and RTL | No clipped labels or horizontal scrolling | TODO |

### O. License Manager & offline Windows — After secure admin

| ID | Task / clear deliverable | Mandatory task-specific UI/UX check | Status |
| --- | --- | --- | --- |
| BS-141 | Complete private package release-tag publication | Admin errors में package internals न दिखें | TODO |
| BS-142 | Verify authorized private NuGet consumption | Existing cloud screens regress न हों | TODO |
| BS-143 | Integrate platform-only license list/history API | Only Super Admin sees licenses | TODO |
| BS-144 | Add license issue/renew/transfer/revoke APIs | Dangerous actions confirmed and audited हों | TODO |
| BS-145 | Build Super Admin License screens | Create/renew flow सरल और consistent हो | TODO |
| BS-146 | Reuse protected Windows signing service | Signing key किसी UI/API response में न दिखे | TODO |
| BS-147 | Build offline activation request import/export | File selection और outcome clearly labeled हों | TODO |
| BS-148 | Integrate on-prem local signed license validation | License status और expiry स्पष्ट दिखें | TODO |
| BS-149 | Implement expiry grace/read-export policy | Expired user का safe next step दिखे | TODO |
| BS-150 | Test offline reboot/machine binding/reissue | LAN app बिना internet और confusion चले | TODO |

### P. White label & dedicated SQL Server — Enterprise after Retail

| ID | Task / clear deliverable | Mandatory task-specific UI/UX check | Status |
| --- | --- | --- | --- |
| BS-151 | Create simple business branding settings | Logo/name/theme preview सही दिखे | TODO |
| BS-152 | Validate approved custom domain ownership | Domain verification steps readable हों | TODO |
| BS-153 | Automate TLS binding and trusted host routing | Wrong host पर accidental access न हो | TODO |
| BS-154 | Protect tenant-specific login/cookie scope | Cross-domain sign-in loop न बने | TODO |
| BS-155 | Define central control vs operational data boundary | Business switch behavior unchanged रहे | TODO |
| BS-156 | Add approved dedicated SQL Server assignment | Admin accidental database switch न करे | TODO |
| BS-157 | Implement migration to dedicated DB with rollback | Progress और success/failure clear हों | TODO |
| BS-158 | Verify tenant isolation across database modes | Wrong database data never appears | TODO |
| BS-159 | Define BYO customer-hosted database contract | Unsupported provider selectable न हो | TODO |
| BS-160 | Test white-label/SQL upgrade/restore flows | Branding, invoice and navigation stay consistent | TODO |

### Q. Salon / Barber vertical — After Retail release

| ID | Task / clear deliverable | Mandatory task-specific UI/UX check | Status |
| --- | --- | --- | --- |
| BS-161 | Enable reviewed Salon business type | Unsupported state hidden until ready | TODO |
| BS-162 | Build service/category catalog | Service price and duration readable हों | TODO |
| BS-163 | Build staff master and permissions | Staff availability easy to edit हो | TODO |
| BS-164 | Build resources/chairs/rooms configuration | Schedule capacity understandable हो | TODO |
| BS-165 | Build appointment calendar day/week | Busy slots easy to distinguish हों | TODO |
| BS-166 | Prevent overlapping appointments | Conflict with alternatives दिखे | TODO |
| BS-167 | Add customer lookup and booking form | Repeat customer booking fast हो | TODO |
| BS-168 | Implement appointment status/cancel/no-show | Status meanings and fees clear हों | TODO |
| BS-169 | Implement checkout and relevant deposits | Appointment to receipt smooth हो | TODO |
| BS-170 | Run salon owner/receptionist usability pilot | Book→Serve→Bill end-to-end simple हो | TODO |

### R. Repair Center vertical — After Salon readiness

| ID | Task / clear deliverable | Mandatory task-specific UI/UX check | Status |
| --- | --- | --- | --- |
| BS-171 | Enable reviewed Repair business type | Only working workflows advertised हों | TODO |
| BS-172 | Build repair customer/device intake | Serial and issue fields easy हों | TODO |
| BS-173 | Create job card with numeric ID | Customer receipt/tracking readable हो | TODO |
| BS-174 | Assign technician and due date | Ownership and priority clear हों | TODO |
| BS-175 | Define repair statuses and history | Status timeline customer-friendly हो | TODO |
| BS-176 | Implement parts use and stock deduction | Parts consumption validated हो | TODO |
| BS-177 | Add estimates/customer approval | Approved vs draft cost obvious हो | TODO |
| BS-178 | Record labor charges and final billing | Parts and labor separate हों | TODO |
| BS-179 | Handle rework/return and audit | Reopen reason and prior history visible हों | TODO |
| BS-180 | Run job intake→delivery usability pilot | Technician and counter flow intuitive हो | TODO |

### S. Production, quality & scale — Release gate

| ID | Task / clear deliverable | Mandatory task-specific UI/UX check | Status |
| --- | --- | --- | --- |
| BS-181 | Verify browser E2E critical workflows | Real click journey without dead ends | TODO |
| BS-182 | Run role/tenant penetration regression | Access denied clear and no leakage | TODO |
| BS-183 | Review secrets, packages and vulnerability alerts | No private debug details on screens | TODO |
| BS-184 | Perform SQL backup and independent restore drill | Restored app screens behave correctly | TODO |
| BS-185 | Configure IIS/HTTPS/reverse proxy/deploy pipeline | Cold start shows useful load state | TODO |
| BS-186 | Add production error metrics and monitoring | User errors never expose stack traces | TODO |
| BS-187 | Load test target concurrent business workload | POS search and checkout remain responsive | TODO |
| BS-188 | Perform payment and stock fault-injection drills | Failures preserve outcome visibility | TODO |
| BS-189 | Run accessibility/mobile/RTL acceptance by role | Zero unresolved critical confusion | TODO |
| BS-190 | Pilot rollout, support runbook and release sign-off | New users finish first sale unassisted | TODO |

## Definition of DONE, required for each BS task

- [ ] Task-specific acceptance criterion implemented with minimal code, without unrelated rewrites or duplicate logic
- [ ] Backend API/DB migration reviewed as needed; build, tests and negative permission/tenant cases PASS
- [ ] Frontend typecheck/build and task workflow browser tests PASS where applicable
- [ ] **UI/UX reviewed after this step:** real role, primary flow, mobile/tablet/desktop, keyboard/focus, loading/empty/error/edit, navigation, labels, no confusion
- [ ] Related existing screens regression-checked (and RTL when shipped); confusing elements fixed
- [ ] Commit and CI link, actual UX evidence, known limitations and next task recorded

If any checkbox cannot be verified, keep `TODO` or `UX REVIEW PENDING`, not `DONE`.

## First executable delivery sequence

**Release slice 1:** Secure registration/login and scoped Business creation/switching (A–D), UX basics (E), minimum plan/trial checks (G).  
**Release slice 2:** Retail catalog (H) → stock (I) → PO/receipt (J) → POS (K) → returns/cash (L) → real reports/pilot (M).  
**Release slice 3:** Platform admin/entitlements (F), localization (N) and tested cloud paid operations (G), only after supporting routes exist.  
**Release slice 4:** Reuse licensing and Windows on-prem validation (O); private signing key never reaches the customer or public repository.  
**Release slice 5:** Enterprise branding/dedicated SQL (P), then Salon (Q), Repair (R); final cross-product production gates (S). Security, responsive UX and tests are continuous from task one.

This is a checklist and sequencing contract. **190 tasks does not mean 190 new screens or 190 new DB tables.** Combine implementation into small coherent commits when useful but sign off each applicable acceptance/UX check separately.
