# Business SaaS — Database and Multi-Tenant Architecture

**Status:** Phase 0 logical schema and EF Core constraints; SQL migrations not implemented  
**Updated:** 2026-11-08  
**Repository:** `dharmendra1031/business_saas` ONLY  
**Source of truth:** [Business SaaS Master SRS](./BUSINESS_SAAS_MASTER_SRS.md)  
**First vertical:** [Retail V1](./RETAIL_V1_SRS.md)  
**Database:** Microsoft SQL Server + EF Core in .NET 10 modular monolith.

## 1. Tenancy and identity model

```text
User (global numeric Id)
   |
   +-- BusinessUser membership --> Business (tenant: numeric Id)
                                           |
                                           +-- BusinessLocation (shop / branch / warehouse)
                                           +-- BusinessSubscription -> PlanVersion
                                           +-- BusinessModules / feature entitlements
                                           +-- Business-scoped operations
                                                 +-- Retail: Products / Stock / Sales
                                                 +-- Salon: Services / Appointments
                                                 +-- Repair: Jobs / Parts
```

- One account can be a member of many businesses and have a different role and allowed locations in each.
- Each business is its own **tenant and accounting/security boundary**. One business can have multiple locations.
- All **database application entity primary keys are numeric SQL Server `BIGINT IDENTITY(1,1)` or `INT IDENTITY(1,1)`**. Set ASP.NET Core Identity users/roles to `long` generic keys; never allow Identity to create GUID IDs.
- Cross-tenant business records use `BusinessId` and applicable `BusinessLocationId`. Do not introduce both `SalonId` and `BusinessId` as competing tenant keys.
- `BusinessTypeId` is categorization/configuration, not tenant identity; changing business type cannot transfer another business's data.
- External payment references and expiring security tokens may be nonnumeric strings, but are **not** entity IDs; the application's own IDs remain numeric.

## 1A. Lean physical database rollout — do not create every planned table

The tables below are **what the first working foundation needs**, not an instruction to create the full future entity catalog. Add one reviewed EF migration when a tested feature requires a new table or column.

| When | Minimal physical model | Why |
| --- | --- | --- |
| P1 — Authentication | Built-in ASP.NET Core Identity with `long` numeric keys and its standard tables | Never create a duplicate custom `Users` table or handmade password/session service |
| P1 — Business creation | `BusinessTypes`, `Businesses`, `BusinessUsers`, `BusinessLocations` | First supported Retail type, owner membership, one usable shop/location, safe business switch |
| P1/P3 — Trial | `SubscriptionPlans`, `BusinessSubscriptions` with basic plan/trial state | Simple independent business trial; no billing gateway required before actual paid billing |
| P2 — Module access | `Modules` and a minimal plan-to-module mapping as soon as Platform Admin control works | Store effective entitlements only when managed; API authorization must still enforce them |
| P4 — Retail operations | Product, supplier, stock ledger/balance, purchase, invoice/payment, return, cash register tables **per implemented slice** | Do not create future Salon/Repair/Rental/Class tables |

**Avoid at first:** `BusinessCategories` (a simple supported business type code is enough), persisted `BusinessTemplates` and `BusinessTemplateModules`, `BusinessOnboarding` (derive a short checklist from actual setup state), `BusinessInvitations` (until invitations ship), `BusinessSettings` for fields already on `Businesses`, custom role-editing tables, paid plan version history, module overrides, add-on billing tables, translation-publishing history, Redis/sharding metadata, generic EAV/custom-form entities. Add each only with a real user journey and test.

**V1 permission implementation:** `BusinessUsers` may have a simple validated `RoleCode` (`Owner`/`Manager`/`Cashier`) while all relevant APIs still verify the current `BusinessId` and authorized location. Separate BusinessRole/Permission join tables and `BusinessUserLocations` become necessary when configurable roles or multi-location assignments are genuinely delivered. Do not put platform Super Admin powers into the business RoleCode.

**V1 subscription implementation:** start with basic plan/trial state. Before **real paid subscriptions** are activated, preserve price/currency/billing-term snapshots or immutable plan versions and durable payment reconciliation; simplicity must not let plan edits retroactively alter paid contracts.

**V1 language implementation:** ship stable translation keys and small built-in locale bundles first. Build the requested upload/validate/publish system when Language Management is being implemented, without building a generic file-import framework.

The conceptual lists below describe future candidates. **They are not a V1 table creation checklist.** SQL constraints, concurrency, tenant isolation and data correctness remain mandatory from the first operation.

