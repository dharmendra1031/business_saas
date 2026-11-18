# Module boundaries (lightweight)

Business SaaS remains **one .NET API, one React app and one SQL Server database**.
No microservices, generic plugin framework or additional services are needed for a
second business type.

## Shared platform

Authentication, business creation, membership, locations, subscription status,
platform billing and MFA remain shared. IDs are numeric SQL identities and all
tenant-owned data uses `BusinessId`.

## Retail

`Features/RetailModule.cs` owns the **registration of Retail endpoint groups**
and the initial Retail category/unit setup. Existing Retail endpoints, database
tables and URLs stay unchanged.

Every Retail API still checks both business membership and `BusinessType.Code
== "RETAIL"` on the server. Hiding a menu is not authorization.

The frontend only opens `RetailCatalog`/Retail navigation when dashboard
`business.typeCode` is `RETAIL`. Other types return safely to the business
selector until a real module workspace is implemented.

## Adding Salon or Repair later

1. Add the new module's endpoints and tables without editing Retail operations.
2. Register its route mapping separately and initialize defaults only for that type.
3. Give its business workspace a dedicated frontend component and route it by
   `business.typeCode`; avoid reusing Retail state/navigation.
4. Add negative tests for cross-business access and wrong business types.
5. Run the existing backend, SQL Server migration, frontend and Retail regression
   tests on each change before merging to `main`.

The `RetailMasterTests` suite creates a synthetic supported `SALON` business
**in a test database only** and proves Retail categories, products, purchase
orders and default masters remain inaccessible/not provisioned. It also verifies
that normal Retail data continues working.

**Current scope:** No Salon/Repair product modules are implemented or enabled in
the real catalog. This boundary decreases regression risk; automated tests cannot
guarantee that every future bug is impossible.
