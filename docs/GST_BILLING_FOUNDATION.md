# GST-ready billing draft foundation

Development branch, 18 November 2026. These APIs create only internal drafts. They do not issue statutory invoices or credit notes.

## Endpoints and permissions

Only the MFA-verified Platform Owner can use these endpoints:

- GET /api/platform/billing/tax-profiles/{businessId}
- POST /api/platform/billing/tax-profiles/{businessId}
- POST /api/platform/billing/invoice-drafts with numeric payment ID in the id field
- POST /api/platform/billing/credit-note-drafts with numeric refund ID in the id field
- GET /api/platform/billing/document-drafts/{id}

Tax-profile POST accepts expectedRevision, issuerName, issuerAddress, issuerGstin, issuerStateCode, recipientName, recipientAddress, optional recipientGstin, recipientStateCode, serviceCode, serviceDescription, invoiceSeries, gstRate, pricesIncludeTax and reason. Reason requires 10–200 characters. Creation expects revision 0; later saves must use the current revision. Every write is audited.

The profile supports Indian INR transactions. The GSTIN format and state-prefix checks are input validation, not proof of a verified tax registration. Tax classification, SAC, issuer and recipient details, tax rate and registered address require external review. Rates are not hardcoded.

## Draft behavior

An invoice draft requires a recorded gateway-verified captured payment, the matching provider order and amount, and tax-inclusive captured pricing. It uses an immutable snapshot of the profile and its revision.

For same-state supply, the configured tax is split into CGST and SGST. For different-state supply, IGST is calculated. Rounding preserves the captured gross amount. Tax-inclusive pricing only: exclusive-price profiles are blocked until the payment capture itself includes the approved tax.

A credit note draft requires the gateway-verified processed refund and the original invoice draft. The credit's taxable value and tax amounts are allocated proportionately, and cumulative credits cannot exceed the original amount. Duplicate payment and refund drafts are protected by unique indexes, transaction isolation and replay-safe API behavior.

All primary keys are numeric auto-increment SQL Server identity fields. Razorpay strings remain external references only. A tax-profile edit does not mutate an existing draft's tax snapshot.

## Legal release gates

Every document remains Status=Draft and DocumentNumber=null. The configured invoice series and fiscal year are **future numbering metadata**, not a legal invoice number. Tax-invoice and credit-note issuing, printing, PDF signing, emailing, GST-return reporting and tax-liability postings are deliberately disabled.

Before activation, verify supplier GSTIN/registration, recipient state and place of supply, SAC and tax rates, price basis, eligibility for e-invoicing, reverse-charge requirements, tax document signature and reporting, approved refund/credit-note policy and concurrency-safe annual serial numbering (Rule 46 and Rule 53, max 16 characters). Separate Razorpay capture from bank settlement.

CI checks require a SQL migration and SQLite end-to-end coverage. External gateway sandbox deliveries and statutory sign-off remain outstanding.
