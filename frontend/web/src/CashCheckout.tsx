import { useEffect, useRef, useState } from 'react'
import { api, ApiResponseError, operationKey, post, type RetailDashboard } from './api'

interface Product {
  id: number; name: string; sku: string; barcode: string | null; salePrice: number; available: number
}
interface Line { productId: number; name: string; quantity: number; expectedUnitPrice: number }
interface SaleRequest { locationId: number; requestKey: string; tendered: number; lines: Line[] }
interface Receipt {
  id: number; total: number; tendered: number; currencyCode: string; postedAtUtc: string
  lines: { id: number; productName: string; unitPrice: number; quantity: number }[]
}

function restore(key: string): SaleRequest | null {
  let raw: string | null
  try { raw = sessionStorage.getItem(key) } catch { return null }
  if (!raw) return null
  try {
    const saved = JSON.parse(raw)
    if (!saved || !Number.isSafeInteger(saved.locationId) || saved.locationId < 1 ||
      typeof saved.requestKey !== 'string' || !saved.requestKey ||
      !Number.isFinite(saved.tendered) || saved.tendered < 0) throw new Error('Invalid checkout')
    // Recover a single-product checkout started before baskets were introduced.
    const lines = Array.isArray(saved.lines) ? saved.lines : [{
      productId: saved.productId, name: 'Product #' + saved.productId,
      quantity: saved.quantity, expectedUnitPrice: saved.expectedUnitPrice
    }]
    if (!lines.length || lines.length > 100 || lines.some((line: Line) =>
      !Number.isSafeInteger(line.productId) || line.productId < 1 ||
      typeof line.name !== 'string' || !Number.isSafeInteger(line.quantity) ||
      line.quantity < 1 || line.quantity > 1000000 ||
      !Number.isFinite(line.expectedUnitPrice) || line.expectedUnitPrice < 0 ||
      Math.round(line.expectedUnitPrice * 100) !== line.expectedUnitPrice * 100 &&
      Math.abs(Math.round(line.expectedUnitPrice * 100) - line.expectedUnitPrice * 100) > 0.000001))
      throw new Error('Invalid checkout lines')
    return { locationId: saved.locationId, requestKey: saved.requestKey, tendered: saved.tendered, lines }
  } catch {
    try { sessionStorage.removeItem(key) } catch { /* Storage may be unavailable. */ }
    return null
  }
}

