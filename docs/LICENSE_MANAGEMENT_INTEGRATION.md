# Business SaaS — License Management Integration

**Status:** Integration plan; not implemented in `business_saas`  
**Updated:** 2026-11-10  
**Scope:** One customer-facing Business SaaS platform manages hosted subscriptions and offline licenses, reusing verified components from existing private projects.

## 1. Product and deployment decisions

- **One product and one Super Admin interface:** License List, Create/Issue, Activation Import, Renew, Transfer/Reissue, Revoke, and Audit History are route/screens in the existing Business SaaS React application. No second customer-facing admin portal.
- **Hosted SaaS** continues using `BusinessSubscriptions` and centrally enforced entitlements. It does **not** require an offline license file.
- **On-Premise Windows** uses a locally verified signed `.lic` file and locally hosted Identity and business database. Its core operations must work without contacting our servers.
- **Private signing key** never ships to customers, source control, or published client packages; it resides on an approved key-protected Windows signing environment with tightly restricted administrator access.
- All application-entity primary keys, including license records, remain numeric auto-increment. Cryptographic keys and hashes are *not* entity IDs.

## 2. Verified source inventory (private, development branches)

| Source | Actual code | Reuse approach |
| --- | --- | --- |
| `dharmendra1031/Dharmu-License-Manager` | Customer/Product/License/Activation/Audit MVC app; ECDSA P-256 signing from Windows CNG; signed activation-request parser; renew/deactivate/revocation; SQL schema | Extract/adapt reviewed domain services into the hosted Platform Admin API, with one authoritative central license record and one React admin UI; **do not copy its MVC pages or create a duplicate administration application** |
| `dharmendra1031/Dharmu.Security` | Core ECDSA validation; signed machine-bound activation request; Windows CNG/optional TPM machine identity, DPAPI secrets, trusted-clock check and ASP.NET Core license middleware | Consume versioned **private** `Dharmu.Security.Core`, `Dharmu.Security.Windows`, `Dharmu.Security.AspNetCore` packages from a private feed for the Windows offline deployment only |

**Repository visibility boundary:** Both source repositories are private while `business_saas` is public as currently configured. Do **not** paste private licensing source, license signing keys, customer databases, credentials or proprietary assets into `business_saas`. Approve intended licensing, packaging and publication boundaries first. Use authenticated package consumption and private CI credentials with least privileges; never embed them in repo.

**Code compatibility observation:** The existing signed license payloads have aligned v1 fields `SchemaVersion`, `LicenseId`, `CustomerId`, `ProductId`, `ProductCode`, `MachineCode`, `IssuedAtUtc`, `ValidFromUtc`, `ExpiresAtUtc`, `IsLifetime`, `IsRevoked`. Signed activation requests use v2 envelope/payload with device-key proof. This is *source-level compatibility*, not proof that a Windows device-to-device activation/renewal run passes.

## 3. Ownership and operating architecture

1. The Business SaaS **Platform Admin** (hosted) is the only user-facing admin experience. Authorize every license administration API endpoint with a platform-only permission; a Business Owner/Cashier must not administer licenses.
2. Central Platform API owns customer/business-license mapping, license terms/feature limits, issue/reissue history and audit. Reuse existing signature and activation parser logic after verification; avoid parallel license data stores with ambiguous authority.
3. The signing operation must execute **only in an approved protected Windows hosting context** with access to its non-exportable CNG key; if the hosted API runs Linux or general multi-tenant processes without key isolation, a constrained private internal signer is required instead of bundling the signing key in an ordinary API container. Keep it internal to the product, not a customer-facing portal.
4. A Windows on-premise deployment validates the file, device identity and trusted clock locally using the private NuGet package. It has its own local Identity/membership database, not a call to a hosted control plane.
5. Do not expose `LicenseId` in place of `BusinessId`: license assignment and business ownership are related but distinct; cross-system references need explicit validated mapping. In a dedicated or migrated DB, IDs may repeat across databases.
6. Use `DeploymentMode=Hosted|OnPremise` configuration to switch commercial licensing policy, while sharing actual operational business code. Do not create duplicate branches or fork the business logic.

## 4. License flow

**New install:** Locally provision Windows device key → generate signed activation-request JSON → transfer to Platform Admin by authenticated upload (through an online operator or secure offline media) → verify signed device proof and ProductCode → approve customer/business binding and term → sign `.lic` → download and import into offline installation → local validation.

