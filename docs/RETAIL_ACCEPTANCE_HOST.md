# Isolated Retail acceptance host

This is not production activation. Use a separate private test server and disposable or sanitized database, never a merchant database. The normal Production and Staging environments retain existing restrictions.

Set ASPNETCORE_ENVIRONMENT=RetailAcceptance (also ensure DOTNET_ENVIRONMENT does not override it), RetailAcceptance__Enabled=true, Payments__Enabled=false, Payments__AllowLive=false and Razorpay__Mode=Test. Supply a dedicated SQL Server connection string and encrypted persistent Data Protection keys outside Git. Configure HTTPS, restricted network access and a verified invited owner. Apply reviewed migrations explicitly before startup; the acceptance host does not automatically migrate.

Retail acceptance exposes POS, register and global catalogue routes and permits supplier-account and stock-transfer posting under their existing authorization and module policies. Credit, return, exchange and register feature gates remain separately controlled. For a complete acceptance run, configure the existing gates deliberately and use test data. Self-registration, relaxed development cookies and automatic migrations remain disabled. Windows receipt rendering uses the existing templates unchanged; warm-up remains development-only.

Check /api/ready and complete actual owner/manager/cashier browser-to-SQL acceptance, concurrency, payment sandbox, reconciliation and backup restore before any production decision. A passing route-registration test alone does not certify the workflow. Do not enable live payments or change Production to Development.

See RETAIL_V1_RELEASE_READINESS.md for outstanding release blockers.
