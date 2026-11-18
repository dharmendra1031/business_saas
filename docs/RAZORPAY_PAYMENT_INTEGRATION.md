# Razorpay payment activation — controlled integration

**Scope (18 Nov 2026):** INR only, one Razorpay Order per internal numeric payment request; merchant-configured payment capture; signed webhook confirmation, independent payment fetch, and period-aware paid access. **Not a tax-invoice implementation.**

## Configuration (server only)

Keep secrets in environment variables, managed secrets or a protected deployment store. Never commit them to the repository or expose them in the React app.

| Environment variable | Meaning |
|---|---|
| `Payments__Enabled` | `true` to enable checkout and webhook acceptance |
| `Payments__AllowLive` | `false` by default; live payments require explicit release approval |
| `Razorpay__Mode` | `Test` for sandbox or `Live` after approval |
| `Razorpay__KeyId` | Razorpay merchant key ID; only this public identifier reaches Checkout |
| `Razorpay__KeySecret` | Backend-only Basic Auth secret for Razorpay Orders/Payments APIs |
| `Razorpay__WebhookSecret` | Independent secret configured on the Razorpay webhook |
| `Razorpay__PreviousWebhookSecret` | Optional previous webhook secret for retried events during rotation |

The defaults are disabled in `appsettings.json`. A complete and valid configuration is required. Never use a Live key in Test mode. Configure the Razorpay dashboard webhook to post to `https://<api-domain>/api/payments/razorpay/webhook`, subscribed to `payment.captured`. Enable automatic capture in the merchant settings. Verify network access to `https://api.razorpay.com/v1`.

## Workflow

1. MFA-verified platform owner publishes prices, drafts a service period, and prepares a numeric payment request. A customer cannot edit the price.
2. The tenant's **Owner** only (not Manager/Cashier) sees their current-period request. The API reserves a one-time order creation attempt, then creates a Razorpay Order server-side with exact INR paise, a deterministic receipt, and fixed currency. The public key and provider order ID are sent to Checkout, never the key secret.
3. `payment.captured` is accepted only with an HMAC-SHA256 signature over the **unchanged raw HTTP body**, validated with a constant-time comparison. The provider event reference (`X-Razorpay-Event-Id`) is deduplicated.
4. The backend separately fetches the captured payment from Razorpay's Payments API and checks payment ID, provider order ID, `captured` state, currency and exact amount before storing the payment.
5. One numeric `SubscriptionPayment.Id` is stored per numeric `SubscriptionPaymentRequest.Id`. The one-time event log uses numeric IDs, while externally supplied Razorpay IDs remain string references. The payment record, event and subscription update commit atomically.
6. The platform converts the draft's start-inclusive/end-exclusive service dates from the business timezone to UTC. Access only works within the verified paid validity. A same-plan contiguous renewal may extend the end date.
7. If the subscription is Suspended, capture **does not** unsuspend it: `HeldSuspended` is recorded and only the existing MFA-protected Admin reactivate action may resume access. Expired, mismatched or noncontiguous periods become `CapturedReview` and do **not** activate access.

## Failure rules

- **`AwaitingGateway`**: no external order; no money received.
- **`CreatingOrder`**: creation in progress; a second checkout cannot create another order.
- **`OrderUncertain`**: gateway timeout/error after reserving; **do not automatically retry**. Reconcile the existing Razorpay order/receipt and stored request manually before allowing another attempt.
- **`OrderCreated`**: a gateway order exists; no captured payment verified yet.
- **`Captured`**: verified capture and eligible paid subscription state; a suspended account remains suspended.
- **`CapturedReview`**: money was captured but activation was not allowed. Support must reconcile and address the payment; never silently discard or auto-refund it.

Only valid `payment.captured` events drive payment posting. UI callbacks, `payment.authorized`, user-submitted payment IDs, mismatched amounts and repeated webhooks never grant paid access. The webhook has a strict body-size limit and cryptographic signature validation; it is the **only** CSRF-exempt write route.

## Deployment checklist

- Approve the issuer's legal entity, tax registration, place-of-supply/tax treatment and invoice numbering before accepting live payments or issuing statutory invoices. This batch issues **no invoice or receipt** and makes no tax-rate assumptions.
- Back up the intended SQL Server, review the migration `20261118150000_AddRazorpayPaymentsAndPaidValidity`, and apply it in the intended environment; this does not prove any production database was migrated.
- Test Razorpay **Test Mode** end-to-end with real sandbox credentials and actual merchant webhook deliveries. Automated CI uses a simulated gateway, not Razorpay's external sandbox.
- Check webhook retries, API timeouts, duplicate event IDs, different events for the same payment, suspicious amount/currency, suspended/expired accounts, future service periods, and reconciled gateway orders.
- Confirm HTTPS, server clock synchronisation, secure secret storage/rotation, persistent Data Protection keys, environment-specific merchant accounts and operational reconciliation before enabling live payments.
- **Never** report captured payments as tax revenue/invoice totals until the tax/invoicing ledger and settlement/refund policies are approved.

Official references: [Create Order](https://razorpay.com/docs/api/orders/create/), [Validate Webhooks](https://razorpay.com/docs/webhooks/validate-test/), [Payment webhook events](https://razorpay.com/docs/webhooks/payments/).
