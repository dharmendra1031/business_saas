import { useEffect, useRef, useState } from 'react'
import { api, ApiResponseError, operationKey, post } from './api'

export interface Receipt {
  id: number
  total: number
  tendered: number
  currencyCode: string
  postedAtUtc: string
  printAvailable?: boolean
  businessName?: string | null
  locationName?: string | null
  customerId?: number | null
  customerName?: string | null
  timeZoneId?: string | null
  lines: { id: number; productName: string; unitPrice: number; quantity: number;
    productSku?: string | null; unitCode?: string | null; lineTotal?: number | null;
    returnedQuantity?: number | null; remainingQuantity?: number | null;
    returnedAmount?: number | null; remainingAmount?: number | null }[]
}

interface ReturnOptions { path: string; locationId: number; userId: number; canOperate: boolean }
interface ReturnRequest {
  locationId: number; requestKey: string; reason: string; expectedRefund: number;
  cashRefundConfirmed: boolean; lines: { lineId: number; quantity: number; restock: boolean }[]
}
interface PostedReturn {
  id: number; saleId: number; requestKey: string; total: number; reason: string; postedAtUtc: string;
  lines: { saleLineId: number; quantity: number; amount: number; restock: boolean }[]
}
interface ReturnPage { items: PostedReturn[]; hasMore: boolean }
interface Estimate { total: number; currencyCode: string; canPost: boolean; lines: { lineId: number; amount: number }[] }

function returnStorageKey(options: ReturnOptions) {
  return `cash-return:${options.userId}:${options.locationId}:${options.path}`
}

function readPending(key: string): { request: ReturnRequest | null; error: string } {
  try {
    const raw = sessionStorage.getItem(key)
    if (!raw) return { request: null, error: '' }
    const request = JSON.parse(raw) as ReturnRequest
    if (!request.requestKey || !Array.isArray(request.lines) || !request.lines.length ||
      !request.cashRefundConfirmed || !Number.isFinite(request.expectedRefund))
      throw new Error('Invalid pending return')
    return { request, error: '' }
  } catch {
    return { request: null, error: 'Pending return storage is unavailable. Check return history before recording another refund.' }
  }
}

function ReturnHistory({ receipt, options, refresh, timeZone }: {
  receipt: Receipt; options: ReturnOptions; refresh: number; timeZone: string
}) {
  const [page, setPage] = useState(1)
  const [result, setResult] = useState<ReturnPage | null>(null)
  const [error, setError] = useState('')
  const [reload, setReload] = useState(0)
  const [expanded, setExpanded] = useState<number | null>(null)
  useEffect(() => {
    let active = true
    setResult(null); setError(''); setExpanded(null)
    api<ReturnPage>(options.path + '/returns?' + new URLSearchParams({
      locationId: String(options.locationId), page: String(page)
    })).then(value => { if (active) setResult(value) })
      .catch(() => { if (active) setError('Unable to load return history.') })
    return () => { active = false }
  }, [options.path, options.locationId, page, refresh, reload])
  const money = (value: number) => receipt.currencyCode + ' ' + value.toFixed(2)
  return <section className="mt-3" aria-label="Return history">
    <h3 className="h6">Return history</h3>
    {error ? <p role="alert">{error} <button className="btn btn-sm btn-outline-secondary" type="button"
      onClick={() => setReload(value => value + 1)}>Retry</button></p> :
      !result ? <p role="status">Loading returns…</p> :
      !result.items.length ? <p className="small">No posted returns.</p> : <>
        <div className="table-responsive"><table className="table table-sm align-middle">
          <thead><tr><th scope="col">Return</th><th scope="col">Posted</th><th scope="col">Reason</th><th scope="col" className="text-end">Refund</th></tr></thead>
          <tbody>{result.items.map(item => <tr key={item.id}>
            <td><button type="button" className="btn btn-link p-0" aria-expanded={expanded === item.id}
              onClick={() => setExpanded(expanded === item.id ? null : item.id)}>Return #{item.id}</button>
              {expanded === item.id && <div className="small mt-1">{item.lines.map(line => <div key={line.saleLineId}>
                {receipt.lines.find(x => x.id === line.saleLineId)?.productName ?? 'Line #' + line.saleLineId}: {line.quantity} · {money(line.amount)} · {line.restock ? 'Restocked' : 'Not restocked'}
              </div>)}</div>}</td>
            <td>{new Date(item.postedAtUtc).toLocaleString(undefined, { timeZone })}</td>
            <td>{item.reason}</td><td className="text-end">{money(item.total)}</td>
          </tr>)}</tbody>
        </table></div>
        {(page > 1 || result.hasMore) && <div className="d-flex gap-2 align-items-center">
          <button type="button" className="btn btn-outline-secondary btn-sm" disabled={page === 1} onClick={() => setPage(value => value - 1)}>Previous</button>
          <span>Page {page}</span><button type="button" className="btn btn-outline-secondary btn-sm" disabled={!result.hasMore} onClick={() => setPage(value => value + 1)}>Next</button>
        </div>}
      </>}
  </section>
}

