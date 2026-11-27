import { useEffect, useRef, useState } from 'react'
import { api, ApiResponseError, operationKey, post } from './api'
import { baseQuantity } from './stockQuantity'
import LookupDialog from './LookupDialog'
import CashRegisterOpening from './CashRegisterOpening'

export interface Receipt {
  id: number
  total: number
  tendered: number
  currencyCode: string
  postedAtUtc: string
  printAvailable?: boolean
  paymentMethod?: 'Cash' | 'Credit'
  creditDueDate?: string | null
  outstanding?: number
  exchange?: { originalSaleId: number; returnId: number; returnTotal: number; difference: number } | null
  requestKey?: string
  businessName?: string | null
  locationName?: string | null
  customerId?: number | null
  customerName?: string | null
  timeZoneId?: string | null
  lines: { id: number; productName: string; unitPrice: number; quantity: number;
    productSku?: string | null; unitCode?: string | null; lineTotal?: number | null;
    conversionFactor?: number | null; baseUnitCode?: string | null;
    returnedQuantity?: number | null; remainingQuantity?: number | null;
    returnedAmount?: number | null; remainingAmount?: number | null }[]
}

interface ReturnOptions {
  path: string; productSearchPath: string; locationId: number; userId: number; canOperate: boolean
  businessId: number; registerRequired?: boolean
  onRecorded: (id: number, total: number) => void
  onExchangeRecorded: (id: number, replacementId: number) => void
}
interface ReturnRequest {
  locationId: number; requestKey: string; reason: string; expectedRefund: number;
  cashRefundConfirmed: boolean; lines: { lineId: number; quantity: number; restock: boolean }[]
}
interface PostedReturn {
  id: number; saleId: number; requestKey: string; total: number; reason: string; postedAtUtc: string;
  replacementSaleId?: number | null; netCashSettlement?: number | null;
  lines: { saleLineId: number; quantity: number; amount: number; restock: boolean }[]
}
interface ReturnPage { items: PostedReturn[]; hasMore: boolean }
interface Estimate { total: number; currencyCode: string; canPost: boolean; lines: { lineId: number; amount: number }[] }

function returnStorageKey(options: ReturnOptions) {
  return `cash-return:${options.userId}:${options.locationId}:${options.path}`
}

function readPending(options: ReturnOptions): { request: ReturnRequest | null; error: string } {
  try {
    const raw = sessionStorage.getItem(returnStorageKey(options))
    if (!raw) return { request: null, error: '' }
    const request = JSON.parse(raw) as ReturnRequest
    if (request.locationId !== options.locationId ||
      typeof request.requestKey !== 'string' || !/^[0-9a-f]{32}$/.test(request.requestKey) ||
      typeof request.reason !== 'string' || request.reason.length < 5 || request.reason.length > 200 ||
      !Array.isArray(request.lines) || !request.lines.length || request.lines.length > 100 ||
      request.lines.some(line => !Number.isSafeInteger(line.lineId) || line.lineId <= 0 ||
        !Number.isFinite(line.quantity) || line.quantity < 0.001 || line.quantity > 1000000 ||
        Math.abs(Math.round(line.quantity * 1000) - line.quantity * 1000) > 0.000001 ||
        typeof line.restock !== 'boolean') ||
      new Set(request.lines.map(line => line.lineId)).size !== request.lines.length ||
      request.cashRefundConfirmed !== true || !Number.isFinite(request.expectedRefund) || request.expectedRefund < 0)
      throw new Error('Invalid pending return')
    return { request, error: '' }
  } catch {
    return { request: null, error: 'Pending return storage is unavailable. Check return history before recording another refund.' }
  }
}