**Renewal:** Admin adjusts approved term and exports a newly signed license. Customer imports file. Do not reinstall software or reset database.

**Transfer:** Verify entitlement and replacement reason, deactivate old machine assignment for future issue, sign a new bound license and record the event. Fully offline old installations are not instantly revocable; finite license validity remains important.

**Expiry/grace:** Explicit, versioned policy for warnings, renewal grace, read-only reports/export and fail-safe behavior; no data deletion. Existing `Dharmu.Security.AspNetCore` middleware returns HTTP 403 for unlicensed requests. Adapt verified entitlement enforcement to distinguish **writes** from authorized **read/export** operations before customer launch; do not simply exempt every GET or trust UI buttons. Purchased modules, user limits and location limits also require server-side enforcement after signed payload schema and contract evolution.

**Hosted subscriptions** are independent from offline `.lic` issuance. License issuance is administrative; never make POS transaction processing depend on a real-time remote license lookup.

## 5. Small data changes — only when integration ships

- Reuse Platform Identity, Businesses, BusinessSubscriptions and existing audit conventions.
- Add central `Licenses` only for the working admin issuance workflow, with numeric `Id`, `BusinessId` or approved customer mapping, `ProductCode`, term/type/status, machine binding, signed payload schema version and timestamps.
- Add minimal immutable issue/reissue history *when* file generation/renewal is delivered, not speculative tables for every future license family.
- Keep only verified signed file and protected local validation state on on-premise installation; do not copy the hosted License Manager database to the customer server.

## 6. Implementation sequence

| Stage | Work | Gate |
| --- | --- | --- |
| L0 — Verify existing libraries | Get authoritative Windows CI build/test logs; fix real compilation/test defects if present, align signing/verification contracts | Both repositories build and tests pass, with recorded workflow and commit SHAs |
| L1 — Package | Publish versioned private `Dharmu.Security` NuGet artifacts and consume them from a clean Windows sample build | Package restore on authorized CI runner + signed file from manager validates in offline client |
| L2 — Platform admin API/UI | Integrate licensed domain services into hosted API, central records and React platform-only screens | Admin authentication, audit, issuance and unauthorized cross-role tests |
| L3 — Offline mode | Add Windows deployment configuration, local Identity/bootstrap, activation import, feature/expiry policy | Unplug Internet and test login, license activation, entitlement denial, data access and reboot |
| L4 — Hardening | Key rotation, reissue after server replacement, revocation file semantics, clock recovery, backup/restore | Second-machine clone rejection, wrong product, tamper, expiry/grace, signed-key rotation, IIS key permissions |
| L5 — Release | Document supported Windows/SQL versions, private package update policy and on-prem support contract | Fresh installation + upgrade + restore on an actual pilot |

**Never ship until:** production signing-key access controls and rotation, cross-role authorization, restore drills, package authenticity, Windows IIS key permissions, external Internet unplug test and end-to-end signed activation tests pass.

## 7. Known findings and dependencies

- **Verified 2026-11-10 Windows CI:** Dharmu.Security build + **8/8 tests PASS** on [rerun attempt 2](https://github.com/dharmendra1031/Dharmu.Security/actions/runs/39233145919/attempts/2), commit `a0b4b04`. Dharmu-License-Manager build + **5/5 tests PASS** on [run 42784765745](https://github.com/dharmendra1031/Dharmu-License-Manager/actions/runs/42784765745), commit `39abae1`. Its previous restore failed because of known vulnerabilities in transitive `System.Security.Cryptography.Pkcs 9.0.4` (NU1902/NU1903), resolved by explicitly selecting patched `10.0.14` in the web project's package references. These passing unit tests do not establish real two-machine activation or production readiness.
- A private GitHub Packages publish workflow exists in `Dharmu.Security`; publishing a workflow file is not equivalent to having a verified released package.
- Current license payload v1 does not convey module/user/location entitlements; design a **backwards-compatible, versioned** extension and compatibility tests rather than silently reinterpreting signed bytes.
- Current offline validator is Windows-specific in its host integration. Keep cloud environment provider and operating-system independence unless separately tested.
- Current Security license middleware rejects all protected operations on invalid license, so **read-only after expiry is a product-level change**, not implemented.
- Private projects should be maintained as the authoritative package sources, not copied into public product repo.
- Existing plans: [Offline Deployment](./OFFLINE_ON_PREMISE_DEPLOYMENT.md) and [White Label](./WHITE_LABEL_DATABASE_IMPLEMENTATION.md).
