import { useEffect, useState, type FormEvent } from 'react'
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
}

interface BillingPage {
  items: BillingDraft[]
  total: number
  page: number
  pageSize: number
}

function money(amount: number, currency: string) {
  return new Intl.NumberFormat('en-IN', {
    style: 'currency',
    currency,
    minimumFractionDigits: 2
  }).format(amount)
}

export default function PlatformBilling() {
  const [businessId, setBusinessId] = useState('')
  const [cycle, setCycle] = useState('Monthly')
  const [periodStart, setPeriodStart] = useState(() => {
    const today = new Date()
    return `${today.getFullYear()}-${String(today.getMonth() + 1).padStart(2, '0')}-01`
  })
  const [reason, setReason] = useState('')
  const [preview, setPreview] = useState<BillingPreview | null>(null)
  const [requestKey, setRequestKey] = useState('')
  const [page, setPage] = useState(1)
  const [reload, setReload] = useState(0)
  const [records, setRecords] = useState<BillingPage | null>(null)
  const [loading, setLoading] = useState(true)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  const [notice, setNotice] = useState('')

  useEffect(() => {
    let active = true
    setLoading(true)
    api<BillingPage>(`/platform/billing/drafts?page=${page}`)
      .then(result => { if (active) setRecords(result) })
      .catch(cause => {
        if (!active || cause instanceof SessionExpiredError) return
        setRecords(null)
        setError('Unable to load billing drafts.')
      })
      .finally(() => { if (active) setLoading(false) })
    return () => { active = false }
  }, [page, reload])

  function clearPreview() {
    setPreview(null)
    setRequestKey('')
    setNotice('')
  }

  async function loadPreview(event: FormEvent) {
    event.preventDefault()
    setError('')
    setNotice('')
    setPreview(null)
    if (!/^\d{4}-\d{2}-01$/.test(periodStart)) {
      setError('The billing period must start on the first day of a month.')
      return
    }
    setBusy(true)
    try {
      const result = await api<BillingPreview>(
        `/platform/billing/businesses/${Number(businessId)}/preview?cycle=${cycle}&periodStartDate=${periodStart}`)
      setPreview(result)
      setRequestKey(operationKey())
    } catch (cause) {
      if (!(cause instanceof SessionExpiredError))
        setError(cause instanceof Error ? cause.message : 'Unable to preview billing terms.')
    } finally {
      setBusy(false)
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
            <label className="form-label" htmlFor="billing-business-id">Business ID</label>
            <input className="form-control" id="billing-business-id" type="number"
              min="1" step="1" required value={businessId}
              onChange={event => { setBusinessId(event.target.value); clearPreview() }}
              placeholder="Numeric business ID" />
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
            <button className="btn btn-outline-primary w-100" type="submit" disabled={busy}>
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
                  <th>Period</th><th>Amount</th><th>Created</th><th>Status</th></tr></thead>
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
                  <td><span className="text-secondary">{item.status} (unpaid)</span></td>
                </tr>)}</tbody>
              </table>
            </div>
            <div className="d-flex justify-content-between align-items-center mt-3 gap-2">
              <span className="small text-secondary">{records.total} drafts · Page {page}</span>
              <div className="d-flex gap-2">
                <button className="btn btn-outline-secondary btn-sm" type="button"
                  disabled={page === 1} onClick={() => setPage(p => p - 1)}>Previous</button>
                <button className="btn btn-outline-secondary btn-sm" type="button"
                  disabled={page * records.pageSize >= records.total}
                  onClick={() => setPage(p => p + 1)}>Next</button>
              </div>
            </div>
          </>}
    </section>
    <p className="small text-secondary mt-3">
      Billing periods start on the first day of a calendar month. Overlapping internal drafts are blocked.
      Payment gateway, tax invoices, payment ledger and paid activation will be implemented
      separately after payment and tax requirements are approved.
    </p>
  </>
}
