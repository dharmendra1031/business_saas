# Offline / On-Premise Deployment Plan

**Status:** Planned only; NOT implemented, deployed or production-tested  
**Updated:** 2026-11-10  
**Repository:** dharmendra1031/business_saas  
**Related:** [White Label and Dedicated Database](./WHITE_LABEL_DATABASE_IMPLEMENTATION.md) · [Database Architecture](./DATABASE_ARCHITECTURE.md) · [Engineering Guardrails](./ENGINEERING_GUARDRAILS.md)

## 1. Customer promise and precise terminology

An Enterprise customer can install an independently operable edition on a **customer-owned local server**, reachable on an office LAN/Wi-Fi, without an Internet connection. Users access the locally hosted web application via an internal address. Local sales, salon appointments, customers, stock, reports, authentication and printing must continue when the external Internet is down, provided the server, LAN, power and local database are available.

This is a distinct **OnPremise / Offline** deployment mode, not merely a centrally hosted dedicated SQL Server database. Avoid promising offline support for features that inherently call external gateways. An installation whose login, tenant router, entitlement check or application assets call our cloud is **not offline**.

Supported commercial deployment targets:

| Mode | API, React, identity | Operational data | Internet need |
| --- | --- | --- | --- |
| Shared SaaS | Our hosted platform | Shared SQL Server (BusinessId scoped) | Required |
| Managed Dedicated | Our hosted platform | Dedicated managed SQL Server | Required for customer access |
| On-Premise Offline | Complete independent local deployment | Customer local SQL Server | Not required for core workflows |
| Hybrid Sync | Local plus external synchronization | Requires conflict/recovery design | Deferred; not a V1 promise |

A custom domain and branding are separate choices in each relevant hosted deployment. On-premise hostname is resolved via customer's internal DNS; a public domain by itself does not make an offline LAN service reachable externally.

## 2. Code and data boundary: preserve one product

- Keep **one source codebase**: .NET 10 ASP.NET Core API, React/TypeScript compiled static assets, EF Core and SQL Server first. Use configuration for `DeploymentMode=Hosted` or `DeploymentMode=OnPremise`, not forks of the application or duplicate business logic.
- **Hosted mode:** global control plane (identity, businesses, memberships, subscriptions, approved domain/destination mapping) remains on our managed platform; operational context selects shared or dedicated SQL Server, as documented in the white-label plan.
- **On-premise mode:** ship the required control-plane data, Identity stores, membership and license validation **inside the customer's site**. The local installation must never require our central control DB, remote authentication endpoint, third-party CDN or cloud discovery service to start or process a sale.
- Operational entities still carry `BusinessId`, `BusinessLocationId` where appropriate, with **numeric auto-increment application entity IDs**. Use the same explicit control/operational EF boundaries being designed before Retail masters; a small single-business installation may use one local SQL Server database with distinct context mappings and safely separated EF migration histories.
- The initial on-premise SKU supports one contracted business with authorized branches/locations on a connected LAN; reseller tenancy or dozens of independent customer businesses inside a license are separately scoped.
- Existing production self-service registration currently requires verified email; on-prem installation needs a **local one-time administrator bootstrap** / approved setup workflow with no email dependency. Subsequent staff identities are locally managed. Central cloud accounts are **not automatically valid offline**.
- Shared business code must use dependency-injected local services for mandatory workflows rather than unconditional HTTP calls to SaaS-only services. No speculative plugin/microservices framework.

## 3. Suggested initial Windows on-premise package

1. Windows Server or another supported Windows host with IIS and the compatible .NET 10 Hosting Bundle (or an explicitly supported bundled .NET runtime deployment).
2. Local SQL Server instance on the server or an accessible LAN DB server; use least-privilege application login, separate controlled migration/admin credentials.
3. Published ASP.NET Core API under IIS and compiled React static files served from the **same local origin**, with local/static JS/CSS/fonts/icons; no CDN/runtime downloads.
4. Local file/attachment directory under a dedicated ACL-protected data path; ensure backup captures SQL + files + application configuration needed for recovery.
5. Company-controlled internal DNS entry such as `salon.office.example` plus trusted HTTPS certificate or a securely managed internal CA. An IP-only deployment is possible but HTTPS/certificates on every client need deliberate setup. Avoid assuming public certificate automation works without Internet.
6. Local firewall allowlist only the required LAN ports, account protections, HTTPS, persistent encrypted ASP.NET Core Data Protection keys, CSRF, audit logs, least privilege and server time synchronization.
7. An install checklist and **versioned deployment manifest** covering .NET runtime, SQL compatibility, schema version, config, required storage, backup and restore procedures. No need for a general installer/orchestrator until a supported pilot demonstrates it.

## 4. Offline licensing and activation

- Offer a customer-contracted installation license, for example a signed **license file** containing numeric Customer/Business license references, purchased modules/locations/users, edition, permitted installation scope, issue/effective dates and term or permanent-entitlement policy.
- License proof uses asymmetric digital signatures: our private signing key **never ships**; the application verifies with an embedded/trusted public key. Numeric database row IDs remain auto-increment; signing bytes/non-entity tokens are not application identifiers.
- Issue signed licenses out of band (customer portal while online, or approved file transfer/USB) and **import locally**, without requiring a runtime Internet call.
- Define an explicit expiry, grace, clock-skew and machine-replacement/recovery policy. Never allow accidental clock drift to silently corrupt sales or delete records; define read-only/report/export behavior after expiry.
- Offline revocation cannot be instant without contacting the licensing authority; manage this contractually and do not promise tamper-proof enforcement on infrastructure fully controlled by the customer.
- Offline activation is separate from cloud subscription renewal; no recurring online heartbeat in an air-gapped installation.