## 2. Future entity candidates — platform and membership (not all V1 tables)

| Table | Primary fields | Constraints / owner |
| --- | --- | --- |
| ASP.NET Core Identity `AspNetUsers` and standard Identity tables | Long numeric Id, Email/Phone, PasswordHash, verification state | Reuse Identity's official schema; do not create a parallel custom Users table |
| `Businesses` | Id, Name, BusinessTypeId, CountryCode, CurrencyCode, TimeZoneId, Status, CreatedByUserId | Independent tenant |
| `BusinessCategories` | Id, Code, Name, IsActive | Admin-managed taxonomy |
| `BusinessTypes` | Id, BusinessCategoryId, Code, Name, IsSupported, IsActive | Only supported types usable for self-service activation |
| `BusinessTemplates` | Id, BusinessTypeId, Version, Status, SetupSchemaVersion | Versioned, published/retired setup definition |
| `BusinessTemplateModules` | Id, BusinessTemplateId, ModuleId, IsRequired | Published template capabilities |
| `BusinessLocations` | Id, BusinessId, Code, Name, LocationType, IsActive, IsPrimary | Business-scoped; Shop/Branch/Warehouse |
| `BusinessUsers` | Id, BusinessId, UserId, RoleCode (simple V1), Status, JoinedAt | Unique(BusinessId, UserId); no implicit access |
| `BusinessRoles` | Id, BusinessId, Code, Name, IsSystem | Per-business RBAC templates |
| `BusinessUserRoles` | Id, BusinessUserId, BusinessRoleId | Role in the same business enforced |
| `BusinessUserLocations` | Id, BusinessUserId, BusinessLocationId | Assigned location in the same business enforced |
| `Permissions` | Id, Code, ModuleId, Description | Stable platform permission codes |
| `BusinessRolePermissions` | Id, BusinessRoleId, PermissionId | Role configuration; subscription checked separately |
| `BusinessSettings` | Id, BusinessId, DefaultLanguageId, ReceiptConfig, UpdatedAt | Prefer named columns for critical business settings |
| `BusinessOnboarding` | Id, BusinessId, TemplateVersionId, Status, Step, UpdatedAt | Resume interrupted onboarding |
| `BusinessInvitations` | Id, BusinessId, Email/Phone, Role/LocationScope, ExpiresAt, Status | Numeric row ID; signed expiring invitation token outside ID |

Add unique constraints for `BusinessTypes.Code`, `BusinessCategories.Code`, `BusinessTemplates(BusinessTypeId,Version)`, and `BusinessLocations(BusinessId,Code)`. Ensure one primary active location per business only if product rules demand it.

**Important:** do not create all future tables at once. This is a target logical model; add physical tables in tested migrations when their vertical slice is implemented.

## 3. Modules, subscriptions and language

| Table | Purpose |
| --- | --- |
| `Modules` | Canonical module codes, dependencies, active status |
| `SubscriptionPlans` | Plan family, stable code/name, availability |
| `PlanVersions` | Immutable version and effective dates of subscription terms |
| `PlanVersionModules` | Which modules are entitled in that plan version, optionally constrained by supported business type |
| `PlanVersionLimits` | Quotas (locations, active users, terminals, storage, usage) for plan version |
| `PlanPrices` | Currency, market, billing period, amount and price version |
| `BusinessSubscriptions` | BusinessId, PlanVersionId, State, StartAt, EndAt, TrialEndAt, RenewalAt |
| `BusinessModuleOverrides` | BusinessId, ModuleId, enabled/disabled, reason, author, expiration |
| `BusinessAddOns` | BusinessSubscriptionId, add-on selection, limits and effective dates |
| `SubscriptionInvoices` / `SubscriptionPayments` | SaaS charges distinct from business customer POS payments |
| `BusinessUsage` | BusinessId, MetricCode, Value, MeasuredAt; reliable quota calculation |
| `Languages` | BCP47 Code, native name, RTL/LTR, published status |
| `TranslationKeys` | Stable application/module/key catalog |
| `TranslationImports` / `TranslationVersions` / `TranslationValues` | Validated draft/publish/rollback |
| `BusinessLanguages` | BusinessId, LanguageId, allowed/default |
| `UserLanguagePreferences` | UserId, preferred LanguageId |

