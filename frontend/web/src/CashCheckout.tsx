import { useEffect, useRef, useState } from 'react'
import { api, ApiResponseError, operationKey, post, type RetailDashboard } from './api'

interface Product {
  id: number
  name: string
  sku: string
  salePrice: number
}
interface SaleRequest {
  requestKey: string
  productId: number
  locationId: number
  quantity: number
  expectedUnitPrice: number
  tendered: number
}
interface Receipt {
  id: number
  productName: string
  quantity: number
  unitPrice: number
  total: number
  tendered: number
  currencyCode: string
  postedAtUtc: string
}

export default function CashCheckout({ userId, dashboard }: { userId: number; dashboard: RetailDashboard }) {
  const { business, locations } = dashboard
  const storageKey = `cash-checkout:${userId}:${business.id}`
  const [pending, setPending] = useState<SaleRequest | null>(() => {
    const saved = sessionStorage.getItem(storageKey)
    return saved ? JSON.parse(saved) as SaleRequest : null
  })
  const [receipt, setReceipt] = useState<Receipt | null>(null)
  const [locationId, setLocation] = useState(locations[0]?.id ?? 0)
  const [search, setSearch] = useState('')
  const [products, setProducts] = useState<Product[]>([])
  const [product, setProduct] = useState<Product | null>(null)
  const [quantity, setQuantity] = useState('1')
  const [cash, setCash] = useState('')
  const [busy, setBusy] = useState(false)
  const submitting = useRef(false)
  const [error, setError] = useState('')
  const [lookup, setLookup] = useState('')
  const base = `/businesses/${business.id}/cash-sales`
  const money = (n: number) => `${business.currencyCode} ${n.toFixed(2)}`
  const total = product ? Math.round(product.salePrice * 100) * Number(quantity) / 100 : 0

  useEffect(() => {
    if (!locationId || !dashboard.canOperate || pending || receipt) return
    let current = true
    setProducts([])
    setLookup('Finding products…')
    const timer = setTimeout(() => {
      api<Product[]>(`${base}/products?locationId=${locationId}&search=${encodeURIComponent(search)}`)
        .then(items => {
          if (!current) return
          setProducts(items)
          setLookup(items.length ? '' : 'No eligible products found.')
        })
        .catch((e: Error) => { if (current) setLookup(e.message) })
    }, 200)
    return () => { current = false; clearTimeout(timer) }
  }, [base, locationId, search, dashboard.canOperate, pending, receipt])

  async function submit() {
    if (submitting.current || (!pending && !product)) return
    const request = pending ?? { productId: product!.id, expectedUnitPrice: product!.salePrice,
      locationId, quantity: Number(quantity), tendered: Number(cash), requestKey: operationKey() }
    submitting.current = true
    setBusy(true)
    setError('')
    try {
      sessionStorage.setItem(storageKey, JSON.stringify(request))
      setPending(request)
      if (pending) {
        const query = new URLSearchParams({ locationId: String(request.locationId), requestKey: request.requestKey })
        try {
          setReceipt(await api<Receipt>(base + '/receipt?' + query))
          return
        } catch (cause) {
          if (!(cause instanceof ApiResponseError && cause.status === 404)) throw cause
        }
      }
      const posted = await post<Receipt>(base, request)
      setReceipt(posted)
      // Keep the key until the cashier starts a new sale. Refresh can recover this receipt.
    } catch (cause) {
      setError(cause instanceof Error ? cause.message : 'Unable to confirm sale.')
      if (cause instanceof ApiResponseError && [400, 403].includes(cause.status)) {
        sessionStorage.removeItem(storageKey)
        setPending(null)
        setProduct(null)
      }
    } finally {
      setBusy(false)
      submitting.current = false
    }
  }

  if (receipt) return <section className="content-section cash-receipt" aria-label="Cash receipt">
    <h2>Cash receipt #{receipt.id}</h2>
    <p>{business.name} · {new Date(receipt.postedAtUtc).toLocaleString(undefined, { timeZone: business.timeZoneId })}</p>
    <p>{receipt.productName} · {receipt.quantity} × {money(receipt.unitPrice)}</p>
    <p><strong>Total {money(receipt.total)}</strong><br />Cash received {money(receipt.tendered)}<br />
      Change {money(receipt.tendered - receipt.total)}</p>
    <button className="btn btn-outline-secondary me-2" onClick={() => window.print()}>Print receipt</button>
    <button className="btn btn-primary" onClick={() => {
      sessionStorage.removeItem(storageKey)
      setReceipt(null)
      setPending(null)
      setProduct(null)
      setQuantity('1')
      setCash('')
      setSearch('')
    }}>New sale</button>
  </section>

  return <section className="content-section" aria-label="Single-product cash sale">
    {error && <p role="alert" className="alert alert-danger">{error}</p>}
    {pending ? <>
      <p role="status">Confirm this sale or recover its receipt using the same request.</p>
      <button className="btn btn-primary" disabled={busy} onClick={() => void submit()}>
        {busy ? 'Confirming…' : 'Confirm or recover receipt'}</button>
    </> : <form onSubmit={event => { event.preventDefault(); void submit() }}>
      <fieldset className="d-grid gap-2" disabled={!dashboard.canOperate || busy}>
        <legend>Single-product cash sale</legend>
        <label>Location<select className="form-select" value={locationId} onChange={e => {
          setLocation(Number(e.target.value)); setProduct(null)
        }}>{locations.map(l => <option key={l.id} value={l.id}>{l.name}</option>)}</select></label>
        <label>Find by name, SKU or barcode<input className="form-control" maxLength={150} value={search}
          onChange={e => { setSearch(e.target.value); setProduct(null) }} /></label>
        <label>Product<select className="form-select" required value={product?.id ?? ''}
          onChange={e => setProduct(products.find(p => p.id === Number(e.target.value)) ?? null)}>
          <option value="">Select a product</option>
          {products.map(p => <option key={p.id} value={p.id}>{p.name} · {p.sku} · {money(p.salePrice)}</option>)}
        </select></label>
        {lookup && <p role="status">{lookup}</p>}
        <label>Quantity<input className="form-control" type="number" min="1" max="1000000" step="1"
          required value={quantity} onChange={e => setQuantity(e.target.value)} /></label>
        <strong>Total {money(total)}</strong>
        <label>Cash received<input className="form-control" type="number" min={total} step="0.01"
          required value={cash} onChange={e => setCash(e.target.value)} /></label>
        <span>Change {money(Math.max(0, Number(cash) - total))}</span>
        <button className="btn btn-primary" disabled={!product || !locationId || !total || Number(cash) < total}>
          Record cash sale</button>
      </fieldset>
    </form>}
  </section>
}
