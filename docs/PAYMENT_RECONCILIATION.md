# Payment reconciliation and verified refund tracking

**Implemented on development, 18 November 2026.** This batch adds manual recovery of uncertain Razorpay order mappings, a read-only review of captured payments and processed refunds, and webhook-driven refund observation. It **does not** create refunds, issue GST invoices, release settlements or change access automatically on refund.

## Admin review

The MFA-verified Platform Owner can use **Plans & Billing → Payment Reconciliation** to see captured payment records, processed refund totals, remaining balances and review status. Figures are payment observations, **not** statutory revenue, GST totals or bank settlements.

The review API is `GET /api/platform/billing/reconciliation`. It returns up to 50 payment rows per page and the first 50 requests in `CreatingOrder` or `OrderUncertain`. Non-platform users and owner sessions without verified MFA cannot access it. The API is read-only.

## Recovering a lost order response

When Razorpay creates an order but the application does not receive or persist the response, the request is intentionally stuck at `OrderUncertain`, not retried automatically. A verified Platform Owner:

1. Finds the existing Razorpay order in the merchant dashboard using its deterministic receipt `bs_<numeric request ID>`.
2. Opens **Verify Order** in Payment Reconciliation and enters that existing `order_...` reference and a reason.
3. The server calls the Razorpay Fetch Order API and independently matches its ID, receipt, exact INR amount, currency and eligible order state.
4. If all fields match, the existing payment request is bound to the verified order in a transaction, and `Billing.OrderReconciled` is audited.
5. Normal `payment.captured` webhooks can then safely resolve the existing order. No new order, payment or subscription activation is performed by reconciliation itself.

API: `POST /api/platform/billing/payment-requests/{numericId}/reconcile-order`.

If the external receipt/order cannot be found, leave the request on hold, inspect Razorpay's merchant account and request logs, and reconcile manually. Do not reissue an order while its external outcome is unknown.

## Refund observation

Subscribe to the Razorpay `refund.processed` webhook in addition to `payment.captured`. A refund is only recorded after:

- Verifying HMAC-SHA256 against the unchanged webhook body using the configured webhook secret.
- Deduplicating the provider event reference.
- Fetching the refund independently using `GET /v1/refunds/{refundId}`.
- Matching refund ID, original payment ID, currency (INR), processed status and exact amount.
- Confirming a captured subscription payment exists and that cumulative processed refunds do not exceed its amount.

Each `SubscriptionRefund.Id` is a **numeric SQL identity**, with a unique external `ProviderRefundId` string. The event log uses its existing numeric ID and stores the provider event string separately. A repeated event or another signed event for the same refund cannot inflate refunded totals. The refund entry, event entry and captured-payment `RefundReview` flag commit together.

Refund states other than `refund.processed` do not increase processed refund totals. **This API does not initiate refunds or touch cards.** Processed refunds flag a payment for manual investigation, but do not automatically cancel or suspend the business. Refund-based entitlement adjustment, prorating and credit-note rules require a separately approved policy. A full refund could otherwise leave subscription access active; manual suspension should be considered until that policy is implemented and tested.

## Tax and financial controls pending

Do **not** issue tax invoices until the issuer entity, GST registration status, registered address, state/place of supply, SAC/tax classification, price inclusive/exclusive basis, numbering sequence and credit-note rules are verified. Do not treat Razorpay captured amounts as settled money; separate bank settlement reconciliation is required. No business registration numbers or tax rates are hardcoded.

## Tests and deployment

The SQLite endpoint integration tests use a fake Razorpay gateway. They verify order receipt mismatch and idempotent recovery, invalid webhook signature, API-confirmed refund amount mismatch, partial refund record, duplicate events, over-refunds and ignored failed refunds. SQL Server CI verifies migration `20261118170000_AddVerifiedSubscriptionRefunds` and model snapshot parity.

**Production release remains blocked** until real Razorpay Test Mode credentials, external sandbox webhook deliveries, deployment migration/backup, supported tax settings and a formal refund policy are verified.

Official references:
- [Razorpay refund API list](https://github.com/razorpay/markdown-docs/blob/master/api/refunds.md)
- [Refund webhook event types](https://github.com/razorpay/markdown-docs/blob/master/payments/refunds/subscribe-to-webhooks.md)
- [Fetch Order](https://razorpay.com/docs/api/orders/fetch-with-id/)
