import { useEffect, useRef, useState, type FormEvent } from 'react'
import { ArrowRight, Banknote, Check, Minus, Package, Plus, ReceiptText, Search, ShoppingBag, Trash2 } from 'lucide-react'
import PosCashPaymentDialog from './PosCashPaymentDialog'
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
  const [showPayment, setShowPayment] = useState(false)
  const [clearArmed, setClearArmed] = useState(false)
  const [scanBusy, setScanBusy] = useState(false)
  const [cash, setCash] = useState(pending ? String(pending.tendered) : '')
  const [error, setError] = useState('')
  const [hint, setHint] = useState('')
  const [busy, setBusy] = useState(false)
  const lock = useRef(false)
  const searchInput = useRef<HTMLInputElement>(null)
  const payButtonRef = useRef<HTMLButtonElement>(null)
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
          // Product cards and barcode entry use the same verified product collection.
        }).catch(cause => { if (active) setHint(cause.message) })
    }, 200)
    return () => { active = false; clearTimeout(timer) }
  }, [base, search, locationId, dashboard.canOperate, pending, receipt])

  function addProduct(item: Product) {
    if (!dashboard.canOperate || busy || pending || receipt) return
    const existing = basket.find(line => line.productId === item.id)
    const nextQty = (existing?.quantity ?? 0) + 1
    if (!Number.isSafeInteger(nextQty) || nextQty > Math.min(item.available, 1000000)) {
      setError('Insufficient available stock for this product.')
      return
    }
    if (!existing && basket.length >= 100) {
      setError('A receipt supports up to 100 products.')
      return
    }
    const line = { productId: item.id, name: item.name, quantity: nextQty, expectedUnitPrice: item.salePrice }
    setBasket(existing ? basket.map(previous => previous.productId === item.id ? line : previous) : [...basket, line])
    setCash('')
    setError('')
    setClearArmed(false)
    setSearch('')
    searchInput.current?.focus()
  }

  async function submitBarcode(event: FormEvent) {
    event.preventDefault()
    const code = search.trim().toUpperCase()
    if (!code || scanBusy || busy || !dashboard.canOperate || !locationId) return
    setScanBusy(true)
    try {
      const matching = products.find(x => x.barcode?.toUpperCase() === code || x.sku.toUpperCase() === code) ??
        (await api<Product[]>(`${base}/products?locationId=${locationId}&search=${encodeURIComponent(code)}`))
          .find(x => x.barcode?.toUpperCase() === code || x.sku.toUpperCase() === code)
      if (matching) addProduct(matching)
      else setHint('No exact barcode or SKU match. Select a product card from the results.')
    } catch (cause) {
      setHint(cause instanceof Error ? cause.message : 'Unable to verify barcode.')
    } finally { setScanBusy(false) }
  }

  function updateQuantity(line: Line, quantity: number) {
    if (!Number.isSafeInteger(quantity) || quantity < 1 || quantity > 1000000) return
    const knownStock = products.find(x => x.id === line.productId)?.available
    if (knownStock !== undefined && quantity > knownStock) {
      setError('Quantity is greater than available stock.')
      return
    }
    setBasket(previous => previous.map(x => x.productId === line.productId ? { ...x, quantity } : x))
    setCash('')
    setError('')
  }

  const itemCount = basket.reduce((sum, line) => sum + line.quantity, 0)

  async function recordSale() {
    const received = Number(cash)
    if (lock.current || !dashboard.canOperate || (pending && lookupState !== 'missing')) return
    if (!pending && (!locationId || !basket.length || basket.some(line =>
      !Number.isSafeInteger(line.quantity) || line.quantity < 1 ||
      !Number.isFinite(line.expectedUnitPrice) || line.expectedUnitPrice < 0) ||
      !Number.isFinite(total) || total <= 0 || !Number.isFinite(received) ||
      Math.abs(Math.round(received * 100) - received * 100) > 0.000001 || received < total)) {
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
      setSearch('')
      setShowPayment(false)
    }}>{pending ? 'New sale' : 'Back to checkout'}</button>
  </section>

  return <section className="pos-checkout" aria-label="Point of Sale checkout">
    <header className="pos-header">
      <div>
        <p className="pos-kicker">RETAIL · POINT OF SALE</p>
        <h2>{pending ? 'Recover Pending Sale' : 'New Sale'}</h2>
        <p>{pending ? 'Check the previous transaction before recording it again.' :
          'Select products, review your basket and collect cash payment.'}</p>
      </div>
      <span className="pos-mode"><Check size={15} /> Cash checkout · {business.currencyCode}</span>
    </header>
    {error && <div role="alert" className="alert alert-danger">{error}</div>}
    {pending ? <section className="content-section pos-pending" aria-label="Pending sale recovery">
      <div className="pos-panel-heading"><h2>Pending cash sale</h2><span>Reference held for safe retry</span></div>
      <ul>{pending.lines.map(line => <li key={line.productId}>{line.name} · {line.quantity} × {money(line.expectedUnitPrice)}</li>)}</ul>
      <p><strong>Total {money(total)}</strong> · Cash received {money(pending.tendered)}</p>
      <p role="status">{busy ? 'Recording sale…' : lookupState === 'missing' ? 'No posted receipt found for this sale.'
        : lookupState === 'unknown' ? 'Receipt status could not be verified. Do not create another sale.' : 'Checking for the posted receipt…'}</p>
      {lookupState === 'missing' && <button type="button" className="btn btn-primary me-2"
        disabled={busy || !dashboard.canOperate} onClick={() => void recordSale()}>Record original request</button>}
      <button type="button" className="btn btn-outline-secondary" disabled={busy || lookupState === 'checking'}
        onClick={() => { setLookupState('checking'); setRefresh(n => n + 1) }}>Check status again</button>
    </section> : <div className="pos-grid">
      <section className="content-section pos-catalog" aria-label="Product selection">
        <div className="pos-panel-heading">
          <div><h2>Products</h2><small>Tap a product to add it to this sale</small></div>
          <span className="pos-count-pill">{products.length} shown</span>
        </div>
        <div className="pos-location-row">
          <label className="form-label" htmlFor="pos-location">Store location
            <select id="pos-location" className="form-select" value={locationId}
              disabled={busy || basket.length > 0} onChange={event => setLocation(Number(event.target.value))}>
              {locations.map(location => <option key={location.id} value={location.id}>{location.name}</option>)}
            </select>
          </label>
          <button type="button" className="btn btn-outline-secondary btn-sm" disabled={busy || !locationId}
            onClick={() => void lastReceipt()}><ReceiptText size={16} /> Last Receipt</button>
        </div>
        <form className="pos-search" role="search" onSubmit={event => void submitBarcode(event)}>
          <label className="visually-hidden" htmlFor="pos-product-search">Search product or scan barcode</label>
          <Search size={19} aria-hidden="true" />
          <input ref={searchInput} id="pos-product-search" autoFocus className="form-control" type="search"
            placeholder="Search product, SKU or scan barcode…" maxLength={150} value={search}
            disabled={!dashboard.canOperate || busy} onChange={event => setSearch(event.target.value)} />
          <button type="submit" className="btn btn-outline-primary btn-sm"
            disabled={!search.trim() || scanBusy || busy || !dashboard.canOperate} aria-label="Add exact barcode or SKU">
            {scanBusy ? 'Checking…' : 'Scan / Enter'}
          </button>
        </form>
        {hint && <p className="pos-field-status" role="status">{hint}</p>}
        {!dashboard.canOperate && <p className="alert alert-warning" role="status">
          Your business is not enabled for checkout. Product selection is read-only.
        </p>}
        <div className="pos-catalog-results">
          {products.length ? <div className="pos-products-grid">
            {products.map(item => {
              const inBasket = basket.find(line => line.productId === item.id)?.quantity ?? 0
              const canAdd = dashboard.canOperate && !busy && item.available > inBasket &&
                (inBasket > 0 || basket.length < 100)
              return <button key={item.id} type="button" className="pos-product-card" disabled={!canAdd}
                aria-label={`Add ${item.name}, ${money(item.salePrice)}, ${item.available} available`}
                onClick={() => addProduct(item)}>
                <span className="pos-product-visual"><Package size={33} aria-hidden="true" />
                  {inBasket > 0 && <span className="pos-product-in-cart">{inBasket} in cart</span>}
                </span>
                <span className="pos-product-meta">
                  <strong>{item.name}</strong>
                  <small>{item.sku}</small>
                  <span className="pos-product-bottom"><b>{money(item.salePrice)}</b>
                    <span>{item.available > 0 ? `${item.available} in stock` : 'Out of stock'}</span></span>
                </span>
                <span className="pos-product-plus" aria-hidden="true"><Plus size={15} /></span>
              </button>
            })}
          </div> : <div className="pos-products-empty">
            <Package size={30} /><strong>No products to display</strong>
            <p>{hint || 'Only available products for the selected location can be sold.'}</p>
          </div>}
        </div>
        <p className="pos-catalog-note">Product image and category filters require verified catalogue data; no sample images are shown.</p>
      </section>
      <section className="content-section pos-basket" aria-label="Current sale basket">
        <div className="pos-panel-heading">
          <div><h2>Current Sale</h2><small>Cash payment · {business.currencyCode}</small></div>
          <span className="pos-count-pill">{itemCount} items</span>
        </div>
        <div className="pos-basket-toolbar">
          <span><ShoppingBag size={16} /> {basket.length} {basket.length === 1 ? 'product' : 'products'}</span>
          {basket.length > 0 && !clearArmed && <button type="button" className="pos-basket-clear"
            disabled={busy} onClick={() => setClearArmed(true)}><Trash2 size={15} /> Clear</button>}
          {clearArmed && <div className="pos-clear-confirm">
            <span>Clear this basket?</span>
            <button type="button" disabled={busy} onClick={() => { setBasket([]); setClearArmed(false); setCash('') }}>Yes, clear</button>
            <button type="button" onClick={() => setClearArmed(false)}>Cancel</button>
          </div>}
        </div>
        {!basket.length && <div className="pos-basket-empty">
          <ShoppingBag size={34} /><strong>Your basket is empty</strong>
          <p>Select a product from the catalogue to start this sale.</p>
        </div>}
        {basket.length > 0 && <ul className="pos-basket-lines">{basket.map(line =>
          <li key={line.productId} className="pos-basket-line">
            <div className="pos-line-description">
              <strong>{line.name}</strong>
              <small>{money(line.expectedUnitPrice)} each</small>
              <b>{money(Math.round(line.expectedUnitPrice * 100) * line.quantity / 100)}</b>
            </div>
            <div className="pos-quantity-control" aria-label={`Quantity of ${line.name}`}>
              <button type="button" aria-label={`Decrease quantity of ${line.name}`}
                disabled={busy || line.quantity <= 1}
                onClick={() => updateQuantity(line, line.quantity - 1)}><Minus size={15} /></button>
              <input className="form-control form-control-sm" aria-label={`Quantity of ${line.name}`}
                type="number" min="1" max="1000000" step="1" disabled={busy} value={line.quantity}
                onChange={event => updateQuantity(line, Number(event.target.value))} />
              <button type="button" aria-label={`Increase quantity of ${line.name}`}
                disabled={busy || line.quantity >= 1000000}
                onClick={() => updateQuantity(line, line.quantity + 1)}><Plus size={15} /></button>
            </div>
            <button type="button" className="pos-remove-item" disabled={busy}
              aria-label={`Remove ${line.name}`} onClick={() => {
                setBasket(previous => previous.filter(x => x.productId !== line.productId))
                setCash('')
                setClearArmed(false)
              }}><Trash2 size={16} /></button>
          </li>)}</ul>}
        <div className="pos-basket-checkout">
          <div className="pos-subtotal-row"><span>Subtotal</span><strong>{money(total)}</strong></div>
          <div className="pos-total-row"><span>Total Payable</span><strong>{money(total)}</strong></div>
          <p className="pos-no-discount-note">Additional discounts and payment methods are not enabled for this checkout.</p>
          <button ref={payButtonRef} type="button" className="btn btn-primary pos-record-button"
            disabled={!dashboard.canOperate || busy || !basket.length || !Number.isFinite(total) || total <= 0 ||
              basket.some(line => !Number.isSafeInteger(line.quantity) || line.quantity < 1)}
            onClick={() => { setError(''); setShowPayment(true) }}>
            <Banknote size={19} /> Pay / Checkout <ArrowRight size={18} />
          </button>
        </div>
      </section>
    </div>}
    {showPayment && !pending && !receipt && <PosCashPaymentDialog amount={total}
      cash={cash} currency={business.currencyCode} lineCount={itemCount} busy={busy}
      onCashChange={setCash} onCancel={() => setShowPayment(false)}
      onConfirm={() => void recordSale()} />}
  </section>
}