## 5. Core features and third-party boundaries

| Workflow | Fully local operation | Notes |
| --- | --- | --- |
| Staff login and access control | Yes, after local Identity/setup | No online email OTP or cloud login dependency |
| Product / Salon service masters | Yes | Data stored locally |
| Purchase, stock, POS, cash sale and local appointment | Yes, when implemented | All writes and stock/money transactions remain in local SQL Server |
| Local reports and printing | Yes | Local printer drivers and fonts installed; browser/print tests required |
| Card gateway, UPI verification, online payment capture | Not offline | Show clear unavailable/pending state; never label an unverified payment 'paid' |
| SMS, WhatsApp, remote email, cloud backup | Not offline | Disabled or queued only with a separately approved delivery/retry design |
| Remote customer booking site | Not available during isolation | Requires reachable networking or separate online component, not in V1 |
| Multiple branches disconnected from one another | Not guaranteed | Requires later store-and-forward sync, conflict handling and idempotency |

A disconnected local LAN still requires local networking between devices and server. A fully standalone single-PC server+browser installation is possible but is a separate tested hardware/deployment profile.

## 6. Backups, maintenance and recovery

- Customer controls the server: agree who owns OS patching, SQL licensing, UPS/power, network, hardware, data retention and support access before the sale.
- Schedule **encrypted SQL backups**, with copies off the primary server/physical disk (for air-gapped environments, rotated encrypted offline media). Test actual restore regularly. Files/attachments, license file, certificates/key backups, and DB schema version must also be recoverable.
- Deliver reviewed, **signed** offline release packages via controlled file transfer/USB. Operator checks signature and compatible schema version, takes backup, stops/drains operations, applies reviewed EF migrations using controlled credentials, checks health and runs post-upgrade smoke tests. Roll back safely using the documented restore procedure; never destructive auto-migrate at normal startup.
- Offline support and diagnostics require permissioned, sanitized log export; no hidden outbound telemetry. Remote support is opt-in only when network access exists and is approved.
- Each installed instance is a separate operational release target; plan version inventory, patch SLAs, critical security advisories and hands-on recovery procedures.
- Data residency, vendor access, payment/tax rules and hardware/service commitments belong in the deployment contract.

## 7. No automatic offline ↔ cloud synchronization in V1

On-premise and hosted modes are **alternative deployment targets**, not two writable replicas of the same live business. Copying live data between them later demands stable document identity, stock/invoice sequence reconciliation, conflict resolution, retries, idempotency, payments consistency, security and disaster recovery design.

For a first release, support **controlled export/import or a one-time migration** with downtime, backup and reconciliation, instead of an invisible always-on sync. Future hybrid support must have a separate SRS and load/failure acceptance tests.

## 8. Work order and delivery gates

| Phase | Deliverable | Minimum test |
| --- | --- | --- |
| A — Shared core data boundaries | Local-control vs hosted-control setup, operational EF context, configuration switches | Both environments build/test without changes to business logic |
| B — Single on-premise pilot | IIS/React/API/SQL local installation and one-time admin setup | Unplug Internet: login, authorization, product CRUD and report still work |
| C — Reliable business operations | Local purchase, stock ledger, POS and/or salon checkout as modules ship | Concurrent stock/money posting, restart and outage recovery, local printing |
| D — License and upgrades | Signed importable offline license, upgrade/restore playbook | No Internet during activation; expired/grace policy; rollback and corrupted package rejected |
| E — Enterprise delivery | Deployment documentation, backups, monitoring, support agreement and hardware matrix | Fresh install, role isolation, power loss, DB outage, restore, HTTPS and signed-upgrade scenarios |
| F — Optional hybrid | Separate reviewed synchronization architecture | Only when contracted and fully tested; NOT part of offline V1 |

**Pilot acceptance examples:**
- Remove external Internet; existing local users can sign in after reboot; create a product, post a local cash sale, print receipt, and view the correct stock balance when those modules are implemented.
- Domain points to an internal DNS address; unauthorized local machines/users cannot obtain cross-business or cross-role data.
- Reboot server; authentication and persisted key ring recover correctly.
- Disconnect SQL Server / run out of disk space; fail safely without duplicate sale/payment or false success.
- Restore encrypted backup to spare hardware, re-establish license/certificates/secrets under an approved replacement policy, and reconcile balances.
- Install upgrade from authenticated offline package; incompatible/unsigned packages fail closed.

**Existing licensed components:** Reuse the private `Dharmu.Security` packages for Windows offline validation and verified `Dharmu-License-Manager` issuer logic behind Business SaaS Platform Admin; do not copy private code into this public repository. See [License Management Integration](./LICENSE_MANAGEMENT_INTEGRATION.md).

**Current state:** No on-premise install package, local login bootstrap, offline license, operational DB split, approved local HTTPS/certificate automation, backup/restore drill, or Internet-disconnected acceptance test exists in the current `business_saas` code. This document is planning, not a feature release.

## Official deployment references

- [Microsoft: Publish ASP.NET Core to IIS](https://learn.microsoft.com/en-us/aspnet/core/tutorials/publish-to-iis)
- [Microsoft: .NET Hosting Bundle for IIS](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/iis/hosting-bundle?view=aspnetcore-10.0)
- [Microsoft: Persist ASP.NET Core Data Protection keys](https://learn.microsoft.com/en-us/aspnet/core/security/data-protection/configuration/default-settings)
