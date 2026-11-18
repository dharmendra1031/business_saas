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
  const raw = sessionStorage.getItem(key)
  if (!raw) return null
  const saved = JSON.parse(raw)
  if (Array.isArray(saved.lines)) return saved
  // Recover a single-product checkout started before baskets were introduced.
  return { ...saved, lines: [{ productId: saved.productId, name: 'Product #' + saved.productId,
    quantity: saved.quantity, expectedUnitPrice: saved.expectedUnitPrice }] }
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
    if (!Number.isSafeInteger(count) || count < 1 || combined > Math.min(product.available, 1000000)) {
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
    if (lock.current || basket.some(line => line.quantity < 1) || !dashboard.canOperate || (pending && lookupState !== 'missing')) return
    const request = pending ?? { locationId, lines: basket, tendered: Number(cash), requestKey: operationKey() }
    lock.current = true
    setBusy(true)
    setError('')
    try {
      sessionStorage.setItem(key, JSON.stringify(request))
      setPending(request)
      setReceipt(await post<Receipt>(base, request))
    } catch (cause) {
      setError(cause instanceof Error ? cause.message : 'Unable to confirm sale.')
      if (cause instanceof ApiResponseError && [400, 403].includes(cause.status)) {
        sessionStorage.removeItem(key)
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
        sessionStorage.removeItem(key)
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

  return <section className="content-section" aria-label="Cash checkout">
    {error && <p role="alert" className="alert alert-danger">{error}</p>}
    {pending ? <>
      <h2>Pending cash sale</h2>
      <ul>{pending.lines.map(line => <li key={line.productId}>{line.name} · {line.quantity} × {money(line.expectedUnitPrice)}</li>)}</ul>
      <p>Total {money(total)} · Cash received {money(pending.tendered)}</p>
      <p role="status">{busy ? 'Recording sale…' : lookupState === 'missing' ? 'No posted receipt found for this sale.'
        : lookupState === 'unknown' ? 'Receipt status could not be verified.' : 'Checking for a posted receipt…'}</p>
      {lookupState === 'missing' && <button className="btn btn-primary me-2" disabled={busy || !dashboard.canOperate}
        onClick={() => void recordSale()}>Record this sale</button>}
      <button className="btn btn-outline-secondary" disabled={busy || lookupState === 'checking'}
        onClick={() => { setLookupState('checking'); setRefresh(n => n + 1) }}>Check status again</button>
    </> : <>
      <label>Location<select className="form-select" value={locationId} disabled={busy || basket.length > 0}
        onChange={e => { setLocation(Number(e.target.value)); setProduct(null) }}>
        {locations.map(l => <option key={l.id} value={l.id}>{l.name}</option>)}</select></label>
      <button className="btn btn-link" disabled={busy || !locationId} onClick={() => void lastReceipt()}>Last receipt</button>
      <form onSubmit={e => { e.preventDefault(); addItem() }}>
        <fieldset className="d-grid gap-2" disabled={!dashboard.canOperate || busy}>
          <legend>Add a product</legend>
          <label>Find by name, SKU or barcode<input className="form-control" value={search} maxLength={150}
            onChange={e => { setSearch(e.target.value); setProduct(null) }} /></label>
          <label>Product<select className="form-select" value={product?.id ?? ''} required
            onChange={e => setProduct(products.find(x => x.id === Number(e.target.value)) ?? null)}>
            <option value="">Select a product</option>{products.map(x =>
              <option key={x.id} value={x.id}>{x.name} · {x.sku} · {money(x.salePrice)}</option>)}</select></label>
          {hint && <span role="status">{hint}</span>}
          {product && <span>Available at this location: {product.available}</span>}
          <label>Quantity<input ref={qtyInput} className="form-control" type="number" min="1" step="1" required
            value={quantity} onChange={e => setQuantity(e.target.value)} /></label>
          <button className="btn btn-outline-primary" disabled={!product}>Add to basket</button>
        </fieldset>
      </form>
      <h2 className="mt-3">Basket</h2>
      {!basket.length && <p>No products added.</p>}
      <ul>{basket.map(line => <li key={line.productId}>{line.name} · {money(line.expectedUnitPrice)} each
        <label className="ms-2">Quantity <input aria-label={'Quantity of ' + line.name} type="number" min="1" max="1000000"
          step="1" disabled={busy} value={line.quantity || ''} onChange={e => {
            const count = Number(e.target.value)
            if (Number.isSafeInteger(count) && count >= 0 && count <= 1000000)
              setBasket(basket.map(x => x.productId === line.productId ? { ...x, quantity: count } : x))
          }} /></label>
        <button className="btn btn-link" disabled={busy} aria-label={'Remove ' + line.name}
          onClick={() => setBasket(basket.filter(x => x.productId !== line.productId))}>Remove</button></li>)}</ul>
      <form onSubmit={e => { e.preventDefault(); void recordSale() }}>
        <fieldset className="d-grid gap-2" disabled={busy || !dashboard.canOperate || !basket.length}>
          <strong>Total {money(total)}</strong>
          <label>Cash received<input className="form-control" type="number" min={total} step="0.01"
            required value={cash} onChange={e => setCash(e.target.value)} /></label>
          <span>Change {money(Math.max(0, Number(cash) - total))}</span>
          <button className="btn btn-primary" disabled={Number(cash) < total || basket.some(line => line.quantity < 1)}>Record cash sale</button>
        </fieldset>
      </form>
    </>}
  </section>
}
