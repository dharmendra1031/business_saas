import { useEffect, useRef, useState, type FormEvent } from 'react'
import { ArrowLeft, ArrowRight, Banknote, Check, Minus, Package, Plus, ReceiptText, Search, ShoppingBag, Trash2 } from 'lucide-react'
import PosCashPaymentDialog from './PosCashPaymentDialog'
import CashReceipt, { type Receipt } from './CashReceipt'
import { api, ApiResponseError, operationKey, post, type RetailDashboard } from './api'

interface Product {
  id: number; name: string; sku: string; barcode: string | null; salePrice: number; available: number;
  unitCode?: string | null
}
interface Line { productId: number; name: string; quantity: number; expectedUnitPrice: number;
  unitCode?: string | null; available?: number }
interface SaleRequest { locationId: number; requestKey: string; tendered: number; lines: Line[]; customerId?: number | null }
interface SaleCustomer { id: number; name: string; phone: string }
interface ReceiptHistory {
  items: Pick<Receipt, 'id' | 'total' | 'currencyCode' | 'postedAtUtc'>[]
  hasMore: boolean
}

function validQuantity(value: number): boolean {
  return Number.isFinite(value) && value >= 0.001 && value <= 1000000 &&
    Math.abs(Math.round(value * 1000) - value * 1000) < 0.000001
}

// Keep integer cents within a range where conversion back to a number retains
// cent precision. SQL decimal supports larger values than the browser can display.
const maxExactCents = 2 ** 52 - 1

function lineAmountCents(line: Line): number {
  const cents = Math.round(line.expectedUnitPrice * 100)
  if (!validQuantity(line.quantity) || !Number.isSafeInteger(cents) || cents < 0 ||
    Math.abs(cents - line.expectedUnitPrice * 100) > 0.000001) return NaN
  // Quantity has at most three decimals. Integer arithmetic matches the server's
  // positive decimal midpoint rounding without a floating-point half-cent error.
  const amount = (BigInt(cents) * BigInt(Math.round(line.quantity * 1000)) + 500n) / 1000n
  return amount <= BigInt(maxExactCents) ? Number(amount) : NaN
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
      typeof line.name !== 'string' || !validQuantity(line.quantity) ||
      !Number.isFinite(line.expectedUnitPrice) || line.expectedUnitPrice < 0 ||
      Math.round(line.expectedUnitPrice * 100) !== line.expectedUnitPrice * 100 &&
      Math.abs(Math.round(line.expectedUnitPrice * 100) - line.expectedUnitPrice * 100) > 0.000001))
      throw new Error('Invalid checkout lines')
    if (saved.customerId != null && (!Number.isSafeInteger(saved.customerId) || saved.customerId < 1)) throw new Error('Invalid checkout customer')
    return { locationId: saved.locationId, requestKey: saved.requestKey, tendered: saved.tendered, lines, customerId: saved.customerId ?? null }
  } catch {
    try { sessionStorage.removeItem(key) } catch { /* Storage may be unavailable. */ }
    return null
  }
}

