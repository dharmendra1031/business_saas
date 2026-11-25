import { useEffect, useState } from 'react'
import { api, SessionExpiredError, type RetailDashboard } from './api'
import CashReceipt, { type Receipt } from './CashReceipt'

interface Invoice {
  id: number; locationId: number; locationName?: string | null; customerId?: number | null;
  customerName?: string | null; total: number; currencyCode: string; postedAtUtc: string;
  paymentMethod: 'Cash' | 'Credit'; status: 'Posted' | 'Partial return' | 'Returned'; printAvailable: boolean
}
interface PostedSalesPage {
  items: Invoice[]; total: number; pageSize: number; hasMore: boolean
  totals: { currencyCode: string; total: number; cash: number; credit: number }[]
}
const defaults = { locationId: 0, search: '', fromDate: '', toDate: '', payment: 'All', status: 'All' }

export default function BusinessPostedSales({ dashboard, userId }: { dashboard: RetailDashboard; userId: number }) {
  const { business, locations } = dashboard
  const [filters, setFilters] = useState(defaults)
  const [applied, setApplied] = useState(defaults)
  const [page, setPage] = useState(1)
  const [reload, setReload] = useState(0)
  const [result, setResult] = useState<PostedSalesPage | null>(null)
  const [error, setError] = useState('')
  const [selected, setSelected] = useState<Invoice | null>(null)
  const [receipt, setReceipt] = useState<Receipt | null>(null)
  const [receiptError, setReceiptError] = useState('')
  const [receiptReload, setReceiptReload] = useState(0)
  const [returnNotice, setReturnNotice] = useState('')
  const invalidDate = !!filters.fromDate && !!filters.toDate && filters.fromDate > filters.toDate
  const base = `/businesses/${business.id}/cash-sales/business-receipts`
  const change = <K extends keyof typeof defaults>(field: K, value: (typeof defaults)[K]) =>
    setFilters(previous => ({ ...previous, [field]: value }))

  useEffect(() => {
    let active = true
    const controller = new AbortController()
    setResult(null); setError('')
    const query = new URLSearchParams({ locationId: String(applied.locationId), page: String(page),
      search: applied.search, payment: applied.payment, status: applied.status })
    if (applied.fromDate) query.set('fromDate', applied.fromDate)
    if (applied.toDate) query.set('toDate', applied.toDate)
    api<PostedSalesPage>(base + '?' + query, { signal: controller.signal })
      .then(data => { if (active) setResult(data) })
      .catch(cause => {
        if (active && !(cause instanceof SessionExpiredError))
          setError(cause instanceof Error ? cause.message : 'Unable to load invoices.')
      })
    return () => { active = false; controller.abort() }
  }, [base, applied, page, reload])

  useEffect(() => {
    if (!selected) return
    let active = true
    const path = selected.paymentMethod === 'Credit'
      ? `/businesses/${business.id}/credit-sales/business-invoices/${selected.id}` : base + '/' + selected.id
    api<Receipt>(path + '?locationId=' + selected.locationId)
      .then(value => { if (active) setReceipt(value) })
      .catch(cause => {
        if (active && !(cause instanceof SessionExpiredError)) setReceiptError('Unable to open this invoice.')
      })
    return () => { active = false }
  }, [base, business.id, selected, receiptReload])

  function closeReceipt() {
    setSelected(null); setReceipt(null); setReceiptError(''); setReturnNotice('')
    setReload(value => value + 1)
  }
  function refreshReceipt() {
    setReceipt(null); setReceiptError(''); setReceiptReload(value => value + 1)
  }
  if (receipt && selected) return <CashReceipt receipt={receipt} businessName={business.name}
    pdfPath={`/api${base}/${receipt.id}/pdf?locationId=${selected.locationId}`}
    timeZone={business.timeZoneId} backLabel="Back to receipts" onBack={closeReceipt} returnNotice={returnNotice}
    returnEstimate={selected.paymentMethod === 'Cash' && dashboard.roleCode === 'Owner' ? {
      path: base + '/' + receipt.id, locationId: selected.locationId, userId, canOperate: dashboard.canOperate,
      onRecorded: (id, total) => {
        setReturnNotice(`Return #${id} recorded: ${receipt.currencyCode} ${total.toFixed(2)}.`)
        refreshReceipt()
      }
    } : undefined} />
  if (selected) return <>
    {returnNotice && <p className="alert alert-success" role="status">{returnNotice}</p>}
    {receiptError ? <p className="alert alert-danger" role="alert">{receiptError}{' '}
      <button type="button" className="btn btn-outline-secondary btn-sm" onClick={refreshReceipt}>Retry</button></p> :
      <p role="status">Loading invoice…</p>}
    <button className="btn btn-outline-secondary" type="button" onClick={closeReceipt}>Back to receipts</button>
  </>

  return <>
    <form className="d-flex flex-wrap align-items-end gap-2 mb-3" onSubmit={event => {
      event.preventDefault()
      if (!invalidDate) { setApplied({ ...filters }); setPage(1); setReload(x => x + 1) }
    }}>
      <label>Location<select aria-label="Location" className="form-select form-select-sm" value={filters.locationId}
        onChange={event => change('locationId', Number(event.target.value))}>
        <option value={0}>All locations</option>
        {locations.map(location => <option key={location.id} value={location.id}>{location.name}</option>)}
      </select></label>
      <label>Invoice / customer<input aria-label="Invoice / customer" className="form-control form-control-sm"
        type="search" maxLength={100} placeholder="Number, name or phone" value={filters.search}
        onChange={event => change('search', event.target.value)} /></label>
      <label>From date<input className="form-control form-control-sm" type="date" value={filters.fromDate}
        min="1900-01-01" max="9998-12-31" onChange={event => change('fromDate', event.target.value)} /></label>
      <label>To date<input className="form-control form-control-sm" type="date" value={filters.toDate}
        min="1900-01-01" max="9998-12-31" onChange={event => change('toDate', event.target.value)} /></label>
      <label>Payment<select aria-label="Payment" className="form-select form-select-sm" value={filters.payment}
        onChange={event => change('payment', event.target.value)}>
        <option value="All">All methods</option><option>Cash</option><option>Credit</option>
      </select></label>
      <label>Status<select aria-label="Status" className="form-select form-select-sm" value={filters.status}
        onChange={event => change('status', event.target.value)}>
        <option value="All">All statuses</option><option>Posted</option><option>Partial return</option><option>Returned</option>
      </select></label>
      <button type="submit" className="btn btn-primary btn-sm" disabled={invalidDate}>Search</button>
      <button type="button" className="btn btn-outline-secondary btn-sm"
        onClick={() => { setFilters(defaults); setApplied({ ...defaults }); setPage(1); setReload(x => x + 1) }}>Clear filters</button>
    </form>
    {invalidDate && <p role="alert">From date must not be later than To date.</p>}
    {error ? <div className="alert alert-danger" role="alert">{error}{' '}
      <button type="button" className="btn btn-outline-secondary btn-sm" onClick={() => setReload(x => x + 1)}>Retry</button></div> :
      !result ? <p role="status">Loading invoices…</p> : <>
        {result.totals.length > 0 && <div className="d-flex flex-wrap gap-3 border-bottom pb-2 mb-2" aria-label="Filtered gross sales totals">
          {result.totals.map(total => <div key={total.currencyCode} className="d-flex flex-wrap gap-3">
            <span>Gross sales <strong>{total.currencyCode} {total.total.toFixed(2)}</strong></span>
            <span>Cash sales <strong>{total.currencyCode} {total.cash.toFixed(2)}</strong></span>
            <span>Credit sales <strong>{total.currencyCode} {total.credit.toFixed(2)}</strong></span>
          </div>)}
        </div>}
        {result.items.length === 0 ? <p>No matching invoices.</p> : <div className="table-responsive">
          <table className="table table-sm align-middle">
            <thead><tr>{['Invoice', 'Posted', 'Location', 'Customer', 'Payment', 'Status'].map(x => <th scope="col" key={x}>{x}</th>)}
              <th scope="col" className="text-end">Amount</th><th scope="col">Action</th></tr></thead>
            <tbody>{result.items.map(item => <tr key={item.id}>
              <td><button type="button" className="btn btn-link p-0" onClick={() => setSelected(item)}>Receipt #{item.id}</button></td>
              <td>{new Date(item.postedAtUtc).toLocaleString(undefined, { timeZone: business.timeZoneId })}</td>
              <td>{item.locationName || locations.find(x => x.id === item.locationId)?.name || '—'}</td>
              <td>{item.customerName || (item.customerId ? 'Customer #' + item.customerId : 'Walk-in')}</td>
              <td>{item.paymentMethod}</td><td>{item.status}</td>
              <td className="text-end text-nowrap">{item.currencyCode} {item.total.toFixed(2)}</td>
              <td><div className="d-flex flex-wrap gap-1">
                <button type="button" className="btn btn-sm btn-outline-secondary" aria-label={'View invoice ' + item.id} onClick={() => setSelected(item)}>View</button>
                {item.printAvailable && <a className="btn btn-sm btn-outline-secondary" aria-label={'Print receipt ' + item.id}
                  href={`/api${base}/${item.id}/pdf?locationId=${item.locationId}`} target="_blank" rel="noopener noreferrer">Print</a>}
              </div></td>
            </tr>)}</tbody>
          </table>
        </div>}
        <div className="d-flex flex-wrap gap-2 align-items-center">
          <button type="button" className="btn btn-outline-secondary btn-sm" disabled={page === 1}
            onClick={() => setPage(value => value - 1)}>Previous</button>
          <span>{result.items.length ? `Showing ${(page - 1) * result.pageSize + 1}–${(page - 1) * result.pageSize + result.items.length} of ${result.total}` : `0 shown · ${result.total} matching`}</span>
          <button type="button" className="btn btn-outline-secondary btn-sm" disabled={!result.hasMore}
            onClick={() => setPage(value => value + 1)}>Next</button>
        </div>
      </>}
  </>
}