**Subscription rules**
- Subscription belongs to BusinessId, not UserId.
- A published PlanVersion must be immutable for existing subscriber terms. Prices can vary by country and billing cycle; never silently convert currency.
- Required modules from a business template still need a compatible plan/entitlement. Validate dependencies at activation.
- Cache key includes effective BusinessId and plan/version; invalidate when entitlements/memberships change.
- Check plan + module + permission + active location at API, not only frontend.
- For identity use separate role namespaces for Platform Admin vs Business roles. Business Owner never inherits Platform Admin.

## 4. Retail V1 operational schema

| Table | Purpose | Tenant/location scope |
| --- | --- | --- |
| `ProductCategories` | Hierarchical category for each business | BusinessId |
| `Units` | Units and precision; reusable definitions or business-specific overrides | BusinessId where business-defined |
| `Products` | Sku/Code, Name, CategoryId, UnitId, Active, tax/pricing data | BusinessId |
| `Suppliers` | Supplier contacts and terms | BusinessId |
| `Customers` | Customer contacts and optional credit policy | BusinessId |
| `PurchaseOrders` / `PurchaseOrderLines` | Supplier order, location, items, outstanding | BusinessId, receiving location |
| `GoodsReceipts` / `GoodsReceiptLines` | Posted receipts, partial delivery, validation | BusinessId, location |
| `StockBalances` | Current product quantity, concurrency `rowversion` | BusinessId, BusinessLocationId |
| `StockMovements` | Durable stock ledger, movement reason, source posting | BusinessId, BusinessLocationId |
| `StockAdjustments` / `StockAdjustmentLines` | Authorized variance and reason | BusinessId, location |
| `SalesInvoices` / `SalesInvoiceLines` | Header, server prices/tax/discount, fiscal number | BusinessId, location |
| `SalePayments` | Payment legs per invoice; no double capture | BusinessId, location |
| `SalesReturns` / `SalesReturnLines` | Refund/return with original invoice references | BusinessId, location |
| `CashRegisters` / `CashRegisterSessions` | Drawer state, assigned cashier, shift and variance | BusinessId, location |
| `BusinessNumberSequences` | Per-business/location/type fiscal numbering under transaction | BusinessId, optional location |
| `OperationRequests` | Idempotency records for create/post/refund, with token fingerprint, result, status | BusinessId, operation type |

Do not treat Product stock as a plain mutable Product.Quantity field. Use stock postings and a synchronized/locked balance. Product variants, barcode label rules, lot/expiry, serial number, wholesale/Udhar, advanced general ledger and manufacturing are later vertical extensions unless pilot validates an immediate need.

## 5. Schema and foreign-key safety constraints

### Tenant-safe relationships

For parent-child business entities, include **BusinessId on the child and composite referential checks**:
- `Products`: unique index `(BusinessId, Id)`; `SalesInvoiceLines` or `PurchaseOrderLines` refer to `(BusinessId, ProductId)` on parent rather than cross-tenant `ProductId` alone where supported by SQL Server and EF Core alternate keys.
- `BusinessLocations`: unique `(BusinessId, Id)`; transactional tables reference `(BusinessId, BusinessLocationId)`.
- `BusinessUserRoles` membership/role business alignment: enforce via appropriate composite keys, constrained writes and server checks; if join schema cannot represent it safely, include BusinessId on joins and composite FKs.
- Unique natural/business codes are generally scoped: `(BusinessId, Code)` or `(BusinessId, BusinessLocationId, DocumentType, Number)`, not global uniqueness across businesses.
- FK relationships are **NO ACTION / RESTRICT** for posted financial data where cascade would risk deletion. Soft-deactivate/archive instead of removing posted records.

Unique `(BusinessId, Id)` is a supporting alternate key for composite FK enforcement; it does not replace the single numeric IDENTITY primary key.

### Indexing baseline

- `BusinessUsers(BusinessId,UserId)` unique.
- `BusinessLocations(BusinessId,Code)` unique.
- `Products(BusinessId,Sku)` unique where Sku is not null; enforce per-business SKU policy.
- `StockBalances(BusinessId,BusinessLocationId,ProductId)` unique; concurrently update in a short transaction.
- `SalesInvoices(BusinessId,BusinessLocationId,BusinessDate,Status)` for common list/report queries.
- `StockMovements(BusinessId,BusinessLocationId,ProductId,PostedAt)` for historical ledger.
- `OperationRequests(BusinessId,OperationType,ClientOperationToken)` unique for idempotency.
- Plan/template codes unique, and entitlement effective-date lookups indexed as measured.

Measure execution plans before adding costly or redundant indexes. All list/report APIs use tenant-scoped predicates and bounded pagination; big exports stream/batch with authorization.