function ReturnHistory({ receipt, options, timeZone }: {
  receipt: Receipt; options: ReturnOptions; timeZone: string
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
  }, [options.path, options.locationId, page, reload])
  const money = (value: number) => receipt.currencyCode + ' ' + value.toFixed(2)
  return <section className="mt-3" aria-label="Return history">
    <h3 className="h6">Return history</h3>
    {error ? <p role="alert">{error} <button className="btn btn-sm btn-outline-secondary" type="button"
      onClick={() => setReload(value => value + 1)}>Retry</button></p> :
      !result ? <p role="status">Loading returns…</p> :
      !result.items.length ? <p className="small">No posted returns.</p> : <>
        <div className="table-responsive"><table className="table table-sm align-middle">
          <thead><tr><th scope="col">Return</th><th scope="col">Posted</th><th scope="col">Reason</th><th scope="col" className="text-end">Return value</th></tr></thead>
          <tbody>{result.items.map(item => <tr key={item.id}>
            <td><button type="button" className="btn btn-link p-0" aria-expanded={expanded === item.id}
              onClick={() => setExpanded(expanded === item.id ? null : item.id)}>Return #{item.id}</button>
              {expanded === item.id && <div className="small mt-1">{item.lines.map(line => <div key={line.saleLineId}>
                {receipt.lines.find(x => x.id === line.saleLineId)?.productName ?? 'Line #' + line.saleLineId}: {line.quantity} · {money(line.amount)} · {line.restock ? 'Restocked' : 'Not restocked'}
              </div>)}</div>}</td>
            <td>{new Date(item.postedAtUtc).toLocaleString(undefined, { timeZone })}</td>
            <td>{item.reason}{item.replacementSaleId != null && <small className="d-block">
              Exchange · Invoice #{item.replacementSaleId} · {
                item.netCashSettlement == null ? 'Settlement requires reconciliation' :
                item.netCashSettlement > 0 ? 'Cash collected: ' + money(item.netCashSettlement) :
                item.netCashSettlement < 0 ? 'Cash refunded: ' + money(-item.netCashSettlement) : 'No cash settlement'
              }</small>}</td><td className="text-end">{money(item.total)}</td>
          </tr>)}</tbody>
        </table></div>
        {(page > 1 || result.hasMore) && <div className="d-flex gap-2 align-items-center">
          <button type="button" className="btn btn-outline-secondary btn-sm" disabled={page === 1} onClick={() => setPage(value => value - 1)}>Previous</button>
          <span>Page {page}</span><button type="button" className="btn btn-outline-secondary btn-sm" disabled={!result.hasMore} onClick={() => setPage(value => value + 1)}>Next</button>
        </div>}
      </>}
  </section>
}

