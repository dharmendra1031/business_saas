# Business SaaS — Multi-Language Management Specification

**Status:** Agreed product direction; implementation not started  
**Repository:** `dharmendra1031/business_saas` only  
**Scope:** Platform Admin, Business Admin, POS/Operations, customer interfaces, reports, receipts and notifications  
**Reference:** [Canonical Master SRS](./BUSINESS_SAAS_MASTER_SRS.md)

**Multi-business rule:** Language packs are platform-wide, published centrally. One login may switch across businesses with different default locales, but the explicit user preference and current business policy are resolved without mixing business data. Translation-only updates require no frontend redeploy.

## 1. Product decision

Business SaaS must support **multiple languages**, not just English and Arabic. New language packs can be uploaded, validated, reviewed, published and rolled back **from the SaaS Platform Admin UI without editing or deploying frontend code**.

Initial candidate language packs: English (`en`), Arabic (`ar`), Hindi (`hi`), Urdu (`ur`), French (`fr`) and Spanish (`es`). This is an illustrative launch list, **not a hard limit or claim that translations are complete**.

- Use standard language/locale tags such as `en`, `en-IN`, `ar-KW`, `hi-IN` (BCP 47).
- Language direction is metadata: LTR or RTL. Arabic and Urdu require RTL.
- A language pack can be Draft, Published, Disabled or Archived.
- Only published/active language packs can be selected by normal users.
- Platform UI language availability is managed centrally; each business can choose a permitted default and enable a subset for users.
- Each user may choose a preferred language. The currently selected business default applies when a user has no preference.
- POS, Business Admin and Platform Admin may be switched at runtime; a full deployment is not required.
- Do not infer locale, currency, timezone or subscription entitlements from one another.

## 2. Language Admin screens (SaaS Super Admin)

### Language Master

Manage:
- Language name (English and native name, e.g. `Arabic / العربية`)
- Language code/locale tag (`ar`, `ar-KW`) and optional parent locale
- Text direction (`ltr` / `rtl`)
- Published/disabled status, version, coverage and last update
- Language scope: Platform Admin, Salon Admin, POS, Customer App, Reports, Notifications

### Translation file import

Platform Admin can:
1. Download the current **translation template** with stable translation keys.
2. Upload a UTF-8 `JSON` or `CSV` translation file for a selected locale and application/module.
3. Preview key coverage, missing keys, duplicate keys, unknown keys and placeholder mismatches.
4. Preview selected screens and RTL layout before publishing.
5. Save an imported version as **Draft**.
6. Publish the version atomically; invalidate the appropriate locale/version cache.
7. Compare prior versions and **roll back** if a pack causes errors.

**Uploading must never execute scripts, HTML or dynamic code.** Treat translation strings as untrusted text; render them safely with normal React escaping.

### Import format — JSON

```json
{
  "locale": "hi-IN",
  "module": "pos",
  "translations": {
    "pos.checkout.title": "बिलिंग",
    "pos.checkout.pay": "भुगतान करें",
    "pos.checkout.total": "कुल राशि",
    "pos.invoice.number": "इनवॉइस {invoiceNumber}",
    "common.save": "सेव करें"
  }
}
```

Example format; placeholder tokens like `{invoiceNumber}` must match the canonical key definition. Templates should document pluralization and formatting rules where relevant; do not concatenate translated fragments to construct sentences.

### Import format — CSV

```csv
key,value
pos.checkout.title,बिलिंग
pos.checkout.pay,भुगतान करें
pos.checkout.total,कुल राशि
pos.invoice.number,इनवॉइस {invoiceNumber}
common.save,सेव करें
```

UTF-8 only; parse quoted CSV correctly. Export CSV safely to prevent spreadsheet formula injection. Reject malformed files and enforce size/row limits.

## 3. Language resolution and fallback

The system resolves language per request:

1. User's explicit language preference, if active and allowed in the current business.
2. Salon's configured default locale, if available (on business-facing applications).
3. Platform's default locale, normally `en`.

For a missing key, fall back from regional locale to base language, then to **English** (e.g. `ar-KW` → `ar` → `en`). Log missing keys for admin review; do not show raw technical keys to end users.

The Platform Admin has its own global language setting, independent of the business's chosen language.

Salon owners can choose from globally published supported languages. Salon-specific business content (service names, salon descriptions and messages) must have its own localized content model where needed; it must not be confused with the global UI translation dictionary.

## 4. Runtime translation architecture

- The backend owns the **Language Master**, translation keys and published pack versions.
- Frontends load published translation bundles by **application/module + locale + version** using the authenticated/public API as appropriate.
- Keep a small built-in English fallback for first load, login, offline cases and API failures.
- Lazy-load heavy translation namespaces (e.g. reports or payroll) only when needed.
- Avoid one gigantic translation payload for all apps.
- Cache immutable versions via ETag or versioned keys; publishing invalidates the active-version pointer.
- Never use the raw uploaded file directly in the public UI before validation and publication.
- Keep language services independent of business subscriptions; feature access still uses tenant entitlement + permission checks.
- POS must remain usable with its last valid cached pack during temporary network outages.
- Mobile apps and report generators should use the same stable keys and locale metadata, with platform-specific rendering where required.

