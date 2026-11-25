import { useEffect, useState } from 'react'
import { api, SessionExpiredError, type RetailDashboard } from './api'
import CashReceipt, { type Receipt } from './CashReceipt'

interface PostedSalesPage {
  items: { id: number; total: number; currencyCode: string; postedAtUtc: string }[]
  hasMore: boolean
}

export default function BusinessPostedSales({ dashboard, userId }: { dashboard: RetailDashboard; userId: number }) {
  const { business, locations } = dashboard
  const [locationId, setLocationId] = useState(locations[0]?.id ?? 0)
  const [search, setSearch] = useState('')
  const [receiptId, setReceiptId] = useState('')
  const [dateInput, setDateInput] = useState('')
  const [postedDate, setPostedDate] = useState('')
  const [page, setPage] = useState(1)
  const [reload, setReload] = useState(0)
  const [result, setResult] = useState<PostedSalesPage | null>(null)
  const [error, setError] = useState('')
  const [selectedId, setSelectedId] = useState<number | null>(null)
  const [receipt, setReceipt] = useState<Receipt | null>(null)
  const [receiptError, setReceiptError] = useState('')
  const [receiptReload, setReceiptReload] = useState(0)
  const [returnNotice, setReturnNotice] = useState('')
  const validSearch = search === '' || /^[1-9]\d{0,18}$/.test(search)

  useEffect(() => {
    if (!locationId) return
    let active = true
    setResult(null)
    setError('')
    const query = new URLSearchParams({ locationId: String(locationId), page: String(page) })
    if (receiptId) query.set('receiptId', receiptId)
    if (postedDate) query.set('postedDate', postedDate)
    api<PostedSalesPage>(`/businesses/${business.id}/cash-sales/business-receipts?${query}`)
      .then(data => { if (active) setResult(data) })
      .catch(cause => {
        if (active && !(cause instanceof SessionExpiredError))
          setError(cause instanceof Error ? cause.message : 'Unable to load posted receipts.')
      })
    return () => { active = false }
  }, [business.id, locationId, page, receiptId, postedDate, reload])

  useEffect(() => {
    if (selectedId === null) return
    let active = true
    const path = `/businesses/${business.id}/cash-sales/business-receipts/${selectedId}?locationId=${locationId}`
    api<Receipt>(path)
      .then(value => { if (active) setReceipt(value) })
      .catch(cause => {
        if (active && !(cause instanceof SessionExpiredError)) setReceiptError('Unable to open this receipt.')
      })
    return () => { active = false }
  }, [business.id, locationId, selectedId, receiptReload])

  function closeReceipt() {
    setSelectedId(null)
    setReceipt(null)
    setReceiptError('')
    setReturnNotice('')
  }

  function refreshReceipt() {
    setReceipt(null)
    setReceiptError('')
    setReceiptReload(value => value + 1)
  }

  if (receipt) return <CashReceipt receipt={receipt} businessName={business.name}
    pdfPath={`/api/businesses/${business.id}/cash-sales/business-receipts/${receipt.id}/pdf?locationId=${locationId}`}
    timeZone={business.timeZoneId} backLabel="Back to receipts" onBack={closeReceipt} returnNotice={returnNotice}
    returnEstimate={dashboard.roleCode === 'Owner' ? {
      path: `/businesses/${business.id}/cash-sales/business-receipts/${receipt.id}`, locationId,
      userId, canOperate: dashboard.canOperate,
      onRecorded: (id, total) => {
        setReturnNotice(`Return #${id} recorded: ${receipt.currencyCode} ${total.toFixed(2)}.`)
        refreshReceipt()
      }
    } : undefined} />
  if (selectedId !== null) return <>
    {returnNotice && <p className="alert alert-success" role="status">{returnNotice}</p>}
    {receiptError ? <p className="alert alert-danger" role="alert">{receiptError}{' '}
      <button type="button" className="btn btn-outline-secondary btn-sm" onClick={refreshReceipt}>Retry</button></p> :
      <p role="status">Loading receipt…</p>}
    <button className="btn btn-outline-secondary" type="button" onClick={closeReceipt}>Back to receipts</button>
  </>

  return <>
    <form className="platform-search mb-3" onSubmit={event => {
      event.preventDefault()
      if (validSearch) { setReceiptId(search); setPostedDate(dateInput); setPage(1) }
    }}>
      <label>Location<select className="form-select" value={locationId} onChange={event => {
        setLocationId(Number(event.target.value)); setPage(1)
      }}>
        {locations.map(location => <option key={location.id} value={location.id}>{location.name}</option>)}
      </select></label>
      <label>Receipt number<input className="form-control" inputMode="numeric" maxLength={19}
        type="search" value={search} onChange={event => setSearch(event.target.value)} /></label>
      <label>Posted date<input className="form-control" type="date" value={dateInput}
        min="1900-01-01" max="9998-12-31" onChange={event => setDateInput(event.target.value)} /></label>
      <button type="submit" className="btn btn-primary" disabled={!validSearch || !locationId}>Search</button>
      {(search || receiptId || dateInput || postedDate) && <button type="button" className="btn btn-outline-secondary"
        onClick={() => { setSearch(''); setReceiptId(''); setDateInput(''); setPostedDate(''); setPage(1) }}>Clear</button>}
    </form>
    {!locationId ? <p>No authorized locations.</p> :
      error ? <div className="alert alert-danger" role="alert">{error}{' '}
        <button type="button" className="btn btn-outline-secondary btn-sm"
          onClick={() => setReload(value => value + 1)}>Retry</button></div> :
      !result ? <p role="status">Loading posted receipts…</p> : <>
        {result.items.length === 0 ? <p>No matching cash receipts.</p> : <div className="table-responsive">
          <table className="table table-sm">
            <thead><tr><th scope="col">Receipt</th><th scope="col">Posted</th><th scope="col">Total</th></tr></thead>
            <tbody>{result.items.map(item => <tr key={item.id}>
              <td><button type="button" className="btn btn-link p-0"
                onClick={() => setSelectedId(item.id)}>Receipt #{item.id}</button></td>
              <td>{new Date(item.postedAtUtc).toLocaleString(undefined, { timeZone: business.timeZoneId })}</td>
              <td>{item.currencyCode} {item.total.toFixed(2)}</td>
            </tr>)}</tbody>
          </table>
        </div>}
        <div className="d-flex gap-2 align-items-center">
          <button type="button" className="btn btn-outline-secondary btn-sm" disabled={page === 1}
            onClick={() => setPage(value => value - 1)}>Previous</button>
          <span>Page {page}</span>
          <button type="button" className="btn btn-outline-secondary btn-sm" disabled={!result.hasMore}
            onClick={() => setPage(value => value + 1)}>Next</button>
        </div>
      </>}
  </>
}