function ReturnEstimate({ receipt, options }: { receipt: Receipt; options: ReturnOptions }) {
  const key = returnStorageKey(options)
  const [restored] = useState(() => readPending(options))
  const [pending, setPending] = useState(restored.request)
  const [quantities, setQuantities] = useState<Record<number, string>>({})
  const [dispositions, setDispositions] = useState<Record<number, string>>({})
  const [estimate, setEstimate] = useState<Estimate | null>(null)
  const [reason, setReason] = useState('')
  const [confirmed, setConfirmed] = useState(false)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  const [registerReady, setRegisterReady] = useState(false)
  const [registerRejected, setRegisterRejected] = useState(false)
  const [registerRefresh, setRegisterRefresh] = useState(0)
  const registerRequired = options.registerRequired === true || registerRejected
  useEffect(() => { if (registerRequired && !registerReady) setConfirmed(false) }, [registerRequired, registerReady])
  const registerPanel = registerRequired && <CashRegisterOpening key={registerRefresh}
    userId={options.userId} businessId={options.businessId} locationId={options.locationId}
    currencyCode={receipt.currencyCode} canOperate={options.canOperate} disabled={busy} required onReady={setRegisterReady} />
  const lock = useRef(false)
  const mounted = useRef(true)
  useEffect(() => {
    mounted.current = true
    return () => { mounted.current = false }
  }, [])
  const available = (line: Receipt['lines'][number]) => Math.max(0, line.remainingQuantity ?? 0)
  const selected = receipt.lines.filter(line => (quantities[line.id] ?? '') !== '' && Number(quantities[line.id]) !== 0)
  const valid = selected.length > 0 && selected.every(line => {
    const value = Number(quantities[line.id])
    return value <= available(line) && baseQuantity(value, line.conversionFactor ?? 1) !== null
  })
  function change(next: Record<number, string>) {
    setQuantities(next); setEstimate(null); setConfirmed(false); setError('')
  }
  function finish(record: PostedReturn, request: ReturnRequest) {
    if (!Number.isSafeInteger(record.id) || record.id <= 0 || record.saleId !== receipt.id ||
      record.requestKey !== request.requestKey || record.total !== request.expectedRefund ||
      record.reason !== request.reason || record.lines.length !== request.lines.length ||
      !request.lines.every(line => record.lines.some(saved => saved.saleLineId === line.lineId &&
        saved.quantity === line.quantity && saved.restock === line.restock)))
      throw new Error('Return response could not be verified.')
    sessionStorage.removeItem(key)
    setPending(null)
    if (mounted.current) options.onRecorded(record.id, record.total)
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
      if (cause instanceof ApiResponseError && cause.code === 'REGISTER_REQUIRED') {
        setRegisterRejected(true); setRegisterReady(false); setRegisterRefresh(x => x + 1)
        setError('Open your cash register and retry the same recording. Do not refund cash again.')
      } else if (cause instanceof ApiResponseError && cause.code === 'RETURN_REJECTED') {
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
    {registerPanel}
    {error && <p role="alert" className="text-danger">{error}</p>}
    <div className="d-flex flex-wrap gap-2">
      <button type="button" className="btn btn-outline-secondary" disabled={busy} onClick={() => void recordReturn(pending, true)}>Check posted return</button>
      <button type="button" className="btn btn-primary" disabled={busy || !options.canOperate} onClick={() => void recordReturn(pending)}>Retry recording</button>
    </div>
  </section>
  const canRecord = !!estimate?.canPost && options.canOperate && valid && !busy && !restored.error &&
    reason.trim().length >= 5 && confirmed && (!registerRequired || registerReady) && selected.every(line => !!dispositions[line.id])
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
            onChange={event => change({ ...quantities, [line.id]: event.target.value })} />
            {line.conversionFactor != null && Number(quantities[line.id] ?? '') !== 0 &&
              <small className="d-block mt-1">{baseQuantity(Number(quantities[line.id]), line.conversionFactor) === null
                ? 'Enter a quantity that converts exactly to three stock decimals.'
                : `${baseQuantity(Number(quantities[line.id]), line.conversionFactor)} ${line.baseUnitCode ?? 'base units'} stock equivalent`}
              </small>}</td>
          <td className="text-end">{estimate?.lines.some(item => item.lineId === line.id)
            ? estimate.currencyCode + ' ' + estimate.lines.find(item => item.lineId === line.id)!.amount.toFixed(2) : '—'}</td>
        </tr>)}</tbody>
      </table></div>
      {(restored.error || error) && <p role="alert" className="text-danger">{restored.error || error}</p>}
      {estimate && <p role="status"><strong>Estimated return amount: {estimate.currencyCode} {estimate.total.toFixed(2)}</strong></p>}
      <button type="submit" className="btn btn-outline-primary btn-sm" disabled={!valid || busy}>
        {busy ? 'Calculating…' : 'Calculate estimate'}
      </button>
    </form>
    {estimate && !estimate.canPost && <p className="small mt-2 mb-0">Preview only. Return posting is not enabled.</p>}
    {estimate?.canPost && options.canOperate && <div className="border-top mt-3 pt-2">
      {registerPanel}
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
      <label className="form-check mb-2"><input className="form-check-input" type="checkbox" checked={confirmed && (!registerRequired || registerReady)} disabled={busy || registerRequired && !registerReady}
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

interface ExchangeProduct {
  id: number; name: string; sku: string; unitCode: string | null; salePrice: number; available: number
  conversions: { unitId: number; unitCode: string; factor: number; salePrice: number }[]
}
interface ExchangeQuote {
  currencyCode: string; returnTotal: number; replacementTotal: number; difference: number
  settlement: 'Collect' | 'Refund' | 'Even'; canPost: boolean
}
interface ExchangeRequest {
  locationId: number; requestKey: string; reason: string; expectedSettlement: number; settlementConfirmed: true
  returns: ReturnRequest['lines']
  replacements: { productId: number; quantity: number; expectedUnitPrice: number;
    expectedBaseUnitCode: string | null; unitId?: number; expectedFactor?: number }[]
}
interface PendingExchange { request: ExchangeRequest; currencyCode: string; returnTotal: number; replacementTotal: number }
interface PostedExchange extends ExchangeQuote {
  returnId: number; originalSaleId: number; replacementSaleId: number; requestKey: string; reason: string
}
function exchangeStorageKey(options: ReturnOptions) {
  return `cash-exchange:${options.userId}:${options.locationId}:${options.path}`
}
function readExchange(options: ReturnOptions): { pending: PendingExchange | null; error: string } {
  try {
    const raw = sessionStorage.getItem(exchangeStorageKey(options))
    if (!raw) return { pending: null, error: '' }
    const saved = JSON.parse(raw) as PendingExchange
    const r = saved.request
    const quantity = (n: number) => Number.isFinite(n) && n >= 0.001 && n <= 1000000 &&
      Math.abs(Math.round(n * 1000) - n * 1000) < 0.000001
    if (!r || r.locationId !== options.locationId || typeof r.requestKey !== 'string' ||
      !/^[0-9a-f]{32}$/.test(r.requestKey) || typeof r.reason !== 'string' || r.reason.length < 5 ||
      r.reason.length > 200 || r.settlementConfirmed !== true ||
      !Number.isFinite(r.expectedSettlement) || !Number.isFinite(saved.returnTotal) ||
      !Number.isFinite(saved.replacementTotal) || saved.returnTotal < 0 || saved.replacementTotal <= 0 ||
      Math.abs(saved.replacementTotal - saved.returnTotal - r.expectedSettlement) > 0.000001 ||
      typeof saved.currencyCode !== 'string' || !Array.isArray(r.returns) || !r.returns.length || r.returns.length > 100 ||
      r.returns.some(x => !Number.isSafeInteger(x.lineId) || x.lineId <= 0 || !quantity(x.quantity) || typeof x.restock !== 'boolean') ||
      new Set(r.returns.map(x => x.lineId)).size !== r.returns.length ||
      !Array.isArray(r.replacements) || !r.replacements.length || r.replacements.length > 100 ||
      r.replacements.some(x => !Number.isSafeInteger(x.productId) || x.productId <= 0 || !quantity(x.quantity) ||
        !Number.isFinite(x.expectedUnitPrice) || x.expectedUnitPrice <= 0 ||
        (x.expectedBaseUnitCode !== null && typeof x.expectedBaseUnitCode !== 'string') ||
        (x.unitId != null && (!Number.isSafeInteger(x.unitId) || x.unitId <= 0 ||
          !Number.isFinite(x.expectedFactor) || x.expectedFactor! <= 0)) ||
        (x.unitId == null && x.expectedFactor != null)) ||
      new Set(r.replacements.map(x => x.productId)).size !== r.replacements.length)
      throw new Error('Invalid pending exchange')
    return { pending: saved, error: '' }
  } catch {
    return { pending: null, error: 'Pending exchange storage is unavailable. Reconcile exchange history before recording another return or exchange.' }
  }
}

function ExchangeEstimate({ receipt, options, pending, onPending }: {
  receipt: Receipt; options: ReturnOptions; pending: PendingExchange | null
  onPending: (value: PendingExchange | null) => void
}) {
  const [returns, setReturns] = useState<Record<number, { quantity: string; condition: string }>>({})
  const [replacements, setReplacements] = useState<{ product: ExchangeProduct; quantity: string; unitId: number }[]>([])
  const [lookup, setLookup] = useState(false)
  const [search, setSearch] = useState('')
  const [products, setProducts] = useState<ExchangeProduct[]>([])
  const [status, setStatus] = useState('')
  const [quote, setQuote] = useState<ExchangeQuote | null>(null)
  const [reason, setReason] = useState('')
  const [confirmed, setConfirmed] = useState(false)
  const [error, setError] = useState('')
  const [busy, setBusy] = useState(false)
  const [registerReady, setRegisterReady] = useState(false)
  const [registerRejected, setRegisterRejected] = useState(false)
  const [registerRefresh, setRegisterRefresh] = useState(0)
  const registerRequired = options.registerRequired === true || registerRejected
  useEffect(() => { if (registerRequired && !registerReady) setConfirmed(false) }, [registerRequired, registerReady])
  const registerPanel = registerRequired && <CashRegisterOpening key={registerRefresh}
    userId={options.userId} businessId={options.businessId} locationId={options.locationId}
    currencyCode={receipt.currencyCode} canOperate={options.canOperate} disabled={busy} required onReady={setRegisterReady} />
  const lock = useRef(false)
  const mounted = useRef(true)
  useEffect(() => {
    mounted.current = true
    return () => { mounted.current = false }
  }, [])
  useEffect(() => {
    if (!lookup) return
    const controller = new AbortController()
    setProducts([]); setStatus('Searching…')
    const timer = setTimeout(() => {
      api<ExchangeProduct[]>(options.productSearchPath + '?' + new URLSearchParams({
        locationId: String(options.locationId), search
      }), { signal: controller.signal }).then(items => {
        if (!controller.signal.aborted) { setProducts(items); setStatus(items.length ? '' : 'No matching products.') }
      }).catch(() => { if (!controller.signal.aborted) setStatus('Unable to search. Edit the search to retry.') })
    }, 200)
    return () => { clearTimeout(timer); controller.abort() }
  }, [lookup, search, options.productSearchPath, options.locationId])
  const selected = receipt.lines.filter(line => Number(returns[line.id]?.quantity ?? '') !== 0)
  const unit = (line: typeof replacements[number]) => line.product.conversions?.find(x => x.unitId === line.unitId)
  const valid = selected.length > 0 && selected.every(line =>
    Number(returns[line.id].quantity) <= (line.remainingQuantity ?? 0) &&
    baseQuantity(Number(returns[line.id].quantity), line.conversionFactor ?? 1) !== null &&
    !!returns[line.id].condition) && replacements.length > 0 && replacements.every(line =>
    baseQuantity(Number(line.quantity), unit(line)?.factor ?? 1) !== null &&
    Number(line.quantity) <= 1000000)
  const money = (amount: number) => receipt.currencyCode + ' ' + amount.toFixed(2)
  function invalidate() { setQuote(null); setConfirmed(false); setError('') }
  function exchangeItems() {
    return {
      locationId: options.locationId,
      returns: selected.map(line => ({ lineId: line.id, quantity: Number(returns[line.id].quantity),
        restock: returns[line.id].condition === 'restock' })),
      replacements: replacements.map(line => {
        const chosen = unit(line)
        return { productId: line.product.id, quantity: Number(line.quantity),
          expectedUnitPrice: chosen?.salePrice ?? line.product.salePrice,
          expectedBaseUnitCode: line.product.unitCode,
          ...(chosen ? { unitId: chosen.unitId, expectedFactor: chosen.factor } : {}) }
      })
    }
  }
  async function record(saved: PendingExchange, checkOnly = false) {
    if (lock.current) return
    lock.current = true; setBusy(true); setError('')
    const request = saved.request
    try {
      if (!checkOnly) {
        sessionStorage.setItem(exchangeStorageKey(options), JSON.stringify(saved))
        onPending(saved)
      }
      const result = checkOnly
        ? await api<PostedExchange>(options.path + '/exchanges?' + new URLSearchParams({
          locationId: String(options.locationId), requestKey: request.requestKey
        }))
        : await post<PostedExchange>(options.path + '/exchanges', request)
      if (!Number.isSafeInteger(result.returnId) || result.returnId <= 0 ||
        !Number.isSafeInteger(result.replacementSaleId) || result.replacementSaleId <= 0 ||
        result.replacementSaleId === receipt.id || result.originalSaleId !== receipt.id ||
        result.requestKey !== request.requestKey || result.reason !== request.reason ||
        result.currencyCode !== saved.currencyCode || result.returnTotal !== saved.returnTotal ||
        result.replacementTotal !== saved.replacementTotal || result.difference !== request.expectedSettlement)
        throw new Error('Exchange response could not be verified.')
      sessionStorage.removeItem(exchangeStorageKey(options))
      if (mounted.current) {
        onPending(null)
        options.onExchangeRecorded(result.returnId, result.replacementSaleId)
      }
    } catch (cause) {
      if (mounted.current) {
        if (cause instanceof ApiResponseError && cause.code === 'REGISTER_REQUIRED') {
          setRegisterRejected(true); setRegisterReady(false); setRegisterRefresh(x => x + 1)
        }
        setError((cause instanceof Error ? cause.message : 'Unable to verify exchange.') +
          ' Check the recorded exchange or retry the same request. Do not settle cash again.')
      }
    } finally { lock.current = false; if (mounted.current) setBusy(false) }
  }
  if (pending) return <section className="border rounded p-2 mb-3" aria-label="Exchange recovery">
    <h3 className="h6">Verify previous exchange</h3>
    <p>Confirmed settlement: <strong>{pending.currencyCode} {pending.request.expectedSettlement.toFixed(2)}</strong>. Do not collect or refund cash again.</p>
    {registerPanel}
    {error && <p role="alert" className="text-danger">{error}</p>}
    <div className="d-flex flex-wrap gap-2">
      <button type="button" className="btn btn-outline-secondary" disabled={busy}
        onClick={() => void record(pending, true)}>Check posted exchange</button>
      <button type="button" className="btn btn-primary" disabled={busy || !options.canOperate}
        onClick={() => void record(pending)}>Retry exchange recording</button>
    </div>
  </section>
  const canRecord = quote?.canPost === true && options.canOperate && valid && confirmed && (!registerRequired || registerReady) &&
    !busy && reason.trim().length >= 5 && Number.isFinite(quote.difference) &&
    Math.max(Math.abs(quote.difference), quote.returnTotal, quote.replacementTotal) <= Number.MAX_SAFE_INTEGER / 100
  function changeReturn(id: number, field: 'quantity' | 'condition', value: string) {
    setReturns(previous => ({ ...previous, [id]: { ...(previous[id] ?? { quantity: '', condition: '' }), [field]: value } }))
    invalidate()
  }
  return <section className="border rounded p-2 mb-3" aria-label="Exchange estimate">
    <h3 className="h6">Exchange estimate</h3>
    <p className="small">Calculation does not post a sale, refund or stock movement. Record only after confirming the actual settlement when posting is available.</p>
    <form onSubmit={async event => {
      event.preventDefault()
      if (!valid || lock.current) return
      lock.current = true; setBusy(true); invalidate()
      try {
        setQuote(await post<ExchangeQuote>(options.path + '/exchange-estimate', exchangeItems()))
      } catch (cause) { setError(cause instanceof Error ? cause.message : 'Unable to calculate exchange.') }
      finally { lock.current = false; setBusy(false) }
    }}>
      <div className="table-responsive"><table className="table table-sm align-middle">
        <thead><tr><th scope="col">Original item</th><th scope="col">Quantity to return</th><th scope="col">Condition</th></tr></thead>
        <tbody>{receipt.lines.filter(line => (line.remainingQuantity ?? 0) > 0).map(line => <tr key={line.id}>
          <td>{line.productName}<small className="d-block">Remaining: {line.remainingQuantity} {line.unitCode}</small></td>
          <td><input className="form-control form-control-sm" type="number" min="0" max={line.remainingQuantity ?? 0}
            step="0.001" aria-label={'Exchange quantity for ' + line.productName} disabled={busy}
            value={returns[line.id]?.quantity ?? ''} onChange={e => changeReturn(line.id, 'quantity', e.target.value)} /></td>
          <td><select className="form-select form-select-sm" aria-label={'Exchange condition for ' + line.productName}
            disabled={busy} value={returns[line.id]?.condition ?? ''} onChange={e => changeReturn(line.id, 'condition', e.target.value)}>
            <option value="">Choose condition</option><option value="restock">Sellable</option><option value="no-stock">Damaged / not sellable</option>
          </select></td>
        </tr>)}</tbody>
      </table></div>
      <h4 className="h6">Replacement items</h4>
      {replacements.length > 0 && <div className="table-responsive"><table className="table table-sm align-middle">
        <thead><tr><th scope="col">Product</th><th scope="col">Unit</th><th scope="col">Qty</th><th scope="col">Rate</th><th scope="col">Action</th></tr></thead>
        <tbody>{replacements.map(line => <tr key={line.product.id}>
          <td>{line.product.name}<small className="d-block">{line.product.sku}</small></td>
          <td><select className="form-select form-select-sm" aria-label={'Replacement unit for ' + line.product.name} disabled={busy}
            value={line.unitId} onChange={e => { const id = Number(e.target.value); setReplacements(items => items.map(x => x.product.id === line.product.id ? { ...x, unitId: id, quantity: '1' } : x)); invalidate() }}>
            <option value={0}>{line.product.unitCode ?? 'Base unit'}</option>
            {line.product.conversions?.map(x => <option key={x.unitId} value={x.unitId}>{x.unitCode}</option>)}
          </select></td>
          <td><input className="form-control form-control-sm" type="number" min="0.001" max="1000000" step="0.001"
            aria-label={'Replacement quantity for ' + line.product.name} disabled={busy} value={line.quantity}
            onChange={e => { const quantity = e.target.value; setReplacements(items => items.map(x => x.product.id === line.product.id ? { ...x, quantity } : x)); invalidate() }} /></td>
          <td className="text-nowrap">{money(unit(line)?.salePrice ?? line.product.salePrice)}</td>
          <td><button type="button" className="btn btn-outline-secondary btn-sm" disabled={busy}
            onClick={() => { setReplacements(items => items.filter(x => x.product.id !== line.product.id)); invalidate() }}>Remove</button></td>
        </tr>)}</tbody></table></div>}
      <div className="d-flex gap-2 flex-wrap">
        <button type="button" className="btn btn-outline-secondary btn-sm" disabled={busy || replacements.length >= 100}
          onClick={() => { setSearch(''); setLookup(true) }}>Add replacement</button>
        <button type="submit" className="btn btn-outline-primary btn-sm" disabled={!valid || busy}>{busy ? 'Calculating…' : 'Calculate exchange'}</button>
      </div>
      {error && <p role="alert" className="text-danger mt-2">{error}</p>}
      {quote && <div role="status" className="mt-3">
        <p className="mb-1">Return value: {quote.currencyCode} {quote.returnTotal.toFixed(2)}</p>
        <p className="mb-1">Replacement value: {quote.currencyCode} {quote.replacementTotal.toFixed(2)}</p>
        <strong>{quote.settlement === 'Even' ? 'No balance to settle' : (quote.settlement === 'Collect' ? 'Estimated collection: ' : 'Estimated refund: ') + quote.currencyCode + ' ' + Math.abs(quote.difference).toFixed(2)}</strong>
      </div>}
    </form>
    {quote && !quote.canPost && <p className="small mt-2">Preview only. Exchange posting is not enabled.</p>}
    {quote?.canPost && options.canOperate && <div className="border-top mt-3 pt-2">
      {registerPanel}
      <label className="d-block mb-2">Exchange reason
        <input className="form-control" maxLength={200} disabled={busy} value={reason}
          onChange={event => { setReason(event.target.value); setConfirmed(false) }} />
      </label>
      <label className="form-check mb-2"><input className="form-check-input" type="checkbox" checked={confirmed && (!registerRequired || registerReady)}
        disabled={busy || registerRequired && !registerReady} onChange={event => setConfirmed(event.target.checked)} />
        {quote.difference > 0 ? 'I confirm the stated cash has been collected.' :
          quote.difference < 0 ? 'I confirm the stated cash has been refunded.' : 'I confirm no cash was exchanged.'}
      </label>
      <button type="button" className="btn btn-primary" disabled={!canRecord} onClick={() => {
        if (!canRecord || !quote) return
        void record({ currencyCode: quote.currencyCode, returnTotal: quote.returnTotal, replacementTotal: quote.replacementTotal,
          request: { ...exchangeItems(), requestKey: operationKey(), reason: reason.trim(),
            expectedSettlement: quote.difference, settlementConfirmed: true } })
      }}>Record exchange</button>
    </div>}
    {lookup && <LookupDialog title="Replacement products" searchId="exchange-product-search" search={search}
      placeholder="Search product, SKU or barcode" columns={['SKU', 'Product', 'Available stock', 'Rate']}
      rows={products.map(product => ({ id: product.id, cells: [product.sku, product.name, String(product.available), money(product.salePrice)],
        disabled: replacements.some(line => line.product.id === product.id) }))}
      status={status} context={receipt.locationName ?? undefined} onSearch={value => { setProducts([]); setSearch(value) }}
      onSelect={id => { const product = products.find(x => x.id === id); if (product) { setReplacements(items => [...items, { product, quantity: '1', unitId: 0 }]); invalidate() } setLookup(false) }}
      onClose={() => setLookup(false)} />}
  </section>
}

export default function CashReceipt({ receipt, businessName, timeZone, pdfPath, printNotice, backLabel, onBack, returnEstimate, returnNotice }: {
  receipt: Receipt
  businessName: string
  timeZone: string
  pdfPath: string
  printNotice?: string
  backLabel: string
  onBack: () => void
  returnEstimate?: ReturnOptions
  returnNotice?: string
}) {
  const [showEstimate, setShowEstimate] = useState(() => !!returnEstimate && !!readPending(returnEstimate).request)
  const [restoredExchange] = useState(() => returnEstimate ? readExchange(returnEstimate) : { pending: null, error: '' })
  const [pendingExchange, setPendingExchange] = useState(restoredExchange.pending)
  const [showExchange, setShowExchange] = useState(!!restoredExchange.pending)
  const returnRecovery = returnEstimate ? readPending(returnEstimate) : null
  const money = (amount: number) => receipt.currencyCode + ' ' + amount.toFixed(2)
  const credit = receipt.paymentMethod === 'Credit'
  return <section className="content-section" aria-label={credit ? 'Credit invoice' : 'Cash receipt'}>
    <h2>{credit ? 'Credit invoice' : 'Cash receipt'} #{receipt.id}</h2>
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
    {receipt.exchange ? <p><strong>Replacement invoice total {money(receipt.total)}</strong><br />
      Original invoice #{receipt.exchange.originalSaleId} · Return #{receipt.exchange.returnId}<br />
      Return value {money(receipt.exchange.returnTotal)}<br />
      {receipt.exchange.difference > 0 ? 'Cash collected ' + money(receipt.exchange.difference) :
        receipt.exchange.difference < 0 ? 'Cash refunded ' + money(-receipt.exchange.difference) : 'No cash settlement'}</p> :
      credit ? <p><strong>Total {money(receipt.total)}</strong><br />
      Outstanding {money(receipt.outstanding ?? receipt.total)}<br />Due date: {receipt.creditDueDate}</p> :
      <p><strong>Total {money(receipt.total)}</strong><br />Cash received {money(receipt.tendered)}<br />
      Change {money(receipt.tendered - receipt.total)}</p>}
    {printNotice && <p role="status" className="small text-body-secondary">{printNotice}</p>}
    {receipt.printAvailable
      ? <a className="btn btn-outline-secondary me-2" href={pdfPath} target="_blank" rel="noopener noreferrer">{receipt.exchange ? 'Print exchange invoice' : credit ? 'Print credit invoice' : 'Print receipt'}</a>
      : <p className="small text-body-secondary" role="status">RDLC printing requires a Windows report host.</p>}
    {restoredExchange.error && <p role="alert" className="text-danger">{restoredExchange.error}</p>}
    {returnEstimate && !receipt.exchange && !pendingExchange && !restoredExchange.error && returnEstimate.canOperate && <button type="button" className="btn btn-outline-secondary me-2" aria-expanded={showEstimate}
      onClick={() => { setShowExchange(false); setShowEstimate(value => !value) }}>{showEstimate ? 'Close estimate' : 'Estimate return'}</button>}
    {returnEstimate && !receipt.exchange && !pendingExchange && !restoredExchange.error && !returnRecovery?.request &&
      !returnRecovery?.error && returnEstimate.canOperate && !credit && receipt.lines.some(line => (line.remainingQuantity ?? 0) > 0) && !showEstimate &&
      <button type="button" className="btn btn-outline-secondary me-2" aria-expanded={showExchange}
        onClick={() => setShowExchange(value => !value)}>{showExchange ? 'Close exchange' : 'Estimate exchange'}</button>}
    <button type="button" className="btn btn-primary" onClick={onBack}>{backLabel}</button>
    {returnNotice && <p className="alert alert-success mt-3" role="status">{returnNotice}</p>}
    {returnEstimate && !receipt.exchange && !pendingExchange && !restoredExchange.error && showEstimate && <div className="mt-3"><ReturnEstimate receipt={receipt}
      options={returnEstimate} /></div>}
    {returnEstimate && (showExchange || pendingExchange) && !restoredExchange.error && <div className="mt-3">
      <ExchangeEstimate receipt={receipt} options={returnEstimate} pending={pendingExchange} onPending={setPendingExchange} /></div>}
    {returnEstimate && <ReturnHistory receipt={receipt} options={returnEstimate}
      timeZone={receipt.timeZoneId ?? timeZone} />}
  </section>
}