export default function CashCheckout({ userId, dashboard }: { userId: number; dashboard: RetailDashboard }) {
  const { business, locations } = dashboard
  const key = `cash-checkout:${userId}:${business.id}`
  const base = `/businesses/${business.id}/cash-sales`
  const [pending, setPending] = useState(() => restore(key))
  const [lookupState, setLookupState] = useState<'checking' | 'missing' | 'unknown'>('checking')
  const [refresh, setRefresh] = useState(0)
  const [receipt, setReceipt] = useState<Receipt | null>(null)
  const [locationId, setLocation] = useState(pending?.locationId ?? locations[0]?.id ?? 0)
  const [basket, setBasket] = useState<Line[]>(pending?.lines ?? [])
  const [search, setSearch] = useState('')
  const [products, setProducts] = useState<Product[]>([])
  const [product, setProduct] = useState<Product | null>(null)
  const [quantity, setQuantity] = useState('1')
  const [cash, setCash] = useState(pending ? String(pending.tendered) : '')
  const [error, setError] = useState('')
  const [hint, setHint] = useState('')
  const [busy, setBusy] = useState(false)
  const lock = useRef(false)
  const qtyInput = useRef<HTMLInputElement>(null)
  const total = basket.reduce((sum, line) => sum + Math.round(line.expectedUnitPrice * 100) * line.quantity, 0) / 100
  const money = (amount: number) => business.currencyCode + ' ' + amount.toFixed(2)

  useEffect(() => {
    if (!pending || receipt || busy) return
    let active = true
    setLookupState('checking')
    const query = new URLSearchParams({ locationId: String(pending.locationId), requestKey: pending.requestKey })
    api<Receipt>(base + '/receipt?' + query)
      .then(result => { if (active) { setReceipt(result); setError('') } })
      .catch(cause => {
        if (!active) return
        setLookupState(cause instanceof ApiResponseError && cause.status === 404 ? 'missing' : 'unknown')
      })
    return () => { active = false }
  }, [base, pending, refresh, receipt, busy])

  useEffect(() => {
    if (!locationId || !dashboard.canOperate || pending || receipt) return
    let active = true
    setProducts([])
    setHint('Finding products…')
    const timer = setTimeout(() => {
      api<Product[]>(`${base}/products?locationId=${locationId}&search=${encodeURIComponent(search)}`)
        .then(items => {
          if (!active) return
          setProducts(items)
          setHint(items.length ? '' : 'No eligible products found.')
          const exact = search.trim() && items.find(x => x.barcode === search.trim().toUpperCase())
          if (exact) { setProduct(exact); qtyInput.current?.focus() }
        }).catch(cause => { if (active) setHint(cause.message) })
    }, 200)
    return () => { active = false; clearTimeout(timer) }
  }, [base, search, locationId, dashboard.canOperate, pending, receipt])

  function addItem() {
    if (!product) return
    const count = Number(quantity)
    const existing = basket.find(line => line.productId === product.id)
    const combined = count + (existing?.quantity ?? 0)
    if (!Number.isSafeInteger(count) || count < 1 || !Number.isSafeInteger(combined) ||
      combined > Math.min(product.available, 1000000)) {
      setError('Choose a whole quantity within the available stock.')
      return
    }
    if (!existing && basket.length >= 100) { setError('A receipt supports up to 100 products.'); return }
    const line = { productId: product.id, name: product.name, quantity: combined, expectedUnitPrice: product.salePrice }
    setBasket(existing ? basket.map(item => item === existing ? line : item) : [...basket, line])
    setError('')
    setProduct(null)
    setSearch('')
    setQuantity('1')
  }

  async function recordSale() {
    const received = Number(cash)
    if (lock.current || !dashboard.canOperate || (pending && lookupState !== 'missing')) return
    if (!pending && (!locationId || !basket.length || basket.some(line =>
      !Number.isSafeInteger(line.quantity) || line.quantity < 1 ||
      !Number.isFinite(line.expectedUnitPrice) || line.expectedUnitPrice < 0) ||
      !Number.isFinite(total) || total <= 0 || !Number.isFinite(received) ||
      !Number.isInteger(Math.round(received * 100)) || received < total)) {
      setError('Check basket quantities and enter enough cash before recording the sale.')
      return
    }
    const request = pending ?? { locationId, lines: basket, tendered: received, requestKey: operationKey() }
    lock.current = true
    setBusy(true)
    setError('')
    try {
      try { sessionStorage.setItem(key, JSON.stringify(request)) } catch { /* Keep the request key in memory. */ }
      setPending(request)
      setReceipt(await post<Receipt>(base, request))
    } catch (cause) {
      setError(cause instanceof Error ? cause.message : 'Unable to confirm sale.')
      if (cause instanceof ApiResponseError && [400, 403].includes(cause.status)) {
        try { sessionStorage.removeItem(key) } catch { /* Storage may be unavailable. */ }
        setPending(null)
      }
    } finally { lock.current = false; setBusy(false) }
  }

  async function lastReceipt() {
    setBusy(true)
    setError('')
    try { setReceipt(await api<Receipt>(base + '/latest?locationId=' + locationId)) }
    catch (cause) {
      setError(cause instanceof ApiResponseError && cause.status === 404
        ? 'No previous receipt at this location for your account.' : 'Unable to load the last receipt.')
    } finally { setBusy(false) }
  }

  if (receipt) return <section className="content-section cash-receipt" aria-label="Cash receipt">
    <h2>Cash receipt #{receipt.id}</h2>
    <p>{business.name} · {new Date(receipt.postedAtUtc).toLocaleString(undefined, { timeZone: business.timeZoneId })}</p>
    <ul>{receipt.lines.map(line => <li key={line.id}>{line.productName} · {line.quantity} × {money(line.unitPrice)}</li>)}</ul>
    <p><strong>Total {money(receipt.total)}</strong><br />Cash received {money(receipt.tendered)}<br />
      Change {money(receipt.tendered - receipt.total)}</p>
    <button className="btn btn-outline-secondary me-2" onClick={() => window.print()}>Print receipt</button>
    <button className="btn btn-primary" onClick={() => {
      if (pending) {
        try { sessionStorage.removeItem(key) } catch { /* Storage may be unavailable. */ }
        setPending(null)
        setBasket([])
        setCash('')
      }
      setReceipt(null)
      setProduct(null)
      setSearch('')
      setQuantity('1')
    }}>{pending ? 'New sale' : 'Back to checkout'}</button>
  </section>

  return <section className="pos-checkout" aria-label="Point of Sale checkout">
    <header className="pos-header">
      <div>
        <p className="pos-kicker">RETAIL / POINT OF SALE</p>
        <h2>{pending ? 'Recover Pending Sale' : 'New Cash Sale'}</h2>
        <p>{pending ? 'Verify the previous transaction before submitting again.' :
          'Search or scan products, review the basket and collect payment.'}</p>
      </div>
      <span className="pos-mode">Cash-only checkout · {business.currencyCode}</span>
    </header>
    {error && <p role="alert" className="alert alert-danger">{error}</p>}
    {pending ? <section className="content-section" aria-label="Pending sale recovery">
      <h2>Pending cash sale</h2>
      <ul>{pending.lines.map(line => <li key={line.productId}>{line.name} · {line.quantity} × {money(line.expectedUnitPrice)}</li>)}</ul>
      <p><strong>Total {money(total)}</strong> · Cash received {money(pending.tendered)}</p>
      <p role="status">{busy ? 'Recording sale…' : lookupState === 'missing' ? 'No posted receipt found for this sale.'
        : lookupState === 'unknown' ? 'Receipt status could not be verified.' : 'Checking for a posted receipt…'}</p>
      {lookupState === 'missing' && <button type="button" className="btn btn-primary me-2" disabled={busy || !dashboard.canOperate}
        onClick={() => void recordSale()}>Record this sale</button>}
      <button type="button" className="btn btn-outline-secondary" disabled={busy || lookupState === 'checking'}
        onClick={() => { setLookupState('checking'); setRefresh(n => n + 1) }}>Check status again</button>
    </section> : <div className="pos-grid">
      <section className="content-section pos-catalog" aria-label="Product selection">
        <div className="pos-panel-heading"><h2>Find products</h2><span>Search and add items</span></div>
        <div className="pos-location-row">
          <label className="form-label">Store location
            <select className="form-select" value={locationId} disabled={busy || basket.length > 0}
              onChange={e => { setLocation(Number(e.target.value)); setProduct(null) }}>
              {locations.map(l => <option key={l.id} value={l.id}>{l.name}</option>)}
            </select>
          </label>
          <button type="button" className="btn btn-outline-secondary btn-sm" disabled={busy || !locationId}
            onClick={() => void lastReceipt()}>Last receipt</button>
        </div>
        <form onSubmit={e => { e.preventDefault(); addItem() }}>
          <fieldset className="pos-fields" disabled={!dashboard.canOperate || busy}>
            <legend className="visually-hidden">Add a product</legend>
            <label className="form-label">Product name, SKU or barcode
              <input autoFocus className="form-control" type="search" value={search} maxLength={150}
                placeholder="Search or scan barcode"
                onChange={e => { setSearch(e.target.value); setProduct(null) }} />
            </label>
            <label className="form-label">Matching product
              <select className="form-select" value={product?.id ?? ''} required
                onChange={e => setProduct(products.find(x => x.id === Number(e.target.value)) ?? null)}>
                <option value="">Choose a product</option>{products.map(x =>
                  <option key={x.id} value={x.id}>{x.name} · {x.sku} · {money(x.salePrice)}</option>)}
              </select>
            </label>
            {hint && <p className="pos-field-status" role="status">{hint}</p>}
            {product && <p className="pos-field-status">Available at this location: <strong>{product.available}</strong></p>}
            <div className="pos-add-row">
              <label className="form-label">Quantity
                <input ref={qtyInput} className="form-control" type="number" min="1" step="1" required
                  value={quantity} onChange={e => setQuantity(e.target.value)} />
              </label>
              <button type="submit" className="btn btn-outline-primary" disabled={!product}>Add to basket</button>
            </div>
          </fieldset>
        </form>
      </section>
      <section className="content-section pos-basket" aria-label="Sale basket and cash payment">
        <div className="pos-panel-heading"><h2>Current basket</h2><span>{basket.length} products</span></div>
        {!basket.length && <p className="pos-basket-empty">No products added yet. Search for a product to begin this sale.</p>}
        {basket.length > 0 && <ul className="pos-basket-lines">{basket.map(line =>
          <li key={line.productId} className="pos-basket-line">
            <div className="pos-line-description">
              <strong>{line.name}</strong>
              <small>{money(line.expectedUnitPrice)} each · {money(line.expectedUnitPrice * line.quantity)}</small>
            </div>
            <label className="pos-line-quantity">Qty
              <input className="form-control form-control-sm" aria-label={'Quantity of ' + line.name}
                type="number" min="1" max="1000000" step="1" disabled={busy}
                value={line.quantity || ''} onChange={e => {
                  const count = Number(e.target.value)
                  if (Number.isSafeInteger(count) && count >= 0 && count <= 1000000)
                    setBasket(basket.map(x => x.productId === line.productId ? { ...x, quantity: count } : x))
                }} />
            </label>
            <button type="button" className="btn btn-link btn-sm" disabled={busy}
              aria-label={'Remove ' + line.name}
              onClick={() => setBasket(basket.filter(x => x.productId !== line.productId))}>Remove</button>
          </li>)}</ul>}
        <form className="pos-payment" onSubmit={e => { e.preventDefault(); void recordSale() }}>
          <fieldset className="pos-fields" disabled={busy || !dashboard.canOperate || !basket.length}>
            <legend className="visually-hidden">Cash payment</legend>
            <div className="pos-total-row"><span>Amount to collect</span><strong>{money(total)}</strong></div>
            <label className="form-label">Cash received
              <input className="form-control" type="number" min={total} step="0.01" required
                value={cash} onChange={e => setCash(e.target.value)} />
            </label>
            <div className="pos-change-row">Change to return
              <strong>{money(cash.trim() && Number.isFinite(Number(cash)) ? Math.max(0, Number(cash) - total) : 0)}</strong>
            </div>
            <button className="btn btn-primary pos-record-button" type="submit"
              disabled={!Number.isFinite(Number(cash)) || cash.trim() === '' || Number(cash) < total ||
                !Number.isFinite(total) || total <= 0 ||
                basket.some(line => !Number.isSafeInteger(line.quantity) || line.quantity < 1)}>
              {busy ? 'Recording…' : 'Record cash sale'}
            </button>
          </fieldset>
        </form>
      </section>
    </div>}
  </section>
}
