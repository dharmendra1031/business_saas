import { useEffect, useRef, useState, type FormEvent } from 'react'
import { api, operationKey, post, SessionExpiredError } from './api'

interface BillingPreview {
  businessId: number
  businessName: string
  planCode: string
  publishedPlanVersionId: number
  version: number
  billingCycle: string
  periodStartDate: string
  periodEndDate: string
  currencyCode: string
  amount: number
}

interface BillingDraft {
  id: number
  businessId: number
  businessName: string
  publishedPlanVersionId: number
  planCode: string
  billingCycle: string
  periodStartDate: string | null
  periodEndDate: string | null
  currencyCode: string
  amount: number
  createdAtUtc: string
  status: string
  paymentRequestId: number | null
  paymentStatus: string | null
}

interface PaymentRequest {
  id: number
  billingDraftId: number
  businessId: number
  businessName: string
  currencyCode: string
  amount: number
  status: string
}

interface PaymentPage {
  items: PaymentRequest[]
  total: number
  page: number
  pageSize: number
}

interface ReconciliationRow {
  id: number
  paymentRequestId: number
  providerPaymentId: string
  businessId: number
  businessName: string
  amount: number
  currencyCode: string
  refundedAmount: number
  netAmount: number
  activationStatus: string
}

interface PendingGatewayOrder {
  id: number
  businessId: number
  businessName: string
  amount: number
  currencyCode: string
  status: string
}

interface ReconciliationPage {
  items: ReconciliationRow[]
  total: number
  page: number
  pageSize: number
  unresolvedOrders: PendingGatewayOrder[]
}

interface BillingPage {
  items: BillingDraft[]
  total: number
  page: number
  pageSize: number
}

function paymentStatusLabel(status: string | null): string {
  switch (status) {
    case 'AwaitingGateway': return 'Awaiting checkout'
    case 'CreatingOrder': return 'Creating gateway order'
    case 'OrderCreated': return 'Awaiting customer payment'
    case 'OrderUncertain': return 'Gateway reconciliation required'
    case 'Captured': return 'Captured — verified'
    case 'CapturedReview': return 'Captured — needs review'
    default: return status ? `Status: ${status}` : 'Payment not requested'
  }
}

function money(amount: number, currency: string) {
  return new Intl.NumberFormat('en-IN', {
    style: 'currency',
    currency,
    minimumFractionDigits: 2
  }).format(amount)
}

