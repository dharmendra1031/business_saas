# Platform Owner and Customer Access

## Roles

- **Platform Super Admin:** One configured platform owner account. May open the read-only platform business directory and its subscription summaries, but cannot use customer business operational APIs.
- **Customer:** Every other signed-in account. May create multiple independently managed businesses. Business creation grants the creator the **Owner** membership in that particular business.
- **Business Owner / Manager / Cashier:** Roles are checked against each `BusinessId`. A business role never grants platform access.

All users and business records use numeric SQL identity IDs. Operation request keys are not entity IDs.

## Configure the single platform owner

The API reads `Platform:OwnerUserId` through standard ASP.NET Core configuration. Only the authenticated user with this **numeric user ID** is the platform owner. The value must be positive. When it is absent or invalid, **no account has platform owner access**.

1. Provision a **dedicated** account through a trusted operator process. In a disposable Development environment, create the account using the existing registration flow, sign in, then read its numeric `id` from `GET /api/auth/me`. **Do not use the account for customer business operations.**
2. Set `Platform__OwnerUserId` in the API host's protected environment configuration to that exact numeric ID. For local PowerShell development only:
   
   ```powershell
   $env:Platform__OwnerUserId = '42' # Example only; use the actual numeric user ID
   ```
3. Restart the API process with the setting loaded. Sign in with the dedicated owner account. It should open **Super Admin** rather than **My Businesses**.
4. Confirm `GET /api/platform/access` returns `isPlatformOwner: true` for the owner and `false` for a customer. Confirm `GET /api/businesses` is forbidden for the platform owner and `GET /api/platform/businesses` is forbidden for customers.

**Never** put a real owner ID, password, connection secret or production account details in source control. Do not seed a default admin password or automatically promote the first registered account. Provision and verify the account before enabling platform administration. Production public registration is still disabled pending verified email delivery.

For multi-instance deployment, the same reviewed owner ID must be configured on every API instance. Changing or clearing this setting revokes/reassigns platform access across instances after configuration reload or restart. Keep access to the host configuration restricted and auditable.

## Security boundaries

- The only platform owner authority is `Platform:OwnerUserId`. Historical `platform.permission` database claims do **not** grant Super Admin access.
- The platform owner can read the platform-wide business directory, but cannot open any `/api/businesses` customer data or perform tenant operations, **even if it previously had a business membership**.
- Business Owners and staff are limited by server-side membership, role, business subscription and location rules.
- No public endpoint can promote a user to Super Admin, change the owner ID or bypass the customer boundary.
- Existing business users are unaffected and can create/manage multiple businesses through their membership.

## Current scope and release limits

The Platform Super Admin UI currently supports **read-only** Business Directory and subscription status. Actual license management actions, plan editing, trial extensions, platform audit logs, verified-email onboarding, privileged-account MFA and production readiness gates are **not implemented** by this change. Do not use this configuration alone as a production launch sign-off.