function ReturnEstimate({ receipt, options, onRecorded }: {
  receipt: Receipt; options: ReturnOptions; onRecorded: (record: PostedReturn) => void
}) {
  const key = returnStorageKey(options)
  const [restored] = useState(() => readPending(key))
  const [pending, setPending] = useState(restored.request)
  const [quantities, setQuantities] = useState<Record<number, string>>({})
  const [dispositions, setDispositions] = useState<Record<number, string>>({})
  const [estimate, setEstimate] = useState<Estimate | null>(null)
  const [reason, setReason] = useState('')
  const [confirmed, setConfirmed] = useState(false)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState(restored.error)
  const lock = useRef(false)
  const available = (line: Receipt['lines'][number]) => Math.max(0, line.remainingQuantity ?? 0)
  const selected = receipt.lines.filter(line => (quantities[line.id] ?? '') !== '' && Number(quantities[line.id]) !== 0)
  const valid = selected.length > 0 && selected.every(line => {
    const value = Number(quantities[line.id])
    return Number.isFinite(value) && value > 0 && value <= available(line) &&
      Math.abs(Math.round(value * 1000) - value * 1000) < 0.000001
  })
  function change(next: Record<number, string>) {
    setQuantities(next); setEstimate(null); setConfirmed(false); setError('')
  }
  function finish(record: PostedReturn, request: ReturnRequest) {
    if (!Number.isSafeInteger(record.id) || record.id <= 0 || record.saleId !== receipt.id ||
      record.requestKey !== request.requestKey || record.total !== request.expectedRefund)
      throw new Error('Return response could not be verified.')
    sessionStorage.removeItem(key)
    setPending(null)
    onRecorded(record)
  }
  async function recordReturn(request: ReturnRequest, checkOnly = false) {
    if (lock.current) return
    lock.current = true; setBusy(true); setError('')
    try {
      if (checkOnly) {
        const result = await api<ReturnPage>(options.path + '/returns?' + new URLSearchParams({
          locationId: String(options.locationId), requestKey: request.requestKey
        }))
        if (!result.items.length) {
          setError('No matching posted return found yet. Do not pay cash again. Retry recording with the same request.')
          return
        }
        finish(result.items[0], request)
      } else {
        // Persist before sending so a lost response or reload cannot generate a second key.
        sessionStorage.setItem(key, JSON.stringify(request))
        setPending(request)
        finish(await post<PostedReturn>(options.path + '/returns', request), request)
      }
    } catch (cause) {
      if (cause instanceof ApiResponseError && cause.code === 'RETURN_REJECTED') {
        try { sessionStorage.removeItem(key); setPending(null); setEstimate(null); setConfirmed(false) } catch { /* Keep recovery available. */ }
        setError(cause.message + ' Reconcile cash already refunded before making any new return.')
      } else {
        setError((cause instanceof Error ? cause.message : 'Unable to verify return.') +
          ' Check return history or retry the same recording. Do not refund cash again.')
      }
    } finally { lock.current = false; setBusy(false) }
  }
  if (pending) return <section className="border rounded p-2 mb-3" aria-label="Return recovery">
    <h3 className="h6">Verify previous return</h3>
    <p>Refund amount: <strong>{receipt.currencyCode} {pending.expectedRefund.toFixed(2)}</strong>. Do not refund cash again.</p>
    {error && <p role="alert" className="text-danger">{error}</p>}
    <div className="d-flex flex-wrap gap-2">
      <button type="button" className="btn btn-outline-secondary" disabled={busy} onClick={() => void recordReturn(pending, true)}>Check posted return</button>
      <button type="button" className="btn btn-primary" disabled={busy || !options.canOperate} onClick={() => void recordReturn(pending)}>Retry recording</button>
    </div>
  </section>
  const canRecord = !!estimate?.canPost && options.canOperate && valid && !busy && !restored.error &&
    reason.trim().length >= 5 && confirmed && selected.every(line => !!dispositions[line.id])
  return <section className="border rounded p-2 mb-3" aria-label="Return estimate">
    <h3 className="h6">Return estimate</h3>
    <p className="small mb-2">Calculate the amount before confirming a cash refund. No money is transferred by the software.</p>
    <button type="button" className="btn btn-outline-secondary btn-sm mb-2" disabled={busy || !receipt.lines.some(line => available(line) > 0)}
      onClick={() => change(Object.fromEntries(receipt.lines.map(line => [line.id, String(available(line))])))}>
      Select remaining quantity
    </button>
    <form onSubmit={async event => {
      event.preventDefault()
      if (!valid || busy || lock.current) return
      lock.current = true; setBusy(true); setError(''); setEstimate(null); setConfirmed(false)
      try {
        setEstimate(await post<Estimate>(options.path + '/return-estimate', { locationId: options.locationId,
          lines: selected.map(line => ({ lineId: line.id, quantity: Number(quantities[line.id]) })) }))
      } catch (cause) {
        setError(cause instanceof Error ? cause.message : 'Unable to calculate return estimate.')
      } finally { lock.current = false; setBusy(false) }
    }}>
      <div className="table-responsive"><table className="table table-sm align-middle">
        <thead><tr><th scope="col">Product</th><th scope="col">Return quantity</th><th scope="col" className="text-end">Estimated amount</th></tr></thead>
        <tbody>{receipt.lines.map(line => <tr key={line.id}>
          <td>{line.productName}
            <small className="d-block">Sold: {line.quantity} · Returned: {line.returnedQuantity ?? '—'} · Remaining: {line.remainingQuantity ?? '—'} {line.unitCode}</small></td>
          <td><input type="number" className="form-control form-control-sm" min="0" max={available(line)} step="0.001"
            aria-label={`Return quantity for ${line.productName}`} disabled={busy || available(line) === 0} value={quantities[line.id] ?? ''}
            onChange={event => change({ ...quantities, [line.id]: event.target.value })} /></td>
          <td className="text-end">{estimate?.lines.some(item => item.lineId === line.id)
            ? estimate.currencyCode + ' ' + estimate.lines.find(item => item.lineId === line.id)!.amount.toFixed(2) : '—'}</td>
        </tr>)}</tbody>
      </table></div>
      {error && <p role="alert" className="text-danger">{error}</p>}
      {estimate && <p role="status"><strong>Estimated return amount: {estimate.currencyCode} {estimate.total.toFixed(2)}</strong></p>}
      <button type="submit" className="btn btn-outline-primary btn-sm" disabled={!valid || busy}>
        {busy ? 'Calculating…' : 'Calculate estimate'}
      </button>
    </form>
    {estimate && !estimate.canPost && <p className="small mt-2 mb-0">Preview only. Return posting is not enabled.</p>}
    {estimate?.canPost && options.canOperate && <div className="border-top mt-3 pt-2">
      {selected.map(line => <label key={line.id} className="d-block mb-2">Stock disposition — {line.productName}
        <select className="form-select form-select-sm" disabled={busy} value={dispositions[line.id] ?? ''}
          onChange={event => { setDispositions({ ...dispositions, [line.id]: event.target.value }); setConfirmed(false) }}>
          <option value="">Choose condition</option><option value="restock">Sellable — restore stock</option>
          <option value="no-stock">Damaged / not sellable — do not restore</option>
        </select></label>)}
      <label className="d-block mb-2">Return reason
        <input className="form-control" maxLength={200} disabled={busy} value={reason}
          onChange={event => { setReason(event.target.value); setConfirmed(false) }} />
      </label>
      <label className="form-check mb-2"><input className="form-check-input" type="checkbox" checked={confirmed} disabled={busy}
        onChange={event => setConfirmed(event.target.checked)} />
        I confirm the stated cash refund has already been settled with the customer.
      </label>
      <button className="btn btn-primary" type="button" disabled={!canRecord}
        onClick={() => {
          if (!canRecord || !estimate) return
          void recordReturn({ locationId: options.locationId, requestKey: operationKey(), reason: reason.trim(),
            expectedRefund: estimate.total, cashRefundConfirmed: true, lines: selected.map(line => ({
              lineId: line.id, quantity: Number(quantities[line.id]), restock: dispositions[line.id] === 'restock'
            })) })
        }}>Record return</button>
    </div>}
  </section>
}