### Example API contracts (design only)

```text
GET    /api/localization/languages
GET    /api/localization/bundles?locale=hi-IN&module=pos
GET    /api/platform/languages
POST   /api/platform/languages
POST   /api/platform/languages/{languageId}/imports
GET    /api/platform/languages/{languageId}/imports/{importId}/preview
POST   /api/platform/languages/{languageId}/imports/{importId}/publish
POST   /api/platform/languages/{languageId}/versions/{versionId}/restore
PUT    /api/salon/settings/language
PUT    /api/account/preferences/language
```

Routes are provisional; implement within existing API naming standards rather than duplicating another localization service.

## 5. Database model (conceptual)

All persisted entity IDs use numeric SQL Server IDENTITY, not UUID/GUID.

| Table | Main fields |
| --- | --- |
| `Languages` | Id, Code (unique), Name, NativeName, Direction, IsActive |
| `TranslationKeys` | Id, Code (unique), Module, Description, DefaultText |
| `TranslationImports` | Id, LanguageId, Module, Status, CreatedByUserId, CreatedAt, PublishedAt |
| `TranslationValues` | Id, TranslationImportId, TranslationKeyId, Value |
| `TranslationVersions` | Id, LanguageId, Module, VersionNumber, IsPublished, PublishedAt |
| `BusinessLanguages` | Id, BusinessId, LanguageId, IsEnabled, IsDefault |
| `UserLanguagePreferences` | Id, UserId, LanguageId |

These are **candidate entities**; avoid redundant tables if existing models support the requirements. Ensure uniqueness and immutable published-version semantics. Do not store uploaded translation blobs unbounded in database without a retention policy.

## 6. English, Arabic and other script support

- Apply `lang` and `dir` correctly on document/root and on mixed-direction fields when appropriate.
- Use CSS logical properties (`margin-inline-start`, `padding-inline-end`) and a verified Bootstrap RTL build where relevant.
- Buttons, forms, breadcrumbs, tables, dialogs, mobile navigation and focus order must be reviewed in RTL.
- Do not mirror logos, numbers or business document content blindly.
- Use Unicode-safe database fields and fonts with coverage for Latin, Arabic, Devanagari and other supported scripts.
- Format dates, numbers and currencies using locale-aware APIs with the **salon's actual currency and business timezone**.
- Reports, RDLC/PDF, receipts and notifications need explicit locale tests, not only React translation coverage.
- Translation text, right-to-left switching and local date formatting must be supported in registration and subscription screens too.

## 7. File validation, permissions and audit

- Only authorized Platform Admin translators can import, review, publish or restore global language packs.
- Uploaded files are parsed as data, type-checked, size-limited, scanned where appropriate and rate-limited.
- Reject duplicate keys, unknown or disallowed keys and mismatched placeholders.
- Flag empty strings, unexpectedly long strings and missing translations before publication.
- Keep previous published versions for rollback and display actor, time, checksum and change summary in audit logs.
- A failed import never replaces currently published translations.
- Tenant Admin cannot alter core UI safety/security/payment strings globally. Per-salon UI translation overrides are **not** included in the initial scope; if added later, they require explicit moderation/constraints.
- Ensure translation exports do not contain credentials or salon business data.

## 8. Acceptance tests

1. Upload a new `hi-IN` JSON pack, validate, preview and publish; the target app can switch languages without rebuilding.
2. Upload malformed JSON, duplicate keys or missing placeholders; reject without changing the currently published pack.
3. Disable a locale: existing users fall back safely to an allowed locale.
4. Change an Arabic/Urdu locale: RTL layout and keyboard order remain usable.
5. Missing `ar-KW` key falls back via `ar` then `en`.
6. A salon's default affects its users but does not change the SaaS Platform Admin locale.
7. Switching user language does not alter salon billing currency, tax calculations or business timezone.
8. Language version rollback restores prior strings without deploying new frontend code.
9. Inactive or unauthorized users cannot publish packs.
10. POS/receipt/report translations remain usable on reconnect and after app reload.

## 9. Implementation order

1. Create stable module-scoped translation keys and a small English source catalog.
2. Build Language Master, JSON/CSV import validator, draft/preview/publish/rollback backend.
3. Add platform language management screens.
4. Integrate runtime translation loader and locale preference into Platform Admin, Salon Admin and POS.
5. Verify RTL, numbers/dates/currency, accessibility, report printing, customer apps and offline fallback.
6. Add import audit, version cache and tests before production rollout.

## Decisions

- **LM-001:** Multi-language support is platform-wide; no two-language hardcoding.
- **LM-002:** SaaS Platform Admin manages languages and publishes translation files.
- **LM-003:** Each business can enable published languages and choose its default; users can choose their preference.
- **LM-004:** Translation changes are data-driven, previewed and versioned; no frontend deploy to change strings.
- **LM-005:** English fallback, regional fallback, RTL and safe handling of interpolation are required.
- **LM-006:** Everything is implemented only in `business_saas`, with numeric auto-increment entity IDs.
