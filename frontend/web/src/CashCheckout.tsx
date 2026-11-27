import { useEffect, useLayoutEffect, useRef, useState, type FormEvent } from 'react'
import { ArrowLeft, ArrowRight, Banknote, Check, Minus, Package, Plus, ReceiptText, Search, ShoppingBag, Trash2 } from 'lucide-react'
import PosCashPaymentDialog from './PosCashPaymentDialog'
import CashRegisterOpening from './CashRegisterOpening'
import LookupDialog, { MasterLookup } from './LookupDialog'
import { maxExactCents, moneyCents } from './posMoney'
import { baseQuantity } from './stockQuantity'
import CashReceipt, { type Receipt } from './CashReceipt'
import { ActionButton } from './ActionFeedback'
import { api, ApiResponseError, operationKey, post, SessionExpiredError, UncertainWriteError, type RetailDashboard } from './api'

interface SaleUnit { unitId: number; unitCode: string; factor: number; salePrice: number }
interface Product {
  id: number; name: string; sku: string; barcode: string | null; salePrice: number; available: number;
  unitCode?: string | null
  conversions?: SaleUnit[]
}
interface Line { productId: number; name: string; quantity: number; expectedUnitPrice: number;
  unitCode?: string | null; available?: number
  unitId?: number; expectedFactor?: number; baseUnitCode?: string | null; basePrice?: number;
  expectedBaseUnitCode?: string | null;
  conversions?: SaleUnit[] }
interface SaleRequest { locationId: number; requestKey: string; tendered: number; lines: Line[]; customerId?: number | null; dueDate?: string }
interface SaleCustomer { id: number; name: string; phone: string }
interface ReceiptHistory {
  items: Pick<Receipt, 'id' | 'total' | 'currencyCode' | 'postedAtUtc'>[]
  hasMore: boolean
}

function validQuantity(value: number): boolean {
  return Number.isFinite(value) && value >= 0.001 && value <= 1000000 &&
    Math.abs(Math.round(value * 1000) - value * 1000) < 0.000001
}

function stockQuantity(line: Line, quantity = line.quantity): number | null {
  return validQuantity(quantity) ? baseQuantity(quantity, line.expectedFactor ?? 1) : null
}

function lineAmountCents(line: Line): number {
  const cents = moneyCents(line.expectedUnitPrice)
  if (!validQuantity(line.quantity) || !Number.isFinite(cents)) return NaN
  // Quantity has at most three decimals. Integer arithmetic matches the server's
  // positive decimal midpoint rounding without a floating-point half-cent error.
  const amount = (BigInt(cents) * BigInt(Math.round(line.quantity * 1000)) + 500n) / 1000n
  return amount <= BigInt(maxExactCents) ? Number(amount) : NaN
}

