# Owner Invitation Delivery — Deployment & Acceptance

## Scope and trust boundary

Only a configured Super Admin with a completed MFA session can issue owner invitations. The invite is for a **new Identity account with verified email**, not permission to access a business. The platform owner must then select the verified email in **Create Business → Owner & Access** and confirm provisioning. A link alone does **not** create a business, owner membership, trial or permission.

Invitations are single-use 256-bit random bearer tokens. Only a SHA-256 hash is stored in `PlatformOwnerInvitations`. They expire after 48 hours. Subsequent requests for the same address are throttled to one per 10 minutes. Failed SMTP delivery revokes the invitation; links are never returned to the API caller or logged. SMTP relay acceptance does not guarantee inbox delivery.

## Required protected configuration

Configure these environment variables on the API host (not in source control, screenshots or client JavaScript):

- `OwnerInvitations__PublicBaseUrl` — public HTTPS origin, e.g. `https://your-verified-saas-domain.example/` (path must be `/`, no query or fragment)
- `OwnerInvitations__SmtpHost` — authenticated TLS SMTP hostname
- `OwnerInvitations__SmtpPort` — SMTP STARTTLS port (normally 587; set according to your provider)
- `OwnerInvitations__SmtpUser` — SMTP relay login
- `OwnerInvitations__SmtpPassword` — relay credential **from the deployment secret store**
- `OwnerInvitations__From` — approved sender address on a verified sending domain

No email relay configuration is committed or presumed to be provisioned. Until all settings exist, the backend returns **503** instead of pretending an invitation was sent. The public base URL must serve the SPA at `/?invite=<secret>` and use HTTPS. Configure your DNS sender records, bounce monitoring and any provider-specific limits outside the app.

The current sender uses the existing .NET SMTP stack with STARTTLS enabled and a 15-second connection timeout. Before a production rollout, verify that the provider requires and negotiates TLS and handles delivery/bounce errors. No new third-party mail dependency was introduced.

## Operator acceptance checklist

1. Apply and review SQL Server migration `20261123190000_PlatformOwnerInvitations` after a backup; check `/api/ready`.
2. Configure the protected SMTP settings and public HTTPS SPA origin on the intended environment.
3. Sign in as Super Admin with MFA, open **Businesses → Create Business → Owner & Access**, enter an email that is not registered, select **Verify Owner**, then **Send secure email invitation** with an audit reason.
4. Verify the actual inbox message and link. Confirm that the public page removes the token from the URL, displays the expected email and lets the recipient choose a strong password.
5. Accept once, check that a second attempt fails; confirm the new account is email-verified but initially has **no business membership**.
6. Reverify the Owner in the Super Admin wizard, create the Starter trial business, check location, tenant separation, trial dates and idempotent repeat handling.
7. Verify invalid/expired links, delivery failures, 10-minute resend throttle, audit entries, unauthorized access, 360px mobile layout and password-policy errors.

**Known limitation:** This phase intentionally does not send invitations to existing/unverified Identity accounts, assign a future business before acceptance, provide invitation cancellation UI, or implement automated bounce processing. Owner provisioning remains a separate privileged action. No screen is marked DONE until live email delivery and acceptance pass.