export default function CashCheckout({ userId, dashboard, onExit }: {
  userId: number; dashboard: RetailDashboard; onExit: () => void
}) {
  const { business, locations } = dashboard
  const cashAvailable = dashboard.cashPaymentEnabled !== false
  const key = `cash-checkout:${userId}:${business.id}`
  const base = `/businesses/${business.id}/cash-sales`
  const [pending, setPending] = useState(() => restore(key))
  const [lookupState, setLookupState] = useState<'checking' | 'missing' | 'unknown'>('checking')
  const [refresh, setRefresh] = useState(0)
  const [receipt, setReceipt] = useState<Receipt | null>(null)
  const [printNotice, setPrintNotice] = useState('')
  const [locationId, setLocation] = useState(pending?.locationId ?? locations[0]?.id ?? 0)
  const [customer, setCustomer] = useState<SaleCustomer | null>(() => pending?.customerId
    ? { id: pending.customerId, name: `Customer #${pending.customerId}`, phone: '' } : null)
  const [customerOpen, setCustomerOpen] = useState(false)
  const [customerSearch, setCustomerSearch] = useState('')
  const [customerResults, setCustomerResults] = useState<SaleCustomer[]>([])
  const [customerStatus, setCustomerStatus] = useState('')
  const [basket, setBasket] = useState<Line[]>(pending?.lines ?? [])
  const [reviewProductId, setReviewProductId] = useState<number | null>(null)
  const [receiptNumber, setReceiptNumber] = useState('')
  const [historyPage, setHistoryPage] = useState(0)
  const [history, setHistory] = useState<ReceiptHistory | null>(null)
  const [historyError, setHistoryError] = useState(false)
  const [historyRetry, setHistoryRetry] = useState(0)
  const validReceiptNumber = /^\d{1,19}$/.test(receiptNumber.trim()) && /[1-9]/.test(receiptNumber)
  const needsReview = basket.some(line => line.productId === reviewProductId)
  const [search, setSearch] = useState('')
  const [products, setProducts] = useState<Product[]>([])
  const [productsLoading, setProductsLoading] = useState(false)
  const [showPayment, setShowPayment] = useState(false)
  const [clearArmed, setClearArmed] = useState(false)
  const [exitArmed, setExitArmed] = useState(false)
  const [scanBusy, setScanBusy] = useState(false)
  const [cash, setCash] = useState(pending ? String(pending.tendered) : '')
  const [error, setError] = useState('')
  const [hint, setHint] = useState('')
  const [busy, setBusy] = useState(false)
  const lock = useRef(false)
  const scanLock = useRef(false)
  const searchInput = useRef<HTMLInputElement>(null)
  const customerInput = useRef<HTMLInputElement>(null)
  const checkoutButton = useRef<HTMLButtonElement>(null)
  const lineAmounts = basket.map(lineAmountCents)
  const totalCents = lineAmounts.reduce((sum, amount) => sum + amount, 0)
  const hasUnsupportedAmount = !Number.isSafeInteger(totalCents) || totalCents > maxExactCents
  const total = hasUnsupportedAmount ? NaN : totalCents / 100
  const hasZeroValueLine = lineAmounts.some(amount => amount === 0)
  const money = (amount: number) => Number.isFinite(amount)
    ? (business.currencyCode === 'INR' ? '₹' : business.currencyCode + ' ') + amount.toFixed(2) : '—'

  useEffect(() => {
    if (!historyPage || !locationId || pending || receipt) return
    let active = true
    setHistory(null)
    setHistoryError(false)
    api<ReceiptHistory>(`${base}/receipts?locationId=${locationId}&page=${historyPage}`)
      .then(result => { if (active) setHistory(result) })
      .catch(() => { if (active) setHistoryError(true) })
    return () => { active = false }
  }, [base, historyPage, historyRetry, locationId, pending, receipt])

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
    if (!locationId || !dashboard.canOperate || pending || receipt || scanBusy || historyPage) return
    let active = true
    const controller = new AbortController()
    setProducts([])
    setProductsLoading(true)
    setHint('')
    const timer = setTimeout(() => {
      api<Product[]>(`${base}/products?locationId=${locationId}&search=${encodeURIComponent(search)}`,
        { signal: controller.signal })
        .then(items => {
          if (!active) return
          setProducts(items)
          setProductsLoading(false)
          setHint(items.length ? '' : 'No eligible products found.')
        }).catch(cause => { if (active) { setProductsLoading(false); setHint(cause instanceof Error ? cause.message : 'Unable to load products.') } })
    }, 200)
    return () => { active = false; clearTimeout(timer); controller.abort() }
  }, [base, search, locationId, dashboard.canOperate, pending, receipt, scanBusy, historyPage])

  useEffect(() => {
    if (!customerOpen || pending || receipt || !locationId || !dashboard.canOperate) return
    let active = true
    setCustomerResults([])
    const term = customerSearch.trim()
    if (term.length < 2) { setCustomerStatus('Enter at least two characters.'); return }
    setCustomerStatus('Searching customers…')
    const timer = setTimeout(() => {
      api<SaleCustomer[]>(base + '/customers?' + new URLSearchParams({ locationId: String(locationId), search: term }))
        .then(result => {
          if (active) { setCustomerResults(result); setCustomerStatus(result.length ? '' : 'No matching active customer.') }
        }).catch(() => { if (active) setCustomerStatus('Unable to search customers. Edit the search to retry.') })
    }, 200)
    return () => { active = false; clearTimeout(timer) }
  }, [base, customerOpen, customerSearch, locationId, pending, receipt, dashboard.canOperate])

  useEffect(() => {
    if (!dashboard.canOperate || !cashAvailable || busy || scanBusy || pending ||
      receipt || showPayment || clearArmed || exitArmed) return
    function onKeyDown(event: KeyboardEvent) {
      if (event.defaultPrevented || event.repeat || event.isComposing ||
        event.ctrlKey || event.altKey || event.metaKey || event.shiftKey ||
        lock.current || scanLock.current) return
      if (event.key === 'F2') {
        event.preventDefault()
        if (historyPage) setHistoryPage(0)
        else {
          searchInput.current?.focus()
          searchInput.current?.select()
        }
      } else if (event.key === 'F4') {
        event.preventDefault()
        if (customerOpen) customerInput.current?.focus()
        else {
          setCustomerSearch('')
          setCustomerResults([])
          setCustomerOpen(true)
        }
      } else if (event.key === 'F9') {
        const button = checkoutButton.current
        if (!button || button.disabled) return
        event.preventDefault()
        // Reuse the normal checkout action, including its validation and focus return.
        button.focus()
        button.click()
      }
    }
    document.addEventListener('keydown', onKeyDown)
    return () => document.removeEventListener('keydown', onKeyDown)
  }, [dashboard.canOperate, cashAvailable, busy, scanBusy, pending, receipt,
    showPayment, clearArmed, exitArmed, historyPage, customerOpen])

  function addProduct(item: Product, fromScan = false) {
    if (!dashboard.canOperate || !cashAvailable || lock.current || busy || pending || receipt ||
      (scanLock.current && !fromScan)) return
    const existing = basket.find(line => line.productId === item.id)
    if (item.available - (existing?.quantity ?? 0) < 0.001 - 0.0000001) {
      setError('Insufficient available stock for this product.')
      return
    }
    const nextQty = Number(((existing?.quantity ?? 0) +
      Math.min(1, item.available - (existing?.quantity ?? 0))).toFixed(3))
    if (!validQuantity(nextQty) || nextQty > item.available) {
      setError('Insufficient available stock for this product.')
      return
    }
    if (!existing && basket.length >= 100) {
      setError('A receipt supports up to 100 products.')
      return
    }
    const line = { productId: item.id, name: item.name, quantity: nextQty,
      expectedUnitPrice: item.salePrice, unitCode: item.unitCode, available: item.available }
    setBasket(existing ? basket.map(previous => previous.productId === item.id ? line : previous) : [...basket, line])
    setCash('')
    setError('')
    setClearArmed(false)
    setExitArmed(false)
    setSearch('')
    searchInput.current?.focus()
  }

  async function submitBarcode(event: FormEvent) {
    event.preventDefault()
    const code = search.trim().toUpperCase()
    if (!code || scanLock.current || lock.current || busy || pending || receipt ||
      !dashboard.canOperate || !cashAvailable || !locationId) return
    scanLock.current = true
    setScanBusy(true)
    setError('')
    try {
      // The catalogue may contain stale or partial search results. Verify the exact code
      // at the current location, and never silently choose between two matching products.
      const matching = (await api<Product[]>(`${base}/products?locationId=${locationId}&search=${encodeURIComponent(code)}&exact=true`))
        .filter(x => x.barcode?.toUpperCase() === code || x.sku.toUpperCase() === code)
      if (matching.length === 1) addProduct(matching[0], true)
      else if (matching.length > 1) setError('This code matches multiple products. Select the correct product card.')
      else setError('No exact barcode or SKU match. Select a product card from the results.')
    } catch (cause) {
      setError(cause instanceof Error ? cause.message : 'Unable to verify barcode.')
    } finally { scanLock.current = false; setScanBusy(false) }
  }

  // A filtered product search must not erase the stock limit for items in the basket.
  // The server still rechecks the real balance atomically when posting a sale.
  function availableStock(line: Line): number | undefined {
    return products.find(x => x.id === line.productId)?.available ?? line.available
  }

  function updateQuantity(line: Line, quantity: number) {
    if (scanLock.current || !validQuantity(quantity)) return
    const knownStock = availableStock(line)
    if (knownStock !== undefined && quantity > knownStock) {
      setError(`Only ${knownStock} ${line.unitCode ?? 'units'} available at this location.`)
      return
    }
    setBasket(previous => previous.map(x => x.productId === line.productId ? { ...x, quantity } : x))
    setCash('')
    setError('')
  }

  function stepQuantity(line: Line, direction: -1 | 1) {
    const step = line.quantity >= 1 ? 1 : line.quantity >= 0.1 ? 0.1 :
      line.quantity >= 0.01 ? 0.01 : 0.001
    const maximum = Math.min(1000000, availableStock(line) ?? 1000000)
    const next = Number((direction > 0 ?
      Math.min(maximum, line.quantity + step) :
      Math.max(0.001, line.quantity - step)).toFixed(3))
    if (next !== line.quantity) updateQuantity(line, next)
  }

  async function recordSale(openPrint = false) {
    const received = Number(cash)
    if (lock.current || scanLock.current || !dashboard.canOperate || needsReview || (!pending && !cashAvailable) ||
      (pending && lookupState !== 'missing')) return
    if (!pending && (!locationId || !basket.length || basket.some(line =>
      !validQuantity(line.quantity) ||
      !Number.isFinite(line.expectedUnitPrice) || line.expectedUnitPrice < 0) ||
      hasZeroValueLine || !Number.isFinite(total) || total <= 0 || !Number.isFinite(received) ||
      Math.abs(Math.round(received * 100) - received * 100) > 0.000001 || received < total)) {
      setError('Check basket quantities and enter enough cash before recording the sale.')
      return
    }
    const request = pending ?? { locationId, lines: basket, tendered: received, requestKey: operationKey(), customerId: customer?.id ?? null }
    lock.current = true
    setBusy(true)
    setLookupState('checking')
    setError('')
    setPrintNotice('')
    // Open during the cashier's click, before the async POST can lose user activation.
    let printTab: Window | null = null
    const closePrintTab = () => {
      try { printTab?.close() } catch { /* The sale outcome is independent of the PDF tab. */ }
      printTab = null
    }
    if (openPrint) {
      try {
        printTab = window.open('about:blank', '_blank')
        if (printTab) {
          printTab.opener = null
          printTab.document.title = 'Preparing receipt'
          printTab.document.body.textContent = 'Saving sale and preparing receipt…'
        }
      } catch { closePrintTab() }
    }
    let posted: Receipt
    try {
      try { sessionStorage.setItem(key, JSON.stringify(request)) } catch { /* Keep the request key in memory. */ }
      setPending(request)
      posted = await post<Receipt>(base, request)
    } catch (cause) {
      closePrintTab()
      setError(cause instanceof Error ? cause.message : 'Unable to confirm sale.')
      // Verified basket errors can be corrected after recovery; ambiguous outcomes
      // and access failures keep the original key available for receipt lookup.
      const editableRejection = cause instanceof ApiResponseError && (
        (cause.status === 400 &&
          (cause.code === 'CASH_SALE_BASKET_CHANGED' || cause.code === 'CASH_SALE_ZERO_VALUE_LINE' || cause.code === 'CASH_SALE_CUSTOMER_CHANGED')) ||
        (cause.status === 409 && cause.code === 'CASH_SALES_DISABLED'))
      const initialRejection = !pending && cause instanceof ApiResponseError &&
        [400, 403].includes(cause.status)
      if (initialRejection || editableRejection) {
        try { sessionStorage.removeItem(key) } catch { /* Storage may be unavailable. */ }
        setPending(null)
        if (editableRejection) {
          if (cause.code === 'CASH_SALE_BASKET_CHANGED') {
            setReviewProductId(cause.productId ?? null)
            setProducts([])
            setSearch('')
          }
          if (cause.code === 'CASH_SALE_CUSTOMER_CHANGED') {
            setCustomer(null); setCustomerOpen(true); setCustomerSearch('')
          }
          setShowPayment(false)
          setCash('')
        }
      }
      return
    } finally { lock.current = false; setBusy(false) }

    // The sale is committed. No print/window error may enter sale recovery.
    setReceipt(posted)
    if (!openPrint) return
    if (!posted.printAvailable) {
      closePrintTab()
      setPrintNotice('Sale saved. RDLC printing is unavailable on this host.')
      return
    }
    try {
      if (!printTab || printTab.closed) throw new Error('Print tab unavailable')
      printTab.location.replace(`/api${base}/receipts/${posted.id}/pdf?locationId=${request.locationId}`)
    } catch {
      closePrintTab()
      setPrintNotice('Sale saved. Use Print receipt to open the PDF.')
    }
  }

  async function lookupReceipt(receiptId?: string) {
    if (lock.current || scanLock.current || !locationId || pending) return
    lock.current = true
    setBusy(true)
    setError('')
    const path = receiptId ? '/receipts/' + receiptId : '/latest'
    setPrintNotice('')
    try { setReceipt(await api<Receipt>(base + path + '?locationId=' + locationId)) }
    catch (cause) {
      setError(cause instanceof ApiResponseError && cause.status === 404
        ? 'No matching receipt at this location for your account.' : 'Unable to load the receipt.')
    } finally { lock.current = false; setBusy(false) }
  }

  if (receipt) return <CashReceipt receipt={receipt} businessName={business.name} timeZone={business.timeZoneId}
    pdfPath={`/api${base}/receipts/${receipt.id}/pdf?locationId=${locationId}`} printNotice={printNotice}
    backLabel={pending ? 'New sale' : 'Back to checkout'} onBack={() => {
      if (pending) {
        try { sessionStorage.removeItem(key) } catch { /* Storage may be unavailable. */ }
        setPending(null)
        setBasket([])
        setCustomer(null); setCustomerOpen(false); setCustomerSearch('')
        setCash('')
      }
      setReceipt(null)
      setPrintNotice('')
      setSearch('')
      setShowPayment(false)
    }} />

  return <section className="pos-checkout" aria-label="Point of Sale checkout">
    <header className="pos-header">
      <div>
        <h2>{pending ? 'Recover Pending Sale' : 'New Sale'}</h2>
        {pending && <p>Check the previous transaction before recording it again.</p>}
      </div>
      <div className="pos-header-actions">
        {!pending && dashboard.canOperate && cashAvailable && <span className="small text-dark">F2 Search · F4 Customer · F9 Checkout</span>}
        <span className="pos-mode"><Check size={15} /> Cash checkout · {business.currencyCode}</span>
        <button type="button" className="btn btn-outline-secondary btn-sm pos-exit-button"
          disabled={busy || scanBusy || !!pending} onClick={() => basket.length ? setExitArmed(true) : onExit()}>
          <ArrowLeft size={15} /> Exit POS
        </button>
      </div>
    </header>
    {exitArmed && <div className="pos-exit-confirm" role="alert">
      <span>Unposted basket items will be discarded when you leave checkout.</span>
      <button type="button" className="btn btn-outline-danger btn-sm" disabled={scanBusy} onClick={onExit}>Discard and exit</button>
      <button type="button" className="btn btn-outline-secondary btn-sm" onClick={() => setExitArmed(false)}>Stay in POS</button>
    </div>}
    {error && !showPayment && <div role="alert" className="alert alert-danger">{error}</div>}
    {!cashAvailable && <div className="alert alert-warning" role="status">
      Cash checkout is disabled in Business Settings. Existing receipts can still be reviewed.
    </div>}
    {pending ? <section className="content-section pos-pending" aria-label="Pending sale recovery">
      <div className="pos-panel-heading"><h2>Pending cash sale</h2><span>Reference held for safe retry</span></div>
      <ul>{pending.lines.map(line => <li key={line.productId}>{line.name} · {line.quantity} × {money(line.expectedUnitPrice)}</li>)}</ul>
      <p>Customer: {pending.customerId ? `#${pending.customerId}` : 'Walk-in'}</p>
      <p><strong>Total {money(total)}</strong> · Cash received {money(pending.tendered)}</p>
      <p role="status">{busy ? 'Recording sale…' : lookupState === 'missing' ? 'No posted receipt found for this sale.'
        : lookupState === 'unknown' ? 'Receipt status could not be verified. Do not create another sale.' : 'Checking for the posted receipt…'}</p>
      {lookupState === 'missing' && <button type="button" className="btn btn-primary me-2"
        disabled={busy || scanBusy || !dashboard.canOperate} onClick={() => void recordSale()}>Record original request</button>}
      <button type="button" className="btn btn-outline-secondary" disabled={busy || scanBusy || lookupState === 'checking'}
        onClick={() => { setLookupState('checking'); setRefresh(n => n + 1) }}>Check status again</button>
    </section> : <div className="pos-grid">
      <section className="content-section pos-catalog" aria-label="Product selection">
        <div className="pos-panel-heading">
          <h2>Products</h2>
          <span className="pos-count-pill">{productsLoading ? 'Loading…' : `${products.length} shown`}</span>
        </div>
        <div className="pos-location-row">
          <label className="form-label" htmlFor="pos-location">Store location
            <select id="pos-location" className="form-select" value={locationId}
              disabled={busy || scanBusy || basket.length > 0} onChange={event => {
                setHistoryPage(0)
                setLocation(Number(event.target.value))
              }}>
              {locations.map(location => <option key={location.id} value={location.id}>{location.name}</option>)}
            </select>
          </label>
          <button type="button" className="btn btn-outline-secondary btn-sm" disabled={busy || scanBusy || !locationId}
            aria-expanded={historyPage > 0} aria-controls="pos-receipt-history"
            onClick={() => setHistoryPage(page => page ? 0 : 1)}>
            <ReceiptText size={16} /> {historyPage > 0 ? 'Back to products' : 'Receipt history'}
          </button>
        </div>
        {!historyPage && <form className="pos-search" role="search" onSubmit={event => void submitBarcode(event)}>
          <label className="visually-hidden" htmlFor="pos-product-search">Search product or scan barcode</label>
          <Search size={19} aria-hidden="true" />
          <input ref={searchInput} id="pos-product-search" aria-keyshortcuts="F2" autoFocus className="form-control" type="search"
            placeholder="Search product, SKU or scan barcode…" maxLength={150} value={search}
            disabled={!dashboard.canOperate || busy} readOnly={scanBusy} onChange={event => setSearch(event.target.value)} />
          <button type="submit" className="btn btn-outline-primary btn-sm"
            disabled={!search.trim() || scanBusy || busy || !dashboard.canOperate || !cashAvailable} aria-label="Add exact barcode or SKU">
            {scanBusy ? 'Checking…' : 'Scan / Enter'}
          </button>
        </form>}
        {!historyPage && hint && <p className="pos-field-status" role="status">{hint}</p>}
        {!dashboard.canOperate && <p className="alert alert-warning" role="status">
          Your business is not enabled for checkout. Product selection is read-only.
        </p>}
        <div className="pos-catalog-results" aria-busy={!historyPage && productsLoading}>
          {historyPage > 0 ? <section id="pos-receipt-history" aria-label="Cash receipt history" className="pos-receipt-history">
            <div className="pos-receipt-heading">
              <h3>Receipt history</h3>
              <button type="button" className="btn btn-outline-secondary btn-sm" disabled={busy || scanBusy || !locationId}
                onClick={() => void lookupReceipt()}><ReceiptText size={16} /> Last Receipt</button>
            </div>
        <form className="pos-search" role="search" aria-label="Receipt lookup" onSubmit={event => {
          event.preventDefault()
          if (validReceiptNumber) void lookupReceipt(receiptNumber.trim())
        }}>
          <input className="form-control" aria-label="Receipt number" placeholder="Receipt number"
            inputMode="numeric" maxLength={19} value={receiptNumber} disabled={busy || scanBusy || !locationId}
            onChange={event => setReceiptNumber(event.target.value)} />
          <button type="submit" className="btn btn-outline-secondary btn-sm"
            disabled={busy || scanBusy || !locationId || !validReceiptNumber}>Find receipt</button>
        </form>
          {historyError ? <div role="alert">Unable to load receipt history.{' '}
            <button type="button" onClick={() => setHistoryRetry(value => value + 1)}>Retry history</button>
          </div> : !history ? <p role="status">Loading receipt history…</p> : <>
            {history.items.length === 0 ? <p>No receipts on this page.</p> :
              <ul className="list-group mb-2">{history.items.map(item => <li key={item.id} className="list-group-item">
                <button type="button" className="btn btn-link p-0" disabled={busy || scanBusy}
                  onClick={() => void lookupReceipt(String(item.id))}>Receipt #{item.id}</button>
                {' · '}{item.currencyCode} {item.total.toFixed(2)}{' · '}
                {new Date(item.postedAtUtc).toLocaleString(undefined, { timeZone: business.timeZoneId })}
              </li>)}</ul>}
            <div className="d-flex gap-2 align-items-center">
              <button type="button" className="btn btn-outline-secondary btn-sm" disabled={busy || scanBusy || historyPage === 1}
                onClick={() => setHistoryPage(page => page - 1)}>Previous</button>
              <span>Page {historyPage}</span>
              <button type="button" className="btn btn-outline-secondary btn-sm" disabled={busy || scanBusy || !history.hasMore}
                onClick={() => setHistoryPage(page => page + 1)}>Next</button>
            </div>
          </>}
          </section> : productsLoading ? <div className="pos-products-empty" role="status">
            <Package size={30} aria-hidden="true" /><strong>Loading products…</strong>
            <p>Checking available products and stock for this location.</p>
          </div> : products.length ? <div className="pos-products-grid">
            {products.map(item => {
              const inBasket = basket.find(line => line.productId === item.id)?.quantity ?? 0
              const canAdd = dashboard.canOperate && cashAvailable && !busy && !scanBusy &&
                item.available - inBasket >= 0.001 &&
                (inBasket > 0 || basket.length < 100)
              return <button key={item.id} type="button" className="pos-product-card" disabled={!canAdd}
                aria-label={`Add ${item.name}, ${money(item.salePrice)}, ${item.available} ${item.unitCode ?? 'units'} available`}
                onClick={() => addProduct(item)}>
                <span className="pos-product-meta">
                  {inBasket > 0 && <span className="pos-product-in-cart">{inBasket} in cart</span>}
                  <strong>{item.name}</strong>
                  <small>{item.sku}</small>
                  <span className="pos-product-bottom"><b>{money(item.salePrice)}</b>
                    <span>{item.available > 0 ? `${item.available} ${item.unitCode ?? 'units'} in stock` : 'Out of stock'}</span></span>
                </span>
                <span className="pos-product-plus" aria-hidden="true"><Plus size={15} /></span>
              </button>
            })}
          </div> : <div className="pos-products-empty">
            <Package size={30} /><strong>No products to display</strong>
            <p>{hint || 'Only available products for the selected location can be sold.'}</p>
          </div>}
        </div>
      </section>
      <section className="content-section pos-basket" aria-label="Current sale basket">
        <div className="pos-panel-heading">
          <h2>Current Sale</h2>
          <span className="pos-count-pill">{basket.length} {basket.length === 1 ? 'product' : 'products'}</span>
        </div>
        <div className="pos-basket-toolbar">
          <span><ShoppingBag size={16} /> {basket.length} {basket.length === 1 ? 'product' : 'products'}</span>
          {basket.length > 0 && !clearArmed && <button type="button" className="pos-basket-clear"
            disabled={busy || scanBusy} onClick={() => setClearArmed(true)}><Trash2 size={15} /> Clear</button>}
          {clearArmed && <div className="pos-clear-confirm">
            <span>Clear this basket?</span>
            <button type="button" disabled={busy || scanBusy} onClick={() => {
              setBasket([]); setCustomer(null); setCustomerOpen(false); setCustomerSearch(''); setReviewProductId(null); setClearArmed(false); setCash('')
            }}>Yes, clear</button>
            <button type="button" onClick={() => setClearArmed(false)}>Cancel</button>
          </div>}
        </div>
        <div className="d-flex align-items-center justify-content-between gap-2 px-2 py-1">
          <span className="small text-truncate">Customer: <strong>{customer?.name ?? 'Walk-in'}</strong></span>
          <button type="button" className="btn btn-outline-secondary btn-sm" disabled={busy || scanBusy || !dashboard.canOperate || !cashAvailable}
            aria-expanded={customerOpen} aria-keyshortcuts="F4" onClick={() => { setCustomerOpen(v => !v); setCustomerSearch(''); setCustomerResults([]) }}>
            {customerOpen ? 'Close' : 'Select customer'}
          </button>
          {customer && <button type="button" className="btn btn-outline-secondary btn-sm" disabled={busy || scanBusy}
            onClick={() => { setCustomer(null); setCustomerOpen(false); setCustomerSearch('') }}>Walk-in</button>}
        </div>
        {customerOpen && <div className="px-2 pb-2">
          <label htmlFor="pos-customer-search" className="visually-hidden">Search customer by name or phone</label>
          <input ref={customerInput} id="pos-customer-search" autoFocus className="form-control form-control-sm" maxLength={100}
            placeholder="Customer name or phone" value={customerSearch} disabled={busy || scanBusy}
            onChange={event => { setCustomerSearch(event.target.value); setCustomerResults([]) }} />
          {customerStatus && <p className="small mb-1" role="status">{customerStatus}</p>}
          {customerResults.length > 0 && <div className="list-group overflow-auto" style={{ maxHeight: 150 }}>
            {customerResults.map(item => <button key={item.id} type="button" className="list-group-item list-group-item-action"
              disabled={busy || scanBusy} onClick={() => { setCustomer(item); setCustomerOpen(false); setCustomerSearch('') }}>
              {item.name}{item.phone ? ' · ' + item.phone : ''}
            </button>)}
          </div>}
        </div>}
        {!basket.length && <div className="pos-basket-empty">
          <ShoppingBag size={34} /><strong>Your basket is empty</strong>
          <p>Select a product from the catalogue to start this sale.</p>
        </div>}
        {basket.length > 0 && <ul className="pos-basket-lines">{basket.map((line, index) =>
          <li key={line.productId} className="pos-basket-line">
            <div className="pos-line-description">
              <strong>{line.name}</strong>
              {line.productId === reviewProductId && <span className="text-danger">
                Review required. Remove this item and select it again.
              </span>}
              <small>{money(line.expectedUnitPrice)} / {line.unitCode ?? 'unit'}</small>
            </div>
            <div className="pos-quantity-control" aria-label={`Quantity of ${line.name}`}>
              <button type="button" aria-label={`Decrease quantity of ${line.name}`}
                disabled={busy || scanBusy || line.quantity === 1 || line.quantity <= 0.001}
                onClick={() => stepQuantity(line, -1)}><Minus size={15} /></button>
              <input className="form-control form-control-sm" aria-label={`Quantity of ${line.name}`}
                type="number" min="0.001" max="1000000" step="0.001" disabled={busy || scanBusy} value={line.quantity}
                onChange={event => updateQuantity(line, Number(event.target.value))} />
              <button type="button" aria-label={`Increase quantity of ${line.name}`}
                disabled={busy || scanBusy || line.quantity + 0.001 >
                  Math.min(1000000, availableStock(line) ?? 1000000)}
                onClick={() => stepQuantity(line, 1)}><Plus size={15} /></button>
            </div>
            <b className="pos-line-amount">{money(lineAmounts[index] / 100)}</b>
            <button type="button" className="pos-remove-item" disabled={busy || scanBusy}
              aria-label={`Remove ${line.name}`} onClick={() => {
                setBasket(previous => previous.filter(x => x.productId !== line.productId))
                if (line.productId === reviewProductId) setReviewProductId(null)
                setCash('')
                setClearArmed(false)
              }}><Trash2 size={16} /></button>
          </li>)}</ul>}
        <div className="pos-basket-checkout">
          {hasUnsupportedAmount && <p role="alert" className="pos-field-status">
            The basket amount cannot be represented accurately. Check prices and quantities or split the sale.
          </p>}
          {hasZeroValueLine && <p role="status" className="pos-field-status">
            A product line rounds to {money(0)}. Increase its quantity or remove it.
          </p>}
          <div className="pos-subtotal-row"><span>Subtotal</span><strong>{money(total)}</strong></div>
          <div className="pos-total-row"><span>Total Payable</span><strong>{money(total)}</strong></div>
          <button ref={checkoutButton} type="button" aria-keyshortcuts="F9" className="btn btn-primary pos-record-button"
            disabled={!dashboard.canOperate || !cashAvailable || busy || scanBusy || needsReview || hasZeroValueLine ||
              !basket.length || !Number.isFinite(total) || total <= 0 ||
              basket.some(line => !validQuantity(line.quantity))}
            onClick={() => { setError(''); setShowPayment(true) }}>
            <Banknote size={19} /> Pay / Checkout <ArrowRight size={18} />
          </button>
        </div>
      </section>
    </div>}
    {showPayment && !pending && !receipt && <PosCashPaymentDialog amount={total}
      cash={cash} currency={business.currencyCode} lineCount={basket.length} busy={busy} error={error}
      onCashChange={value => { setCash(value); setError('') }} onCancel={() => setShowPayment(false)}
      autoOpen={dashboard.receiptAutoOpen === true}
      onConfirm={openPrint => void recordSale(openPrint)} />}
  </section>
}