export default function CashReceipt({ receipt, businessName, timeZone, pdfPath, printNotice, backLabel, onBack, returnEstimate }: {
  receipt: Receipt
  businessName: string
  timeZone: string
  pdfPath: string
  printNotice?: string
  backLabel: string
  onBack: () => void
  returnEstimate?: ReturnOptions
}) {
  const [showEstimate, setShowEstimate] = useState(() => !!returnEstimate && !!readPending(returnStorageKey(returnEstimate)).request)
  const [recorded, setRecorded] = useState<PostedReturn | null>(null)
  const money = (amount: number) => receipt.currencyCode + ' ' + amount.toFixed(2)
  return <section className="content-section" aria-label="Cash receipt">
    <h2>Cash receipt #{receipt.id}</h2>
    <p>{receipt.businessName ?? businessName} · {new Date(receipt.postedAtUtc).toLocaleString(undefined, { timeZone: receipt.timeZoneId ?? timeZone })}</p>
    <p className="small mb-2">
      {receipt.locationName && <>Location: <strong>{receipt.locationName}</strong> · </>}
      Customer: <strong>{receipt.customerName || (receipt.customerId ? `Customer #${receipt.customerId}` : 'Walk-in')}</strong>
    </p>
    <div className="table-responsive">
      <table className="table table-sm align-middle" aria-label="Original posted sale lines">
        <thead><tr>
          <th scope="col">Product / SKU</th>
          <th scope="col" className="text-end">Sold quantity</th>
          <th scope="col" className="text-end">Rate</th>
          <th scope="col" className="text-end">Posted amount</th>
        </tr></thead>
        <tbody>{receipt.lines.map(line => <tr key={line.id}>
          <td><strong>{line.productName}</strong>
            {line.productSku && <small className="d-block">{line.productSku}</small>}</td>
          <td className="text-end">{line.quantity}{line.unitCode ? ' ' + line.unitCode : ''}</td>
          <td className="text-end">{money(line.unitPrice)}</td>
          <td className="text-end">{line.lineTotal == null ? '—' : money(line.lineTotal)}</td>
        </tr>)}</tbody>
      </table>
    </div>
    <p><strong>Total {money(receipt.total)}</strong><br />Cash received {money(receipt.tendered)}<br />
      Change {money(receipt.tendered - receipt.total)}</p>
    {printNotice && <p role="status" className="small text-body-secondary">{printNotice}</p>}
    {receipt.printAvailable
      ? <a className="btn btn-outline-secondary me-2" href={pdfPath} target="_blank" rel="noopener noreferrer">Print receipt</a>
      : <p className="small text-body-secondary" role="status">RDLC printing requires a Windows report host.</p>}
    {returnEstimate && returnEstimate.canOperate && !recorded && <button type="button" className="btn btn-outline-secondary me-2" aria-expanded={showEstimate}
      onClick={() => setShowEstimate(value => !value)}>{showEstimate ? 'Close estimate' : 'Estimate return'}</button>}
    <button type="button" className="btn btn-primary" onClick={onBack}>{backLabel}</button>
    {recorded && <p className="alert alert-success mt-3" role="status">Return #{recorded.id} recorded: {money(recorded.total)}. Reopen this receipt for updated quantities.</p>}
    {returnEstimate && showEstimate && !recorded && <div className="mt-3"><ReturnEstimate receipt={receipt}
      options={returnEstimate} onRecorded={record => { setRecorded(record); setShowEstimate(false) }} /></div>}
    {returnEstimate && <ReturnHistory receipt={receipt} options={returnEstimate} refresh={recorded?.id ?? 0}
      timeZone={receipt.timeZoneId ?? timeZone} />}
  </section>
}