## 6. Authentication, session context and caching

- ASP.NET Core Identity must use `IdentityUser<long>`, `IdentityRole<long>` (or equivalent numeric key mapping). Password storage via supported Identity hashers; no handmade crypto.
- Auth verifies current UserId; active BusinessId is selected from that user's memberships server-side. Switching businesses obtains a validated context and recomputes authorization.
- A BusinessId in route/body/header is an object selector, **not proof of authorization**. Every involved record must belong to the selected business.
- Queries/EF Core global filters may be a second layer of defense but do not replace service-level permission and branch checks. Review background jobs and `IgnoreQueryFilters` explicitly.
- First use correctly scoped database queries and a React query cache keyed by current BusinessId; cancel/invalidate on business switch or revoked access. If a distributed cache is later introduced, all private keys must include BusinessId, relevant location/role and version; never cache tenant-private responses under global public URLs.
- Public booking/store links resolve business through a verified public code/domain and limit visible fields.

## 7. Consistent writes, money and stock

- SQL transaction boundary protects local posting (invoice+lines+ledger+stock+idempotency) and prevents partial posts.
- External payment gateways cannot join a SQL transaction: record attempt, call provider, verify/reconcile webhook or status, then finalize safely; unknown result cannot trigger blind second charge.
- `rowversion` supports optimistic concurrency; it is a SQL binary concurrency token, **not an identifier**.
- Monetary amounts use SQL `decimal(p,s)` and C# `decimal` with explicitly approved rounding per market; no floating point for money.
- Stock quantities can require different decimal scale than money; define units and precision.
- Partial purchase receipt posts only physically received accepted quantities; purchase order by itself never changes stock.
- Cash, card and split payments produce auditable distinct tender legs; register expected cash excludes card transactions.
- Issued invoice number may be business/location scoped and governed by country policy; do not assume the primary key is a legal invoice sequence.
- Refunds/returns post compensating entries and validate original invoice/balance under concurrency.

## 8. Migration, data lifecycle and backup

- All new schema in `business_saas` is independent from private Salon/CBK databases; **no automatic import of old backups**.
- Create EF migrations incrementally per vertical slice. Review SQL scripts and transaction safety; never rewrite applied migrations.
- Plan a mapping/import wizard with file validation, preview, duplicate resolution, consent/privacy and audit. No cross-business import of confidential data.
- BusinessStatus controls availability, but historic financial records are preserved per legal retention rules.
- Define per-market retention/export/deletion policy and customer support access procedure before launch.
- Encrypted backups for SQL and object/file storage; test restores in isolated staging. Staged deployment with rollback path.

## 9. Operations and future scaling

- Start with one SQL database and stateless modular-monolith API; scale via measured bottlenecks.
- Tenant isolation must survive future shard/dedicated database routing, but **do not build a shard router now**.
- Track registered businesses, active businesses, active users, concurrent cashiers, API/DB p95 latency, queue lag, storage and report activity as separate metrics.
- No claim that a category catalog of 1,000+ types implies support for 1,000 distinct executable workflows or 10,000 concurrent checkouts.
- Critical two-business negative tests, stock and payment idempotency tests, restore drills and usability pilots are release blockers.

## 10. Initial ER relationships (logical)

```text
Users.Id <- BusinessUsers.UserId -> Businesses.Id
Businesses.Id <- BusinessLocations.BusinessId
Businesses.BusinessTypeId -> BusinessTypes.Id -> BusinessCategories.Id
BusinessTypes.Id <- BusinessTemplates.BusinessTypeId
BusinessTemplates.Id <- BusinessTemplateModules.BusinessTemplateId -> Modules.Id
Businesses.Id <- BusinessSubscriptions.BusinessId -> PlanVersions.Id
PlanVersions.Id <- PlanVersionModules.PlanVersionId -> Modules.Id
BusinessUsers.Id <- BusinessUserRoles.BusinessUserId -> BusinessRoles.Id
Businesses.Id <- Products.BusinessId
Businesses.Id <- SalesInvoices.BusinessId
SalesInvoices.Id <- SalesInvoiceLines.SalesInvoiceId
Products.Id <- SalesInvoiceLines.ProductId (same BusinessId enforced)
Businesses.Id <- StockBalances.BusinessId / StockMovements.BusinessId
```

**Phase 1 must produce the actual EF model and reviewed migrations after these logical constraints are confirmed.** This document is a design, not a database deployment.
