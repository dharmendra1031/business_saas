import { Fragment, useEffect, useState } from 'react'
import { MasterLookup } from './LookupDialog'
import { api, SessionExpiredError, type RetailDashboard } from './api'
import CashReceipt, { type Receipt } from './CashReceipt'

interface ReturnLine {
  id: number; productName: string; productSku?: string; unitCode?: string;
  quantity: number; amount: number; restock: boolean
}
interface PostedReturn {
  id: number; saleId: number; locationId: number; locationName?: string;
  customerId?: number; customerName?: string; currencyCode: string;
  total: number; reason: string; postedAtUtc: string; lines: ReturnLine[]
}
interface ReturnRegister {
  total: number; pageSize: number; hasMore: boolean; items: PostedReturn[]
  totals: { currencyCode: string; refundTotal: number; returnCount: number }[]
}
const defaults = { locationId: 0, search: '', fromDate: '', toDate: '' }

export default function BusinessReturns({ dashboard }: { dashboard: RetailDashboard }) {
  const { business, locations } = dashboard
  const [filters, setFilters] = useState(defaults)
  const [applied, setApplied] = useState(defaults)
  const [page, setPage] = useState(1)
  const [reload, setReload] = useState(0)
  const [result, setResult] = useState<ReturnRegister | null>(null)
  const [error, setError] = useState('')
  const [expanded, setExpanded] = useState<number | null>(null)
  const [selected, setSelected] = useState<PostedReturn | null>(null)
  const [receipt, setReceipt] = useState<Receipt | null>(null)
  const [receiptError, setReceiptError] = useState('')
  const [receiptReload, setReceiptReload] = useState(0)
  const base = `/businesses/${business.id}/cash-sales`
  const invalidDate = !!filters.fromDate && !!filters.toDate && filters.fromDate > filters.toDate
  const change = <K extends keyof typeof defaults>(field: K, value: (typeof defaults)[K]) =>
    setFilters(previous => ({ ...previous, [field]: value }))

  useEffect(() => {
    let active = true
    const controller = new AbortController()
    setResult(null); setError(''); setExpanded(null)
    const query = new URLSearchParams({ locationId: String(applied.locationId), search: applied.search, page: String(page) })
    if (applied.fromDate) query.set('fromDate', applied.fromDate)
    if (applied.toDate) query.set('toDate', applied.toDate)
    api<ReturnRegister>(base + '/business-returns?' + query, { signal: controller.signal })
      .then(value => { if (active) setResult(value) })
      .catch(cause => {
        if (active && !(cause instanceof SessionExpiredError))
          setError(cause instanceof Error ? cause.message : 'Unable to load posted returns.')
      })
    return () => { active = false; controller.abort() }
  }, [base, applied, page, reload])

  useEffect(() => {
    if (!selected) return
    let active = true
    setReceipt(null); setReceiptError('')
    api<Receipt>(base + '/business-receipts/' + selected.saleId + '?locationId=' + selected.locationId)
      .then(value => { if (active) setReceipt(value) })
      .catch(cause => { if (active && !(cause instanceof SessionExpiredError)) setReceiptError('Unable to open original invoice.') })
    return () => { active = false }
  }, [base, selected, receiptReload])

  const closeReceipt = () => { setSelected(null); setReceipt(null); setReceiptError('') }
  if (receipt && selected) return <CashReceipt receipt={receipt} businessName={business.name}
    timeZone={business.timeZoneId} pdfPath={`/api${base}/business-receipts/${receipt.id}/pdf?locationId=${selected.locationId}`}
    backLabel="Back to returns" onBack={closeReceipt} />
  if (selected) return <>
    {receiptError ? <p role="alert">{receiptError} <button type="button" className="btn btn-sm btn-outline-secondary"
      onClick={() => setReceiptReload(x => x + 1)}>Retry</button></p> : <p role="status">Loading original invoice…</p>}
    <button type="button" className="btn btn-outline-secondary" onClick={closeReceipt}>Back to returns</button>
  </>

  const money = (currency: string, amount: number) => currency + ' ' + amount.toFixed(2)
  return <>
    <form className="d-flex flex-wrap align-items-end gap-2 mb-3" onSubmit={event => {
      event.preventDefault()
      if (!invalidDate) { setApplied({ ...filters }); setPage(1); setReload(x => x + 1) }
    }}>
      <div><label className="form-label" htmlFor="returns-location">Location</label>
        <MasterLookup id="returns-location" title="Select location" value={filters.locationId} emptyLabel="All locations"
          options={[{ id: 0, name: 'All locations' }, ...locations]} onChange={id => change('locationId', id)} /></div>
      <label>Return / invoice / customer<input className="form-control form-control-sm" type="search" maxLength={100}
        placeholder="Number, name or phone" value={filters.search} onChange={event => change('search', event.target.value)} /></label>
      <label>From date<input className="form-control form-control-sm" type="date" min="1900-01-01" max="9998-12-31"
        value={filters.fromDate} onChange={event => change('fromDate', event.target.value)} /></label>
      <label>To date<input className="form-control form-control-sm" type="date" min="1900-01-01" max="9998-12-31"
        value={filters.toDate} onChange={event => change('toDate', event.target.value)} /></label>
      <button type="submit" className="btn btn-sm btn-primary" disabled={invalidDate}>Search</button>
      <button type="button" className="btn btn-sm btn-outline-secondary"
        onClick={() => { setFilters(defaults); setApplied({ ...defaults }); setPage(1); setReload(x => x + 1) }}>Clear filters</button>
    </form>
    {invalidDate && <p role="alert">From date must not be later than To date.</p>}
    {error ? <p role="alert">{error} <button type="button" className="btn btn-sm btn-outline-secondary"
      onClick={() => setReload(x => x + 1)}>Retry</button></p> : !result ? <p role="status">Loading posted returns…</p> : <>
      {result.totals.length > 0 && <div className="d-flex flex-wrap gap-3 border-bottom pb-2 mb-2">
        {result.totals.map(total => <span key={total.currencyCode}>Refunded <strong>{money(total.currencyCode, total.refundTotal)}</strong>
          {' · '}{total.returnCount} returns</span>)}
      </div>}
      {result.items.length === 0 ? <p>No matching posted returns.</p> : <div className="table-responsive">
        <table className="table table-sm align-middle">
          <thead><tr>{['Return', 'Posted', 'Original invoice', 'Location', 'Customer', 'Reason', 'Stock'].map(x => <th scope="col" key={x}>{x}</th>)}
            <th scope="col" className="text-end">Refund</th></tr></thead>
          <tbody>{result.items.map(item => <Fragment key={item.id}>
            <tr>
              <td><button className="btn btn-link p-0" type="button" aria-expanded={expanded === item.id}
                onClick={() => setExpanded(expanded === item.id ? null : item.id)}>Return #{item.id}</button></td>
              <td>{new Date(item.postedAtUtc).toLocaleString(undefined, { timeZone: business.timeZoneId })}</td>
              <td><button type="button" className="btn btn-link p-0" onClick={() => setSelected(item)}>Invoice #{item.saleId}</button></td>
              <td>{item.locationName || locations.find(x => x.id === item.locationId)?.name || '—'}</td>
              <td>{item.customerName || (item.customerId ? 'Customer #' + item.customerId : 'Walk-in')}</td>
              <td>{item.reason}</td>
              <td>{!item.lines.length ? '—' : item.lines.every(x => x.restock) ? 'Restocked' :
                item.lines.some(x => x.restock) ? 'Mixed' : 'Not restocked'}</td>
              <td className="text-end text-nowrap">{money(item.currencyCode, item.total)}</td>
            </tr>
            {expanded === item.id && <tr><td colSpan={8}>
              <table className="table table-sm mb-0" aria-label={'Return ' + item.id + ' lines'}>
                <thead><tr><th>Product / SKU</th><th className="text-end">Quantity</th><th>Stock</th><th className="text-end">Refund</th></tr></thead>
                <tbody>{item.lines.map(line => <tr key={line.id}>
                  <td>{line.productName}{line.productSku && <small className="d-block">{line.productSku}</small>}</td>
                  <td className="text-end">{line.quantity} {line.unitCode}</td><td>{line.restock ? 'Restocked' : 'Not restocked'}</td>
                  <td className="text-end">{money(item.currencyCode, line.amount)}</td>
                </tr>)}</tbody>
              </table>
            </td></tr>}
          </Fragment>)}</tbody>
        </table>
      </div>}
      <div className="d-flex flex-wrap gap-2 align-items-center">
        <button type="button" className="btn btn-sm btn-outline-secondary" disabled={page === 1} onClick={() => setPage(x => x - 1)}>Previous</button>
        <span>{result.items.length ? `Showing ${(page - 1) * result.pageSize + 1}–${(page - 1) * result.pageSize + result.items.length} of ${result.total}` : `0 shown · ${result.total} matching`}</span>
        <button type="button" className="btn btn-sm btn-outline-secondary" disabled={!result.hasMore} onClick={() => setPage(x => x + 1)}>Next</button>
      </div>
    </>}
  </>
}
