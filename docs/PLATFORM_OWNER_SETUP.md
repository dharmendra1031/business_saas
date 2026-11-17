# Platform Owner and Customer Access

## Roles

- **Platform Super Admin:** One configured platform owner account. May open the read-only platform business directory and its subscription summaries, but cannot use customer business operational APIs.
- **Customer:** Every other signed-in account. May create multiple independently managed businesses. Business creation grants the creator the **Owner** membership in that particular business.
- **Business Owner / Manager / Cashier:** Roles are checked against each `BusinessId`. A business role never grants platform access.

All users and business records use numeric SQL identity IDs. Operation request keys are not entity IDs.

## Create a dedicated Super Admin account (trusted operator only)

The project includes an **offline one-time bootstrap command**, not a public API. It creates an Identity account with a strong hashed password, confirms its email only under trusted-operator responsibility, and prints its numeric User ID. No account or password is seeded in Git.

1. Verify that the dedicated email inbox is controlled by the platform operator. Configure the intended SQL Server connection through `ConnectionStrings__Default` and apply the reviewed migrations first. **Back up an existing database before changing it.**
2. From the repository root, run:

   ```powershell
   .\scripts\bootstrap-platform-owner.ps1 -Email "admin@your-company.example"
   ```

   The script prompts for a password without displaying it and requires at least 16 characters. It passes the password temporarily to the offline process, clears the environment variables when finished and does not print or commit the password.
3. The command prints the newly created numeric User ID, for example `42`. Set `Platform__OwnerUserId` to **that actual ID** using the API host's protected settings and restart the API.
4. Login using the dedicated account and confirm the Super Admin dashboard is shown. Existing accounts are rejected by the bootstrap; it will not silently promote a Customer.

**Do not** send the production admin password in chat, add it to command-line arguments or commit it to an appsettings file. The command must run in a trusted host with restricted console and environment access. A successful bootstrap does not mean a live account was created unless an operator actually ran it against the intended database.

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
