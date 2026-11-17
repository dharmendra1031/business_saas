# Super Admin Account and MFA Setup

## Access rules

- The platform has **one configured Super Admin** identified by the positive numeric `Platform:OwnerUserId`.
- A verified MFA session (`amr=mfa`) is required for all `/api/platform` read-only directory, overview, audit and license-access endpoints.
- Before MFA enrollment, the configured owner can only use account-security setup actions, not platform management.
- Customers may own multiple separate businesses, each restricted by `BusinessId` and membership. Platform Owner cannot access tenant operational APIs, even through an old membership.
- Older `platform.permission` claims are not sufficient for Super Admin access. No public endpoint promotes an account.
- Database entity IDs remain numeric auto-increment. No UUID/GUID IDs were introduced.

## Dedicated account provisioning (trusted operator)

1. Verify control of the dedicated operator email. Connect to the **intended SQL Server** through `ConnectionStrings__Default`; apply reviewed EF migrations after a verified backup.
2. From the repo root on a trusted Windows host, run:

   ```powershell
   .\scripts\bootstrap-platform-owner.ps1 -Email "admin@your-company.example"
   ```

   The script prompts for a password (16+ characters) and creates a **new** verified Identity account. It refuses an existing account. Nothing is seeded by default; the command has not been run against a production database by this implementation.
3. Note the numeric User ID returned by the command. Configure `Platform__OwnerUserId` with the real ID in the API host's **protected environment**, not in source control, and restart the API. Example for disposable local development:

   ```powershell
   $env:Platform__OwnerUserId = '42' # Replace 42 with the actual ID
   ```

4. Sign in with the dedicated email and password. The application will require **Authenticator Setup** before opening Super Admin.
5. Re-enter the password, copy the private setup key into a TOTP authenticator app, enter the current six-digit code, and securely store all **eight one-time recovery codes**. Enabling MFA signs the account out.
6. Sign in again using password plus authenticator code (or one unused recovery code). Confirm `GET /api/platform/access` reports `mfaVerified: true`; the read-only Dashboard and Security & Audit will now open.

**Never share passwords, TOTP setup keys or recovery codes in chat, logs, screenshots or git.** Recovery codes appear only once. If the authenticator and all recovery codes are lost, there is **no self-service reset** yet; an authenticated trusted-operator recovery procedure must be established before production.

## Security behavior

- A password-only session cannot access any Super Admin directory or audit data.
- Identity's built-in TOTP provider and lockout rules validate authenticator codes; incorrect password during setup is also subject to account lockout.
- The verification challenge uses a short-lived two-factor cookie. Recovery codes are single-use, and invalid recovery attempts increment the lockout counter.
- Sensitive writes require CSRF validation; MFA endpoints also have IP-based request limits. Browser Auth cookies are HttpOnly and Secure in production.
- Login and MFA enrollment actions have numeric platform audit events. Events are not cryptographically tamper-evident.
- The owner ID must be consistently configured across API instances; removing it revokes platform access.

## Current limitations

The Super Admin UI includes **Overview, Customers, Businesses, and Security & Audit** (read-only). Subscription plan editing, trial extension, suspension/reactivation, payments, license operations, login-failure monitoring, audited recovery procedures, verified-email self-service registration, production restore drills and load/security hardening remain pending. The platform is **not yet production-ready**.