function matchesCreditReceipt(receipt: Receipt, request: SaleRequest): boolean {
  return receipt.paymentMethod === 'Credit' && receipt.requestKey === request.requestKey &&
    receipt.customerId === request.customerId && receipt.creditDueDate === request.dueDate
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
      typeof line.name !== 'string' || stockQuantity(line) === null ||
      (line.unitId == null) !== (line.expectedFactor == null) ||
      (line.unitId != null && (!Number.isSafeInteger(line.unitId) || line.unitId <= 0)) ||
      !Number.isFinite(moneyCents(line.expectedUnitPrice))))
      throw new Error('Invalid checkout lines')
    if (saved.customerId != null && (!Number.isSafeInteger(saved.customerId) || saved.customerId < 1)) throw new Error('Invalid checkout customer')
    if (saved.dueDate !== undefined && (!/^\d{4}-\d{2}-\d{2}$/.test(saved.dueDate) ||
      !saved.customerId || saved.tendered !== 0)) throw new Error('Invalid credit checkout')
    return { locationId: saved.locationId, requestKey: saved.requestKey, tendered: saved.tendered, lines, customerId: saved.customerId ?? null,
      ...(saved.dueDate ? { dueDate: saved.dueDate } : {}) }
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
  const creditAvailable = dashboard.roleCode === 'Owner' && dashboard.creditPaymentEnabled === true
  const saleAvailable = cashAvailable || creditAvailable
  const creditBase = `/businesses/${business.id}/credit-sales`
  const key = `cash-checkout:${userId}:${business.id}`
  const base = `/businesses/${business.id}/cash-sales`
  const [pending, setPending] = useState(() => restore(key))
  const [method, setMethod] = useState<'Cash' | 'Credit'>(() => pending?.dueDate || (!cashAvailable && creditAvailable) ? 'Credit' : 'Cash')
  const [dueDate, setDueDate] = useState('')
  const [creditConfirmed, setCreditConfirmed] = useState(false)
  const [lookupState, setLookupState] = useState<'checking' | 'missing' | 'unknown'>('checking')
  const [refresh, setRefresh] = useState(0)
  const [receipt, setReceipt] = useState<Receipt | null>(null)
  const [printNotice, setPrintNotice] = useState('')
  const [locationId, setLocation] = useState(pending?.locationId ?? locations[0]?.id ?? 0)
  const [customer, setCustomer] = useState<SaleCustomer | null>(() => pending?.customerId
    ? { id: pending.customerId, name: `Customer #${pending.customerId}`, phone: '' } : null)
  const [customerOpen, setCustomerOpen] = useState(false)
  const [newCustomerOpen, setNewCustomerOpen] = useState(false)
  const [newCustomer, setNewCustomer] = useState({ name: '', phone: '', email: '' })
  const [customerSaving, setCustomerSaving] = useState(false)
  const [customerError, setCustomerError] = useState('')
  const [customerNeedsReview, setCustomerNeedsReview] = useState(false)
  const customerDialog = useRef<HTMLDialogElement>(null)
  const customerWrite = useRef(false)
  const canCreateCustomer = dashboard.canOperate && ['Owner', 'Manager'].includes(dashboard.roleCode)
  const [productOpen, setProductOpen] = useState(false)
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
  const checkoutButton = useRef<HTMLButtonElement>(null)
  const lineAmounts = basket.map(lineAmountCents)
  const totalCents = lineAmounts.reduce((sum, amount) => sum + amount, 0)
  const hasUnsupportedAmount = !Number.isSafeInteger(totalCents) || totalCents > maxExactCents
  const total = hasUnsupportedAmount ? NaN : totalCents / 100
  const hasZeroValueLine = lineAmounts.some(amount => amount === 0)

  // Confirmation applies to the currently selected customer, basket and location.
  useEffect(() => { setCreditConfirmed(false) }, [basket, customer, locationId])
  const money = (amount: number) => Number.isFinite(amount)
    ? (business.currencyCode === 'INR' ? '₹' : business.currencyCode + ' ') + amount.toFixed(2) : '—'

  useEffect(() => {
    if (!historyPage || !locationId || pending || receipt) return
    const controller = new AbortController()
    setHistory(null)
    setHistoryError(false)
    api<ReceiptHistory>(`${base}/receipts?locationId=${locationId}&page=${historyPage}`, { signal: controller.signal })
      .then(result => { if (!controller.signal.aborted) setHistory(result) })
      .catch(() => { if (!controller.signal.aborted) setHistoryError(true) })
    return () => controller.abort()
  }, [base, historyPage, historyRetry, locationId, pending, receipt])

  useEffect(() => {
    if (!pending || receipt || busy) return
    const controller = new AbortController()
    setLookupState('checking')
    const query = new URLSearchParams({ locationId: String(pending.locationId), requestKey: pending.requestKey })
    api<Receipt>((pending.dueDate ? creditBase : base) + '/receipt?' + query, { signal: controller.signal })
      .then(result => {
        if (pending.dueDate && !matchesCreditReceipt(result, pending))
          throw new Error('Credit invoice response could not be verified.')
        if (!controller.signal.aborted) { setReceipt(result); setError('') }
      })
      .catch(cause => {
        if (controller.signal.aborted) return
        setLookupState(cause instanceof ApiResponseError && cause.status === 404 ? 'missing' : 'unknown')
      })
    return () => controller.abort()
  }, [base, creditBase, pending, refresh, receipt, busy])

  useEffect(() => {
    if (!locationId || !dashboard.canOperate || pending || receipt || scanBusy || historyPage) return
    const controller = new AbortController()
    setProducts([])
    setProductsLoading(true)
    setHint('')
    const timer = setTimeout(() => {
      api<Product[]>(`${base}/products?locationId=${locationId}&search=${encodeURIComponent(search)}`,
        { signal: controller.signal })
        .then(items => {
          if (controller.signal.aborted) return
          setProducts(items)
          setProductsLoading(false)
          setHint(items.length ? '' : 'No eligible products found.')
        }).catch(cause => { if (!controller.signal.aborted) { setProductsLoading(false); setHint(cause instanceof Error ? cause.message : 'Unable to load products.') } })
    }, 200)
    return () => { clearTimeout(timer); controller.abort() }
  }, [base, search, locationId, dashboard.canOperate, pending, receipt, scanBusy, historyPage])

  useEffect(() => {
    if (!customerOpen || pending || receipt || !locationId || !dashboard.canOperate) return
    const controller = new AbortController()
    setCustomerResults([])
    const term = customerSearch.trim()
    if (term.length < 2) { setCustomerStatus('Enter at least two characters.'); return }
    setCustomerStatus('Searching customers…')
    const timer = setTimeout(() => {
      api<SaleCustomer[]>(base + '/customers?' + new URLSearchParams({ locationId: String(locationId), search: term }), { signal: controller.signal })
        .then(result => {
          if (!controller.signal.aborted) { setCustomerResults(result); setCustomerStatus(result.length ? '' : 'No matching active customer.') }
        }).catch(() => { if (!controller.signal.aborted) setCustomerStatus('Unable to search customers. Edit the search to retry.') })
    }, 200)
    return () => { clearTimeout(timer); controller.abort() }
  }, [base, customerOpen, customerSearch, locationId, pending, receipt, dashboard.canOperate])

  useEffect(() => {
    if (!dashboard.canOperate || !saleAvailable || busy || scanBusy || pending ||
      receipt || showPayment || clearArmed || exitArmed) return
    function onKeyDown(event: KeyboardEvent) {
      if (event.defaultPrevented || event.repeat || event.isComposing ||
        event.ctrlKey || event.altKey || event.metaKey || event.shiftKey ||
        lock.current || scanLock.current) return
      if (customerOpen || productOpen || newCustomerOpen || document.querySelector('dialog[open]')) { if (['F2', 'F4', 'F9'].includes(event.key)) event.preventDefault(); return }
      if (event.key === 'F2') {
        event.preventDefault()
        if (historyPage) setHistoryPage(0)
        else {
          searchInput.current?.focus()
          searchInput.current?.select()
        }
      } else if (event.key === 'F4') {
        event.preventDefault()
        setCustomerSearch('')
        setCustomerResults([])
        setCustomerOpen(true)
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
  }, [dashboard.canOperate, saleAvailable, busy, scanBusy, pending, receipt,
    showPayment, clearArmed, exitArmed, historyPage, customerOpen, productOpen, newCustomerOpen])

  useLayoutEffect(() => {
    if (!newCustomerOpen) return
    const dialog = customerDialog.current
    if (!dialog) return
    dialog.showModal()
    dialog.querySelector<HTMLInputElement>('input')?.focus()
    return () => dialog.close()
  }, [newCustomerOpen])

  function closeNewCustomer() {
    customerDialog.current?.close()
    setNewCustomerOpen(false)
    setNewCustomer({ name: '', phone: '', email: '' })
    setCustomerError('')
  }

  async function saveNewCustomer(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (!canCreateCustomer || !saleAvailable || !newCustomerOpen || customerWrite.current ||
      customerNeedsReview || lock.current || scanLock.current || busy || pending || receipt ||
      newCustomer.name.trim().length < 2) return
    customerWrite.current = true
    setCustomerSaving(true)
    setCustomerError('')
    try {
      const created = await post<SaleCustomer>(`/businesses/${business.id}/customers`, {
        name: newCustomer.name.trim(), phone: newCustomer.phone.trim(), email: newCustomer.email.trim()
      })
      setCustomer(created)
      closeNewCustomer()
    } catch (cause) {
      if (cause instanceof UncertainWriteError) {
        setCustomerNeedsReview(true)
        setCustomerError('Creation status is unknown. Search existing customers before creating another record.')
      } else if (!(cause instanceof SessionExpiredError)) {
        setCustomerError(cause instanceof Error ? cause.message : 'Unable to save customer.')
      }
    } finally {
      customerWrite.current = false
      setCustomerSaving(false)
    }
  }

  function addProduct(item: Product, fromScan = false) {
    if (!dashboard.canOperate || !saleAvailable || lock.current || busy || pending || receipt ||
      (scanLock.current && !fromScan)) return
    const existing = basket.find(line => line.productId === item.id)
    if (existing?.unitId != null) {
      setError('This product uses an alternate sale unit. Adjust its basket quantity or switch to the base unit before scanning again.')
      return
    }
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
      expectedUnitPrice: item.salePrice, unitCode: item.unitCode, available: item.available,
      baseUnitCode: item.unitCode, expectedBaseUnitCode: item.unitCode,
      basePrice: item.salePrice, conversions: item.conversions }
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
      !dashboard.canOperate || !saleAvailable || !locationId) return
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

  function fitsStock(line: Line, quantity = line.quantity) {
    const converted = stockQuantity(line, quantity)
    const knownStock = availableStock(line)
    return converted !== null && (knownStock === undefined || converted <= knownStock)
  }

  function changeUnit(line: Line, unitId: number) {
    if (scanLock.current || lock.current || busy || pending || receipt) return
    const selected = line.conversions?.find(x => x.unitId === unitId)
    if (unitId !== 0 && !selected) return
    const next: Line = { ...line, quantity: 1, unitId: selected?.unitId, expectedFactor: selected?.factor,
      unitCode: selected?.unitCode ?? line.baseUnitCode, expectedUnitPrice: selected?.salePrice ?? line.basePrice ?? line.expectedUnitPrice }
    if (!fitsStock(next)) {
      setError('Not enough base stock for one unit. Select another sale unit.')
      return
    }
    if (!Number.isFinite(moneyCents(next.expectedUnitPrice)) || next.expectedUnitPrice <= 0) {
      setError('Selected unit price is outside the supported billing range.')
      return
    }
    setBasket(previous => previous.map(x => x.productId === line.productId ? next : x))
    setCash(''); setError('')
  }

  function updateQuantity(line: Line, quantity: number) {
    if (scanLock.current || !validQuantity(quantity)) return
    if (!fitsStock(line, quantity)) {
      const knownStock = availableStock(line)
      setError(line.unitId == null && knownStock !== undefined
        ? `Only ${knownStock} ${line.unitCode ?? 'units'} available at this location.`
        : 'Quantity exceeds available base stock or cannot be converted exactly to three decimals.')
      return
    }
    setBasket(previous => previous.map(x => x.productId === line.productId ? { ...x, quantity } : x))
    setCash('')
    setError('')
  }

  function stepQuantity(line: Line, direction: -1 | 1) {
    const step = line.unitId != null || line.quantity >= 1 ? 1 : line.quantity >= 0.1 ? 0.1 :
      line.quantity >= 0.01 ? 0.01 : 0.001
    const maximum = Math.min(1000000, line.unitId != null ? 1000000 : availableStock(line) ?? 1000000)
    const next = Number((direction > 0 ?
      Math.min(maximum, line.quantity + step) :
      Math.max(0.001, line.quantity - step)).toFixed(3))
    if (next !== line.quantity) updateQuantity(line, next)
  }

  async function recordSale(openPrint = false) {
    const isCredit = pending ? !!pending.dueDate : method === 'Credit'
    const receivedCents = moneyCents(cash)
    if (lock.current || scanLock.current || !dashboard.canOperate || needsReview ||
      (!pending && (isCredit ? !creditAvailable : !cashAvailable)) ||
      (pending && lookupState !== 'missing')) return
    if (!pending && (!locationId || !basket.length || basket.some(line =>
      !fitsStock(line) ||
      !Number.isFinite(line.expectedUnitPrice) || line.expectedUnitPrice < 0) ||
      hasZeroValueLine || !Number.isFinite(total) || total <= 0 ||
      (isCredit ? !customer || !dueDate || !creditConfirmed : !Number.isFinite(receivedCents) || receivedCents < totalCents))) {
      setError(isCredit ? 'Select a customer and due date, then confirm the credit sale.' : 'Check basket quantities and enter enough cash before recording the sale.')
      return
    }
    const request: SaleRequest = pending ?? { locationId, lines: basket, tendered: isCredit ? 0 : receivedCents / 100,
      requestKey: operationKey(), customerId: customer?.id ?? null, ...(isCredit ? { dueDate } : {}) }
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
      if (isCredit) sessionStorage.setItem(key, JSON.stringify(request))
      else try { sessionStorage.setItem(key, JSON.stringify(request)) } catch { /* Keep the request key in memory. */ }
      setPending(request)
      posted = await post<Receipt>(isCredit ? creditBase : base, request)
      if (isCredit && !matchesCreditReceipt(posted, request))
        throw new Error('Credit invoice response could not be verified. Check the saved request.')
    } catch (cause) {
      closePrintTab()
      setError(cause instanceof Error ? cause.message : 'Unable to confirm sale.')
      // Verified basket errors can be corrected after recovery; ambiguous outcomes
      // and access failures keep the original key available for receipt lookup.
      const editableRejection = cause instanceof ApiResponseError && (
        (cause.status === 400 &&
          (cause.code === 'CASH_SALE_BASKET_CHANGED' || cause.code === 'CASH_SALE_ZERO_VALUE_LINE' || cause.code === 'CASH_SALE_CUSTOMER_CHANGED')) ||
        (cause.status === 409 && ['CASH_SALES_DISABLED', 'CREDIT_NOT_RELEASED', 'CREDIT_LIMIT_EXCEEDED', 'CREDIT_OVERDUE'].includes(cause.code ?? '')))
      const initialRejection = !pending && cause instanceof ApiResponseError &&
        [400, 403].includes(cause.status)
      if (initialRejection || editableRejection) {
        if (isCredit) setCreditConfirmed(false)
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
    if (!openPrint || isCredit) return
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
    pdfPath={receipt.paymentMethod === 'Credit'
      ? `/api${creditBase}/business-invoices/${receipt.id}/pdf?locationId=${locationId}`
      : `/api${base}/receipts/${receipt.id}/pdf?locationId=${locationId}`} printNotice={printNotice}
    backLabel={pending ? 'New sale' : 'Back to checkout'} onBack={() => {
      if (pending) {
        try { sessionStorage.removeItem(key) } catch { /* Storage may be unavailable. */ }
        setPending(null)
        setBasket([])
        setCustomer(null); setCustomerOpen(false); setCustomerSearch('')
        setCash(''); setDueDate(''); setCreditConfirmed(false)
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
        {!pending && dashboard.canOperate && saleAvailable && <span className="small text-dark">F2 Search · F4 Customer · F9 Checkout</span>}
        <span className="pos-mode"><Check size={15} /> {pending?.dueDate || method === 'Credit' ? 'Credit' : 'Cash'} checkout · {business.currencyCode}</span>
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
      Cash checkout is disabled in Business Settings. {creditAvailable ? 'Credit checkout is available in the owner pilot.' : 'Existing receipts can still be reviewed.'}
    </div>}
    {pending ? <section className="content-section pos-pending" aria-label="Pending sale recovery">
      <div className="pos-panel-heading"><h2>Pending {pending.dueDate ? 'credit' : 'cash'} sale</h2><span>Reference held for safe retry</span></div>
      <ul>{pending.lines.map(line => <li key={line.productId}>{line.name} · {line.quantity}{line.unitCode ? ' ' + line.unitCode : ''} × {money(line.expectedUnitPrice)}</li>)}</ul>
      <p>Customer: {pending.customerId ? `#${pending.customerId}` : 'Walk-in'}</p>
      <p><strong>Total {money(total)}</strong> · {pending.dueDate ? 'Credit due ' + pending.dueDate : 'Cash received ' + money(pending.tendered)}</p>
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
          <div className="pos-location-field">
            <label className="form-label mb-0" htmlFor={locations.length > 1 ? 'pos-location' : undefined}>Store location</label>
            {locations.length === 1 ? <strong>{locations[0].name}</strong> :
              <MasterLookup id="pos-location" title="Select store location" value={locationId}
                emptyLabel="Select location" options={locations}
                disabled={busy || scanBusy || basket.length > 0 || !locations.length}
                onChange={id => { setHistoryPage(0); setLocation(id); setCustomer(null) }} />}
          </div>
          <button type="button" className="btn btn-outline-secondary btn-sm" disabled={busy || scanBusy || !locationId}
            aria-expanded={historyPage > 0} aria-controls="pos-receipt-history"
            onClick={() => setHistoryPage(page => page ? 0 : 1)}>
            <ReceiptText size={16} /> {historyPage > 0 ? 'Back to products' : 'Receipt history'}
          </button>
        </div>
        {!historyPage && dashboard.cashRegisterOpeningEnabled && locationId > 0 &&
          <CashRegisterOpening key={locationId} userId={userId} businessId={business.id} locationId={locationId}
            canOperate={dashboard.canOperate} disabled={busy || scanBusy} />}
        {!historyPage && <form className="pos-search" role="search" onSubmit={event => void submitBarcode(event)}>
          <label className="visually-hidden" htmlFor="pos-product-search">Search product or scan barcode</label>
          <button type="button" className="btn btn-outline-secondary btn-sm" aria-label="Search items"
            aria-haspopup="dialog" aria-expanded={productOpen}
            disabled={busy || scanBusy || !dashboard.canOperate || !saleAvailable}
            onClick={() => setProductOpen(true)}><Search size={19} aria-hidden="true" /></button>
          <input ref={searchInput} id="pos-product-search" aria-keyshortcuts="F2" autoFocus className="form-control" type="search"
            placeholder="Search product, SKU or scan barcode…" maxLength={150} value={search}
            disabled={!dashboard.canOperate || busy} readOnly={scanBusy} onChange={event => setSearch(event.target.value)} />
          <button type="submit" className="btn btn-outline-primary btn-sm"
            disabled={!search.trim() || scanBusy || busy || !dashboard.canOperate || !saleAvailable} aria-label="Add exact barcode or SKU">
            {scanBusy ? 'Checking…' : 'Scan / Enter'}
          </button>
        </form>}
        {productOpen && <LookupDialog title="Search items" searchId="pos-item-lookup-search"
          search={search} placeholder="Item code, name or barcode"
          columns={['Item code', 'Item name', 'Unit', 'Available stock', 'Sale price']}
          context={locations.find(location => location.id === locationId)?.name}
          rows={products.map(item => {
            const line = basket.find(entry => entry.productId === item.id)
            const remaining = item.available - (line ? stockQuantity(line) ?? 0 : 0)
            return { id: item.id, cells: [item.sku, item.name, item.unitCode ?? '—',
              new Intl.NumberFormat(undefined, { maximumFractionDigits: 3 }).format(item.available), money(item.salePrice)],
              disabled: remaining < 0.001 || line?.unitId != null || (!line && basket.length >= 100) }
          })}
          status={productsLoading ? 'Searching…' : hint}
          onSearch={value => { setProducts([]); setProductsLoading(true); setSearch(value) }}
          onSelect={id => {
            const item = products.find(entry => entry.id === id)
            setProductOpen(false)
            if (item) addProduct(item)
          }} onClose={() => setProductOpen(false)} />}
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
              const basketLine = basket.find(line => line.productId === item.id)
              const inBasket = basketLine ? stockQuantity(basketLine) ?? 0 : 0
              const canAdd = dashboard.canOperate && saleAvailable && !busy && !scanBusy &&
                basketLine?.unitId == null && item.available - inBasket >= 0.001 &&
                (inBasket > 0 || basket.length < 100)
              return <button key={item.id} type="button" className="pos-product-card" disabled={!canAdd}
                aria-label={`Add ${item.name}, ${money(item.salePrice)}, ${item.available} ${item.unitCode ?? 'units'} available`}
                onClick={() => addProduct(item)}>
                <span className="pos-product-meta">
                  {inBasket > 0 && <span className="pos-product-in-cart">{inBasket} {basketLine?.unitId != null ? (item.unitCode ?? 'base units') + ' ' : ''}in cart</span>}
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
        {basket.length > 0 && <div className="pos-basket-toolbar">
          {!clearArmed && <button type="button" className="pos-basket-clear"
            disabled={busy || scanBusy} onClick={() => setClearArmed(true)}><Trash2 size={15} /> Clear</button>}
          {clearArmed && <div className="pos-clear-confirm">
            <span>Clear this basket?</span>
            <button type="button" disabled={busy || scanBusy} onClick={() => {
              setBasket([]); setCustomer(null); setCustomerOpen(false); setCustomerSearch(''); setReviewProductId(null); setClearArmed(false); setCash('')
            }}>Yes, clear</button>
            <button type="button" onClick={() => setClearArmed(false)}>Cancel</button>
          </div>}
        </div>}
        <div className="d-flex align-items-center justify-content-between gap-2 px-2 py-1">
          <span className="small text-truncate">Customer: <strong>{customer?.name ?? 'Walk-in'}</strong></span>
          <button type="button" className="btn btn-outline-secondary btn-sm" disabled={busy || scanBusy || !dashboard.canOperate || !saleAvailable}
            aria-haspopup="dialog" aria-expanded={customerOpen} aria-keyshortcuts="F4" onClick={() => { setCustomerOpen(v => !v); setCustomerSearch(''); setCustomerResults([]) }}>
            {customerOpen ? 'Close' : 'Select customer'}
          </button>
          {canCreateCustomer && <button type="button" className="btn btn-outline-secondary btn-sm"
            aria-label="New customer" title="New customer" aria-haspopup="dialog"
            disabled={busy || scanBusy || !saleAvailable || customerNeedsReview}
            onClick={() => setNewCustomerOpen(true)}><Plus size={16} aria-hidden="true" /></button>}
          {customer && <button type="button" className="btn btn-outline-secondary btn-sm" disabled={busy || scanBusy}
            onClick={() => { setCustomer(null); setCustomerOpen(false); setCustomerSearch('') }}>Walk-in</button>}
        </div>
        {customerOpen && <LookupDialog title="Search customers" searchId="pos-customer-search"
          search={customerSearch} placeholder="Customer name or phone" columns={['Customer', 'Phone']}
          rows={customerResults.map(item => ({ id: item.id, cells: [item.name, item.phone ?? '—'],
            label: item.name + (item.phone ? ' · ' + item.phone : '') }))}
          status={customerStatus}
          onSearch={value => { setCustomerSearch(value); setCustomerResults([]) }}
          onSelect={id => {
            const item = customerResults.find(x => x.id === id)
            if (item) { setCustomer(item); setCustomerOpen(false); setCustomerSearch(''); setCustomerNeedsReview(false) }
          }} onClose={() => { setCustomerOpen(false); setCustomerSearch('') }} />}
        {newCustomerOpen && <dialog ref={customerDialog} className="entry-dialog" aria-labelledby="pos-new-customer-heading"
          onCancel={event => { event.preventDefault(); if (!customerWrite.current) closeNewCustomer() }}
          onKeyDownCapture={event => {
            if (event.key === 'Escape') { event.preventDefault(); event.stopPropagation(); if (!customerWrite.current) closeNewCustomer() }
          }}>
          <h3 className="h5 mb-3" id="pos-new-customer-heading">New customer</h3>
          <form aria-busy={customerSaving} onSubmit={event => void saveNewCustomer(event)}>
            <fieldset disabled={customerSaving || customerNeedsReview}>
              <div className="mb-3">
                <label className="form-label" htmlFor="pos-new-customer-name">Customer name *</label>
                <input id="pos-new-customer-name" className="form-control" required minLength={2} maxLength={120}
                  value={newCustomer.name} onChange={event => setNewCustomer(value => ({ ...value, name: event.target.value }))} />
              </div>
              <div className="mb-3">
                <label className="form-label" htmlFor="pos-new-customer-phone">Phone</label>
                <input id="pos-new-customer-phone" className="form-control" type="tel" maxLength={25}
                  value={newCustomer.phone} onChange={event => setNewCustomer(value => ({ ...value, phone: event.target.value }))} />
              </div>
              <label className="form-label" htmlFor="pos-new-customer-email">Email</label>
              <input id="pos-new-customer-email" className="form-control" type="email" maxLength={160}
                value={newCustomer.email} onChange={event => setNewCustomer(value => ({ ...value, email: event.target.value }))} />
            </fieldset>
            {customerError && <p className="small text-danger mt-2" role="alert">{customerError}</p>}
            <div className="d-flex justify-content-end gap-2 mt-3">
              <button type="button" className="btn btn-outline-secondary" disabled={customerSaving} onClick={closeNewCustomer}>Cancel</button>
              <ActionButton busy={customerSaving} disabled={customerNeedsReview || newCustomer.name.trim().length < 2}>Save customer</ActionButton>
            </div>
          </form>
        </dialog>}
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
              {!!line.conversions?.length && <select className="form-select form-select-sm"
                aria-label={`Sale unit for ${line.name}`} disabled={busy || scanBusy || line.productId === reviewProductId}
                value={line.unitId ?? 0} onChange={event => changeUnit(line, Number(event.target.value))}>
                <option value={0}>{line.baseUnitCode ?? 'Base unit'}</option>
                {line.conversions.map(unit => <option key={unit.unitId} value={unit.unitId}>
                  {unit.unitCode} · {unit.factor} {line.baseUnitCode}
                </option>)}
              </select>}
              {line.unitId != null && <small>{stockQuantity(line)} {line.baseUnitCode} stock</small>}
            </div>
            <div className="pos-quantity-control" aria-label={`Quantity of ${line.name}`}>
              <button type="button" aria-label={`Decrease quantity of ${line.name}`}
                disabled={busy || scanBusy || line.quantity === 1 || line.quantity <= 0.001 ||
                  (line.unitId != null && (line.quantity <= 1 || !fitsStock(line, Number((line.quantity - 1).toFixed(3)))))}
                onClick={() => stepQuantity(line, -1)}><Minus size={15} /></button>
              <input className="form-control form-control-sm" aria-label={`Quantity of ${line.name}`}
                type="number" min="0.001" max="1000000" step="0.001" disabled={busy || scanBusy} value={line.quantity}
                onChange={event => updateQuantity(line, Number(event.target.value))} />
              <button type="button" aria-label={`Increase quantity of ${line.name}`}
                disabled={busy || scanBusy || (line.unitId != null
                  ? !fitsStock(line, Number((line.quantity + 1).toFixed(3)))
                  : line.quantity + 0.001 > Math.min(1000000, availableStock(line) ?? 1000000))}
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
          <div className="pos-total-row"><span>Total Payable</span><strong>{money(total)}</strong></div>
          {creditAvailable && <div className="mb-2">
            <label htmlFor="pos-payment-method" className="form-label">Payment method</label>
            <select id="pos-payment-method" className="form-select form-select-sm" value={method}
              disabled={busy || scanBusy} onChange={event => {
                setMethod(event.target.value === 'Credit' ? 'Credit' : 'Cash')
                setCash(''); setCreditConfirmed(false); setError('')
              }}>
              {cashAvailable && <option value="Cash">Cash</option>}
              <option value="Credit">Credit</option>
            </select>
            {method === 'Credit' && <>
              <p className="small mt-1 mb-1">{customer ? 'Credit customer: ' + customer.name : 'Select a customer before recording credit.'}</p>
              <label htmlFor="pos-credit-due" className="form-label">Due date</label>
              <input id="pos-credit-due" className="form-control form-control-sm" type="date" value={dueDate}
                disabled={busy || scanBusy} onChange={event => { setDueDate(event.target.value); setCreditConfirmed(false) }} />
              <label className="form-check mt-2"><input className="form-check-input" type="checkbox" checked={creditConfirmed}
                disabled={busy || scanBusy} onChange={event => setCreditConfirmed(event.target.checked)} /> Confirm unpaid credit sale</label>
            </>}
          </div>}
          <button ref={checkoutButton} type="button" aria-keyshortcuts="F9" className="btn btn-primary pos-record-button"
            disabled={!dashboard.canOperate || !saleAvailable || busy || scanBusy || needsReview || hasZeroValueLine ||
              !basket.length || !Number.isFinite(total) || total <= 0 ||
              basket.some(line => !fitsStock(line)) ||
              (method === 'Credit' ? !creditAvailable || !customer || !dueDate || !creditConfirmed : !cashAvailable)}
            onClick={() => { setError(''); if (method === 'Credit') void recordSale(); else setShowPayment(true) }}>
            <Banknote size={19} /> {method === 'Credit' ? 'Record credit sale' : 'Pay / Checkout'} <ArrowRight size={18} />
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