export default function PlatformBilling({ business, onChooseBusiness }: { business: { id: number; name: string } | null; onChooseBusiness: () => void }) {
  const businessId = business?.id
  const businessIdRef = useRef(businessId)
  businessIdRef.current = businessId
  const previewGeneration = useRef(0)
  const [cycle, setCycle] = useState('Monthly')
  const [periodStart, setPeriodStart] = useState(() => {
    const today = new Date()
    return `${today.getFullYear()}-${String(today.getMonth() + 1).padStart(2, '0')}-${String(today.getDate()).padStart(2, '0')}`
  })
  const [reason, setReason] = useState('')
  const [preview, setPreview] = useState<BillingPreview | null>(null)
  const [requestKey, setRequestKey] = useState('')
  const [page, setPage] = useState(1)
  const [paymentPage, setPaymentPage] = useState(1)
  const [reconciliationPage, setReconciliationPage] = useState(1)
  const [reload, setReload] = useState(0)
  const [records, setRecords] = useState<BillingPage | null>(null)
  const [payments, setPayments] = useState<PaymentPage | null>(null)
  const [reconciliation, setReconciliation] = useState<ReconciliationPage | null>(null)
  const [reconcileRequestId, setReconcileRequestId] = useState<number | null>(null)
  const [reconcileOrderId, setReconcileOrderId] = useState('')
  const [reconcileReason, setReconcileReason] = useState('')
  const [selectedDraftId, setSelectedDraftId] = useState<number | null>(null)
  const [paymentReason, setPaymentReason] = useState('')
  const [paymentKey, setPaymentKey] = useState('')
  const [loading, setLoading] = useState(true)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  const [notice, setNotice] = useState('')

  useEffect(() => {
    // A business switch must discard unsubmitted terms from the previous tenant.
    previewGeneration.current++
    setPreview(null)
    setRequestKey('')
    setReason('')
    setSelectedDraftId(null)
    setPaymentReason('')
    setPaymentKey('')
    setBusy(false)
    setError('')
    setNotice('')
  }, [businessId])

  useEffect(() => {
    let active = true
    setLoading(true)
    Promise.all([
      api<BillingPage>(`/platform/billing/drafts?page=${page}`),
      api<PaymentPage>(`/platform/billing/payment-requests?page=${paymentPage}`),
      api<ReconciliationPage>(`/platform/billing/reconciliation?page=${reconciliationPage}`)
    ])
      .then(([drafts, requests, summary]) => {
        if (active) {
          setRecords(drafts)
          setPayments(requests)
          setReconciliation(summary)
        }
      })
      .catch(cause => {
        if (!active || cause instanceof SessionExpiredError) return
        setRecords(null)
        setPayments(null)
        setReconciliation(null)
        setError('Unable to load billing records.')
      })
      .finally(() => { if (active) setLoading(false) })
    return () => { active = false }
  }, [page, paymentPage, reconciliationPage, reload])

  function clearPreview() {
    previewGeneration.current++
    setPreview(null)
    setRequestKey('')
    setNotice('')
  }

  async function loadPreview(event: FormEvent) {
    event.preventDefault()
    if (!businessId) return
    setError('')
    setNotice('')
    setPreview(null)
    if (!/^\d{4}-\d{2}-\d{2}$/.test(periodStart)) {
      setError('Choose a valid billing period start date.')
      return
    }
    const expectedBusinessId = businessId
    const generation = ++previewGeneration.current
    setBusy(true)
    try {
      const result = await api<BillingPreview>(
        `/platform/billing/businesses/${Number(expectedBusinessId)}/preview?cycle=${cycle}&periodStartDate=${periodStart}`)
      if (generation !== previewGeneration.current ||
          businessIdRef.current !== expectedBusinessId) return
      setPreview(result)
      setRequestKey(operationKey())
    } catch (cause) {
      if (generation === previewGeneration.current &&
          businessIdRef.current === expectedBusinessId && !(cause instanceof SessionExpiredError))
        setError(cause instanceof Error ? cause.message : 'Unable to preview billing terms.')
    } finally {
      if (generation === previewGeneration.current &&
          businessIdRef.current === expectedBusinessId) setBusy(false)
    }
  }

  async function createDraft(event: FormEvent) {
    event.preventDefault()
    if (!preview || !requestKey) return
    setBusy(true)
    setError('')
    try {
      const result = await post<{ id: number; alreadyExists: boolean }>('/platform/billing/drafts', {
        businessId: preview.businessId,
        expectedPlanVersionId: preview.publishedPlanVersionId,
        billingCycle: preview.billingCycle,
        periodStartDate: preview.periodStartDate,
        requestKey,
        reason: reason.trim()
      })
      setNotice(`Billing draft #${result.id} ${result.alreadyExists ? 'already exists' : 'created'}; no payment or invoice posted.`)
      setPreview(null)
      setRequestKey('')
      setReason('')
      setReload(value => value + 1)
      setPage(1)
    } catch (cause) {
      if (!(cause instanceof SessionExpiredError))
        setError(cause instanceof Error ? cause.message : 'Unable to create billing draft.')
    } finally {
      setBusy(false)
    }
  }

  function selectDraft(id: number) {
    setSelectedDraftId(id)
    setPaymentReason('')
    setPaymentKey(operationKey())
    setError('')
    setNotice('')
  }

  async function preparePayment(event: FormEvent) {
    event.preventDefault()
    if (!selectedDraftId || !paymentKey) return
    setBusy(true)
    setError('')
    try {
      const result = await post<{ id: number; alreadyExists: boolean }>(
        `/platform/billing/drafts/${selectedDraftId}/payment-requests`, {
          requestKey: paymentKey,
          reason: paymentReason.trim()
        })
      setNotice(`Payment request #${result.id} ${result.alreadyExists ? 'already exists' : 'prepared'}. No money has been collected.`)
      setSelectedDraftId(null)
      setPaymentKey('')
      setPaymentReason('')
      setReload(value => value + 1)
    } catch (cause) {
      if (!(cause instanceof SessionExpiredError))
        setError(cause instanceof Error ? cause.message : 'Unable to prepare payment request.')
    } finally {
      setBusy(false)
    }
  }

  async function reconcileOrder(event: FormEvent) {
    event.preventDefault()
    if (!reconcileRequestId || !reconcileOrderId.trim()) return
    setBusy(true)
    setError('')
    try {
      await post(`/platform/billing/payment-requests/${reconcileRequestId}/reconcile-order`, {
        orderId: reconcileOrderId.trim(),
        reason: reconcileReason.trim()
      })
      setNotice(`Order verified and linked to request #${reconcileRequestId}. No money was collected by this action.`)
      setReconcileRequestId(null)
      setReconcileOrderId('')
      setReconcileReason('')
      setReload(value => value + 1)
    } catch (cause) {
      if (!(cause instanceof SessionExpiredError))
        setError(cause instanceof Error ? cause.message : 'Unable to reconcile order.')
    } finally {
      setBusy(false)
    }
  }

  return <>
    <section className="content-section">
      <h2>Create Billing Draft</h2>
      <p className="text-secondary small">
        Prepare an internal price snapshot from a published plan. This is not a tax invoice,
        payable demand, or verified payment. It does not activate subscriptions.
      </p>
      {notice && <div className="alert alert-success" role="status">{notice}</div>}
      {error && <div className="alert alert-danger" role="alert">{error}</div>}
      <form onSubmit={event => void loadPreview(event)}>
        <div className="row g-3 align-items-end">
          <div className="col-12 col-sm-4">
            <p className="mb-1">{business?.name ?? 'Select a business to prepare billing.'}</p>
            <button className="btn btn-outline-secondary" type="button" onClick={onChooseBusiness}>
              {business ? 'Change business' : 'Find business by name'}</button>
          </div>
          <div className="col-12 col-sm-3">
            <label className="form-label" htmlFor="billing-cycle">Billing cycle</label>
            <select id="billing-cycle" className="form-select" value={cycle}
              onChange={event => { setCycle(event.target.value); clearPreview() }}>
              <option value="Monthly">Monthly</option>
              <option value="Quarterly">Quarterly</option>
              <option value="Yearly">Yearly</option>
            </select>
          </div>
          <div className="col-12 col-sm-3">
            <label className="form-label" htmlFor="billing-period">Period starts</label>
            <input id="billing-period" className="form-control" type="date" required
              value={periodStart} onChange={event => {
                setPeriodStart(event.target.value)
                clearPreview()
              }} />
          </div>
          <div className="col-12 col-sm-2">
            <button className="btn btn-outline-primary w-100" type="submit" disabled={busy || !businessId}>
              {busy ? 'Loading…' : 'Preview Terms'}
            </button>
          </div>
        </div>
      </form>
      {preview && <section className="border rounded p-3 mt-3" aria-label="Billing draft preview">
        <h3 className="h6">Review: {preview.businessName} (#{preview.businessId})</h3>
        <dl className="detail-list">
          <div><dt>Plan</dt><dd>{preview.planCode}, published v{preview.version}</dd></div>
          <div><dt>Cycle</dt><dd>{preview.billingCycle}</dd></div>
          <div><dt>Period</dt><dd>{preview.periodStartDate} – {preview.periodEndDate} (end exclusive)</dd></div>
          <div><dt>Price snapshot</dt><dd>{money(preview.amount, preview.currencyCode)}</dd></div>
          <div><dt>Payment</dt><dd>Not collected</dd></div>
        </dl>
        <form onSubmit={event => void createDraft(event)}>
          <label htmlFor="billing-reason" className="form-label">Reason</label>
          <textarea id="billing-reason" className="form-control" rows={2}
            minLength={10} maxLength={200} required value={reason}
            onChange={event => setReason(event.target.value)}
            placeholder="Why is this internal billing draft being prepared?" />
          <div className="form-footer">
            <button className="btn btn-outline-secondary" type="button" disabled={busy}
              onClick={() => { clearPreview(); setError('') }}>Cancel</button>
            <button className="btn btn-primary" type="submit"
              disabled={busy || reason.trim().length < 10}>
              {busy ? 'Saving…' : 'Confirm Draft'}
            </button>
          </div>
        </form>
      </section>}
    </section>

    <section className="content-section mt-3">
      <div className="d-flex align-items-center justify-content-between gap-2 flex-wrap mb-3">
        <h2 className="mb-0">Billing Draft History</h2>
        <button type="button" className="btn btn-outline-secondary btn-sm"
          disabled={loading || busy} onClick={() => setReload(value => value + 1)}>
          Refresh
        </button>
      </div>
      {loading ? <p role="status" className="text-secondary">Loading billing drafts…</p>
        : records?.total === 0 ? <p className="text-secondary">
            No billing drafts have been created.
          </p>
          : records && <>
            <div className="table-responsive">
              <table className="table platform-table align-middle">
                <thead><tr><th>Draft ID</th><th>Business</th><th>Plan / Cycle</th>
                  <th>Period</th><th>Amount</th><th>Created</th><th>Payment status</th><th>Action</th></tr></thead>
                <tbody>{records.items.map(item => <tr key={item.id}>
                  <td>#{item.id}</td>
                  <td><strong>{item.businessName}</strong>
                    <small className="d-block text-secondary">#{item.businessId}</small></td>
                  <td>{item.planCode} · {item.billingCycle}</td>
                  <td>{item.periodStartDate && item.periodEndDate
                    ? `${item.periodStartDate} – ${item.periodEndDate} (exclusive)`
                    : 'Not recorded (older draft)'}</td>
                  <td>{money(item.amount, item.currencyCode)}</td>
                  <td>{new Date(item.createdAtUtc).toLocaleString()}</td>
                  <td><span className="text-secondary">{paymentStatusLabel(item.paymentStatus)}</span>
                    {item.paymentRequestId !== null &&
                      <small className="d-block text-secondary">Request #{item.paymentRequestId}</small>}
                  </td>
                  <td>
                    {item.paymentRequestId !== null
                      ? <span className="text-secondary small">Already prepared</span>
                      : <button className="btn btn-outline-primary btn-sm" type="button"
                          disabled={busy || !item.periodStartDate || !item.periodEndDate}
                          onClick={() => selectDraft(item.id)}>Prepare Payment</button>}
                  </td>
                </tr>)}</tbody>
              </table>
            </div>
            <div className="d-flex justify-content-between align-items-center mt-3 gap-2">
              <span className="small text-secondary">{records.total} drafts · Page {page}</span>
              {(page > 1 || page * records.pageSize < records.total) && <div className="d-flex gap-2">
                <button className="btn btn-outline-secondary btn-sm" type="button"
                  disabled={page === 1} onClick={() => setPage(p => p - 1)}>Previous</button>
                <button className="btn btn-outline-secondary btn-sm" type="button"
                  disabled={page * records.pageSize >= records.total}
                  onClick={() => setPage(p => p + 1)}>Next</button>
              </div>}
            </div>
          </>}
    </section>
    {selectedDraftId !== null && <section className="content-section mt-3">
      <h2>Prepare Payment Request for Draft #{selectedDraftId}</h2>
      <p className="text-secondary small">
        Internal unpaid request only. No gateway order, tax invoice or activation.
      </p>
      <form onSubmit={event => void preparePayment(event)}>
        <label className="form-label" htmlFor="payment-preparation-reason">Reason</label>
        <textarea id="payment-preparation-reason" className="form-control" rows={2}
          minLength={10} maxLength={200} required value={paymentReason}
          onChange={event => setPaymentReason(event.target.value)} />
        <div className="form-footer">
          <button type="button" className="btn btn-outline-secondary" disabled={busy}
            onClick={() => setSelectedDraftId(null)}>Cancel</button>
          <button className="btn btn-primary" disabled={busy || paymentReason.trim().length < 10}>
            {busy ? 'Preparing…' : 'Confirm Preparation'}
          </button>
        </div>
      </form>
    </section>}
    <section className="content-section mt-3">
      <h2>Payment Requests</h2>
      {payments?.total === 0 ? <p className="text-secondary">No payment requests prepared.</p>
        : payments && <div className="table-responsive">
          <table className="table platform-table align-middle">
            <thead><tr><th>Request ID</th><th>Draft</th><th>Business</th>
              <th>Amount</th><th>Status</th></tr></thead>
            <tbody>{payments.items.map(item => <tr key={item.id}>
              <td>#{item.id}</td>
              <td>#{item.billingDraftId}</td>
              <td>{item.businessName} (#{item.businessId})</td>
              <td>{money(item.amount, item.currencyCode)}</td>
              <td>{item.status}</td>
            </tr>)}</tbody>
          </table>
        </div>}
      {payments && (paymentPage > 1 || paymentPage * payments.pageSize < payments.total) && <div className="d-flex justify-content-between align-items-center gap-2 mt-3">
        <span className="small text-secondary">{payments.total} payment requests · Page {paymentPage}</span>
        <div className="d-flex gap-2">
          <button className="btn btn-outline-secondary btn-sm" type="button"
            disabled={busy || paymentPage === 1}
            onClick={() => setPaymentPage(n => n - 1)}>Previous requests</button>
          <button className="btn btn-outline-secondary btn-sm" type="button"
            disabled={busy || paymentPage * payments.pageSize >= payments.total}
            onClick={() => setPaymentPage(n => n + 1)}>Next requests</button>
        </div>
      </div>}
    </section>
    <section className="content-section mt-3">
      <div className="d-flex justify-content-between align-items-center gap-2 mb-3">
        <h2 className="mb-0">Payment Reconciliation</h2>
        <button className="btn btn-outline-secondary btn-sm" type="button"
          disabled={busy || loading} onClick={() => setReload(value => value + 1)}>Refresh</button>
      </div>
      <p className="small text-secondary">
        Recorded captured payments, processed refunds and remaining amounts. These totals
        are not settlement, revenue, or tax invoice totals.
      </p>
      {reconciliation?.items.length === 0
        ? <p className="text-secondary">No verified captures recorded.</p>
        : reconciliation && <div className="table-responsive">
            <table className="table platform-table align-middle">
              <thead><tr><th>Payment</th><th>Business</th><th>Captured</th>
                <th>Refunded</th><th>Remaining</th><th>Review</th></tr></thead>
              <tbody>{reconciliation.items.map(item => <tr key={item.id}>
                <td>#{item.id}<small className="d-block text-secondary">{item.providerPaymentId}</small></td>
                <td>{item.businessName}<small className="d-block text-secondary">#{item.businessId}</small></td>
                <td>{money(item.amount, item.currencyCode)}</td>
                <td>{money(item.refundedAmount, item.currencyCode)}</td>
                <td>{money(item.netAmount, item.currencyCode)}</td>
                <td>{item.activationStatus}</td>
              </tr>)}</tbody>
            </table>
          </div>}
      {reconciliation && <p className="small text-secondary">
        Showing {reconciliation.items.length} of {reconciliation.total} captured payments · Page {reconciliationPage}.
      </p>}
      {reconciliation && (reconciliationPage > 1 || reconciliationPage * reconciliation.pageSize < reconciliation.total) && <div className="d-flex gap-2 mt-2">
        <button className="btn btn-outline-secondary btn-sm" type="button"
          disabled={busy || reconciliationPage === 1}
          onClick={() => setReconciliationPage(n => n - 1)}>Previous captures</button>
        <button className="btn btn-outline-secondary btn-sm" type="button"
          disabled={busy || reconciliationPage * reconciliation.pageSize >= reconciliation.total}
          onClick={() => setReconciliationPage(n => n + 1)}>Next captures</button>
      </div>}
      <h3 className="h6 mt-4">Orders requiring reconciliation</h3>
      {reconciliation?.unresolvedOrders.length === 0
        ? <p className="small text-secondary">No uncertain gateway orders.</p>
        : reconciliation?.unresolvedOrders.map(item => <div key={item.id}
            className="d-flex justify-content-between align-items-center gap-2 border-bottom py-2">
            <span><strong>Request #{item.id}</strong> · {item.businessName} · {item.status}</span>
            <button type="button" className="btn btn-outline-primary btn-sm" disabled={busy}
              onClick={() => {
                setReconcileRequestId(item.id)
                setReconcileOrderId('')
                setReconcileReason('')
              }}>Verify Order</button>
          </div>)}
      {reconcileRequestId !== null && <form className="border rounded p-3 mt-3"
        onSubmit={event => void reconcileOrder(event)}>
        <h3 className="h6">Verify existing order for request #{reconcileRequestId}</h3>
        <p className="small text-secondary">
          Find the existing order in Razorpay by receipt bs_{reconcileRequestId}.
          This checks the provider; it never creates another order or captures money.
        </p>
        <label htmlFor="reconcile-order-id" className="form-label">Razorpay Order ID</label>
        <input id="reconcile-order-id" className="form-control" required
          placeholder="order_..." value={reconcileOrderId}
          onChange={event => setReconcileOrderId(event.target.value)} />
        <label htmlFor="reconcile-reason" className="form-label mt-2">Reason</label>
        <textarea id="reconcile-reason" className="form-control" required
          minLength={10} maxLength={200} rows={2} value={reconcileReason}
          onChange={event => setReconcileReason(event.target.value)} />
        <div className="form-footer">
          <button type="button" className="btn btn-outline-secondary" disabled={busy}
            onClick={() => setReconcileRequestId(null)}>Cancel</button>
          <button type="submit" className="btn btn-primary"
            disabled={busy || reconcileReason.trim().length < 10}>Verify with Razorpay</button>
        </div>
      </form>}
    </section>
    <p className="small text-secondary mt-3">
      Billing periods remain immutable. Gateway checkout requires configuration;
      refunds are only recorded after independent provider verification.
      Tax invoices and refund issuance are not enabled.
    </p>
  </>
}
