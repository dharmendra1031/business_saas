import { Plus, Search } from 'lucide-react'
import { useEffect, useRef, useState, type FormEvent } from 'react'
import { api, post, operationKey, SessionExpiredError } from './api'
import { baseQuantity } from './stockQuantity'
import LookupDialog from './LookupDialog'

interface Supplier {
  id: number
  name: string
  phone?: string
  email?: string
  isActive: boolean
}

interface AlternateUnit { unitId: number; factor: number; unitCode?: string; isActive?: boolean }

interface Product {
  id: number
  name: string
  sku: string
  unitCode?: string
  conversions?: AlternateUnit[]
  availableStock?: number | null
  isActive: boolean
}

interface Page<T> {
  items: T[]
  total: number
  pageSize?: number
}

interface OrderRow {
  id: number
  locationId: number
  locationName: string
  supplierId: number
  supplierName: string
  status: string
  createdAtUtc: string
}

interface OrderLine {
  id: number
  productId: number
  name: string
  sku: string
  unitCode?: string
  baseUnitCode?: string
  conversionFactor?: number
  baseOrderedQuantity?: number
  baseReceivedQuantity?: number
  orderedQuantity: number
  unitCost: number
  receivedQuantity: number
  remainingQuantity: number
}

interface Receipt {
  lines: { purchaseOrderLineId: number; quantity: number }[]
  id: number
  status: string
  createdAtUtc: string
}

interface OrderDetail {
  id: number
  locationId: number
  supplierId: number
  status: string
  lines: OrderLine[]
  receivings: Receipt[]
}

interface NewLine {
  productId: number
  name: string
  sku: string
  unitCode?: string
  alternateUnits: AlternateUnit[]
  selectedUnitId: number
  quantity: string
  unitCost: string
}

function message(error: unknown): string {
  return error instanceof Error ? error.message : 'Unable to complete the request.'
}

export default function RetailPurchases({ businessId, canOperate, locations, roleCode, currencyCode, initialApprovalsOnly }: {
  initialApprovalsOnly: boolean
  businessId: number
  canOperate: boolean
  locations: { id: number; name: string; code: string }[]
  roleCode: 'Owner' | 'Manager'
  currencyCode: string
}) {
  const [approvalsOnly, setApprovalsOnly] = useState(initialApprovalsOnly)
  const [orders, setOrders] = useState<OrderRow[]>([])
  const [total, setTotal] = useState(0)
  const [page, setPage] = useState(1)
  const [reload, setReload] = useState(0)
  const [loading, setLoading] = useState(true)
  const [loadError, setLoadError] = useState('')
  const [error, setError] = useState('')
  const [notice, setNotice] = useState('')
  const [busy, setBusy] = useState(false)
  const [creating, setCreating] = useState(false)
  const [orderKey, setOrderKey] = useState(operationKey)
  const [receivingKey, setReceivingKey] = useState(operationKey)
  const [locationId, setLocationId] = useState(locations[0]?.id ?? 0)
  const [supplierSearch, setSupplierSearch] = useState('')
  const [supplierName, setSupplierName] = useState('')
  const [lookup, setLookup] = useState<'supplier' | 'product' | null>(null)
  const [resolving, setResolving] = useState(false)
  const [lookupPage, setLookupPage] = useState(1)
  const [lookupTotal, setLookupTotal] = useState(0)
  const [lookupPageSize, setLookupPageSize] = useState(20)
  const [lookupError, setLookupError] = useState('')
  const [suppliers, setSuppliers] = useState<Supplier[]>([])
  const [suppliersLoading, setSuppliersLoading] = useState(true)
  const [supplierId, setSupplierId] = useState(0)
  const [supplierFormOpen, setSupplierFormOpen] = useState(false)
  const [newSupplier, setNewSupplier] = useState({ name: '', phone: '', email: '' })
  const [addingSupplier, setAddingSupplier] = useState(false)
  const [productSearch, setProductSearch] = useState('')
  const [products, setProducts] = useState<Product[]>([])
  const [productsLoading, setProductsLoading] = useState(true)
  const [lines, setLines] = useState<NewLine[]>([])
  const focusQuantity = useRef<number | null>(null)

  useEffect(() => {
    if (focusQuantity.current === null) return
    document.getElementById('purchase-qty-' + focusQuantity.current)?.focus()
    focusQuantity.current = null
  }, [lines])
  const [selectedId, setSelectedId] = useState<number | null>(null)
  const [detail, setDetail] = useState<OrderDetail | null>(null)
  const [detailError, setDetailError] = useState('')
  const [receiving, setReceiving] = useState<Record<number, string>>({})
  const [receivingOpen, setReceivingOpen] = useState(false)
  const basePath = `/businesses/${businessId}/purchase-orders`
  const money = (amount: number) =>
    new Intl.NumberFormat('en-IN', { style: 'currency', currency: currencyCode }).format(amount)


  useEffect(() => {
    let active = true
    setLoading(true)
    setLoadError('')
    setOrders([])
    setTotal(0)
    api<Page<OrderRow>>(basePath + '?page=' + page + (approvalsOnly ? '&draftOnly=true' : ''))
      .then(data => {
        if (!active) return
        setOrders(data.items)
        setTotal(data.total)
      })
      .catch(cause => {
        if (active && !(cause instanceof SessionExpiredError)) setLoadError(message(cause))
      })
      .finally(() => { if (active) setLoading(false) })
    return () => { active = false }
  }, [basePath, page, reload, approvalsOnly])

  useEffect(() => {
    if (!creating || !lookup) return
    const controller = new AbortController()
    const supplier = lookup === 'supplier'
    const term = supplier ? supplierSearch : productSearch
    setLookupError('')
    setLookupTotal(0)
    setSuppliers([])
    setProducts([])
    setSuppliersLoading(supplier)
    setProductsLoading(!supplier)
    const timer = setTimeout(async () => {
      try {
        const query = new URLSearchParams({ search: term.trim(), page: String(lookupPage) })
        if (supplier) {
          const data = await api<Page<Supplier>>(`/businesses/${businessId}/suppliers?${query}`, { signal: controller.signal })
          if (controller.signal.aborted) return
          setSuppliers(data.items.filter(item => item.isActive))
          setLookupTotal(data.total)
          setLookupPageSize(data.pageSize ?? 20)
        } else {
          query.set('status', 'active')
          query.set('locationId', String(locationId))
          const data = await api<Page<Product>>(`/businesses/${businessId}/products?${query}`, { signal: controller.signal })
          if (controller.signal.aborted) return
          setProducts(data.items.filter(item => item.isActive))
          setLookupTotal(data.total)
          setLookupPageSize(data.pageSize ?? 20)
        }
      } catch (cause) {
        if (!controller.signal.aborted && !(cause instanceof SessionExpiredError)) setLookupError(message(cause))
      } finally {
        if (!controller.signal.aborted) { setSuppliersLoading(false); setProductsLoading(false) }
      }
    }, 200)
    return () => { clearTimeout(timer); controller.abort() }
  }, [businessId, supplierSearch, productSearch, locationId, reload, creating, lookup, lookupPage])

  function openLookup(kind: 'supplier' | 'product') {
    setLookupPage(1)
    setLookupTotal(0)
    setLookupError('')
    if (kind === 'supplier') { setSupplierSearch(''); setSuppliers([]); setSuppliersLoading(true) }
    else { setProducts([]); setProductsLoading(true) }
    setLookup(kind)
  }

  function changeLookupPage(next: number) {
    setSuppliers([])
    setProducts([])
    setSuppliersLoading(lookup === 'supplier')
    setProductsLoading(lookup === 'product')
    setLookupPage(next)
  }

  useEffect(() => {
    if (selectedId === null) return
    let active = true
    setDetail(null)
    setDetailError('')
    api<OrderDetail>(basePath + '/' + selectedId)
      .then(value => {
        if (!active) return
        setDetail(value)
        setReceiving({})
        setReceivingOpen(false)
      })
      .catch(cause => {
        if (active && !(cause instanceof SessionExpiredError)) setDetailError(message(cause))
      })
    return () => { active = false }
  }, [basePath, selectedId, reload])

  async function createSupplier() {
    const name = newSupplier.name.trim()
    const phone = newSupplier.phone.trim()
    const email = newSupplier.email.trim()
    if (!canOperate || addingSupplier || name.length < 2 || (!phone && !email)) {
      setError('Enter a supplier name and at least a phone number or email.')
      return
    }
    setAddingSupplier(true)
    setError('')
    try {
      const added = await post<Supplier>(`/businesses/${businessId}/suppliers`, { name, phone, email })
      setSuppliers([added])
      setSupplierId(added.id)
      setSupplierName(added.name)
      setSupplierFormOpen(false)
      setNewSupplier({ name: '', phone: '', email: '' })
      setNotice('Supplier added to this order.')
    } catch (cause) {
      if (!(cause instanceof SessionExpiredError)) setError(message(cause))
    } finally {
      setAddingSupplier(false)
    }
  }

  function resetOrderDraft() {
    setLocationId(locations[0]?.id ?? 0)
    setSupplierId(0)
    setSupplierSearch('')
    setSupplierName('')
    setLookup(null)
    setSuppliers([])
    setSuppliersLoading(true)
    setSupplierFormOpen(false)
    setNewSupplier({ name: '', phone: '', email: '' })
    setProductSearch('')
    setProducts([])
    setProductsLoading(true)
    setLines([])
  }

  function addLine(product: Product) {
    if (lines.some(x => x.productId === product.id) || lines.length >= 50) return
    setLookup(null)
    focusQuantity.current = product.id
    setLines(value => [...value, {
      productId: product.id, name: product.name, sku: product.sku,
      unitCode: product.unitCode, alternateUnits: (product.conversions ?? []).filter(x => x.isActive && x.unitCode),
      selectedUnitId: 0, quantity: '1', unitCost: ''
    }])
    if (productSearch) { setProductSearch(''); setProducts([]); setProductsLoading(true) }
    setError('')
  }

  async function resolveItemCode() {
    const code = productSearch.trim()
    if (busy || resolving || lines.length >= 50) return
    if (!code) { openLookup('product'); return }
    setResolving(true)
    setError('')
    try {
      const result = await api<Page<Product>>(`/businesses/${businessId}/products?code=${encodeURIComponent(code)}&status=active&page=1`)
      const match = result.items.find(item => item.isActive && item.sku.toUpperCase() === code.toUpperCase())
      if (match) {
        if (lines.some(line => line.productId === match.id)) setError('This item is already in the order.')
        else addLine(match)
      } else {
        openLookup('product')
      }
    } catch (cause) {
      if (!(cause instanceof SessionExpiredError)) setError(message(cause))
    } finally { setResolving(false) }
  }

  function selectedUnit(line: NewLine) {
    return line.alternateUnits.find(x => x.unitId === line.selectedUnitId)
  }

  const validOrderLines = lines.length > 0 && lines.every(line =>
    baseQuantity(Number(line.quantity), selectedUnit(line)?.factor ?? 1) !== null &&
    line.unitCost.trim() !== '' && Number.isFinite(Number(line.unitCost)) &&
    Number(line.unitCost) >= 0 && Number(line.unitCost) <= 99999999.99 &&
    Math.round(Number(line.unitCost) * 100) / 100 === Number(line.unitCost))
  const validReceiving = !!detail && detail.lines.some(line => Number(receiving[line.id] ?? '') > 0) &&
    detail.lines.every(line => {
      const quantity = Number(receiving[line.id] ?? '')
      return quantity === 0 || (quantity <= line.remainingQuantity &&
        baseQuantity(quantity, line.conversionFactor ?? 1) !== null)
    })

  function toggleReceiving() {
    if (!detail) return
    if (receivingOpen) {
      setReceivingOpen(false)
      setReceiving({})
      return
    }
    setReceiving({})
    setReceivingOpen(true)
  }

  async function createOrder(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (!canOperate || busy || resolving || lookup || addingSupplier || supplierFormOpen) return
    const approveNow = roleCode === 'Owner' &&
      (event.nativeEvent as SubmitEvent).submitter?.getAttribute('data-action') !== 'draft'
    if (!locationId || !supplierId || lines.length === 0 || lines.length > 50 ||
        !validOrderLines) {
      setError('Select location, supplier and 1–50 valid product lines.')
      return
    }

    setBusy(true)
    setError('')
    setNotice('')
    let savedOrderId: number | null = null
    try {
      const result = await post<{ id: number }>(basePath, {
        locationId, supplierId, lines: lines.map(x => ({
          productId: x.productId, quantity: Number(x.quantity), unitCost: Number(x.unitCost),
          expectedBaseUnitCode: x.unitCode,
          ...(selectedUnit(x) ? { unitId: x.selectedUnitId, expectedFactor: selectedUnit(x)!.factor } : {})
        })), requestKey: orderKey
      })
      savedOrderId = result.id
      if (approveNow) await post(`${basePath}/${result.id}/approve`, {})
      setNotice(approveNow
        ? 'Purchase order saved and approved. Stock changes only when goods are received.'
        : roleCode === 'Manager' ? 'Purchase order submitted for Owner approval.'
          : 'Purchase order saved as Draft.')
      if (approveNow) setApprovalsOnly(false)
    } catch (cause) {
      if (!(cause instanceof SessionExpiredError)) {
        setError(savedOrderId !== null
          ? `Purchase order #${savedOrderId} was saved, but approval could not be confirmed. Check its status in the order details; approve there if still Draft. ${message(cause)}`
          : message(cause))
      }
    } finally {
      if (savedOrderId !== null) {
        setCreating(false)
        setOrderKey(operationKey())
        resetOrderDraft()
        setPage(1)
        setSelectedId(savedOrderId)
        setReload(value => value + 1)
      }
      setBusy(false)
    }
  }

  async function approve() {
    if (!detail || busy || roleCode !== 'Owner' || !canOperate) return
    setBusy(true)
    setError('')
    setNotice('')
    try {
      await post(basePath + '/' + detail.id + '/approve', {})
      setNotice('Order approved. Stock has not changed.')
      setReload(value => value + 1)
    } catch (cause) {
      if (!(cause instanceof SessionExpiredError)) setError(message(cause))
    } finally {
      setBusy(false)
    }
  }

  async function saveReceiving(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (!detail || busy || !canOperate) return
    const draftOnly = (event.nativeEvent as SubmitEvent).submitter?.getAttribute('data-action') === 'draft'
    const receivingLines = detail.lines.filter(line => Number(receiving[line.id] ?? '0') > 0)
      .map(line => ({
        purchaseOrderLineId: line.id, quantity: Number(receiving[line.id])
      }))
    if (!validReceiving) {
      setError('Enter valid quantities within the remaining ordered amounts.')
      return
    }

    setBusy(true)
    setError('')
    setNotice('')
    let savedReceivingId: number | null = null
    try {
      const receipt = await post<{ id: number }>(
        basePath + '/' + detail.id + '/receivings',
        { lines: receivingLines, requestKey: receivingKey })
      savedReceivingId = receipt.id
      if (!draftOnly) {
        await post(`${basePath}/${detail.id}/receivings/${receipt.id}/post`, {})
      }
      setNotice(draftOnly
        ? `Receiving #${receipt.id} saved as Draft. Stock has not changed.`
        : `Receiving #${receipt.id} posted. Stock and remaining PO quantity updated.`)
    } catch (cause) {
      if (!(cause instanceof SessionExpiredError)) {
        setError(savedReceivingId !== null
          ? `Receiving #${savedReceivingId} was saved, but stock posting could not be confirmed. Check Receiving history and use Post stock if still Draft. ${message(cause)}`
          : message(cause))
      }
    } finally {
      if (savedReceivingId !== null) {
        // A posted receiving or a saved draft has its own ID; never create a duplicate on retry.
        setReceivingKey(operationKey())
        setReceiving({})
        setReceivingOpen(false)
        setReload(value => value + 1)
      }
      setBusy(false)
    }
  }

  async function postReceiving(receivingId: number) {
    if (!detail || busy || !canOperate) return
    setBusy(true)
    setError('')
    setNotice('')
    try {
      await post(`${basePath}/${detail.id}/receivings/${receivingId}/post`, {})
      setNotice(`Receiving #${receivingId} posted. Stock and remaining PO quantity updated.`)
      setReload(value => value + 1)
    } catch (cause) {
      if (!(cause instanceof SessionExpiredError)) {
        setError(message(cause))
        setReload(value => value + 1)
      }
    } finally {
      setBusy(false)
    }
  }

  return (
    <section className="content-section retail-masters business-module-card" aria-label="Purchase orders">
      <div className="masters-heading business-module-heading">
        <h2>Purchase Orders</h2>
        {canOperate && !creating && selectedId === null && <button type="button" className="btn btn-primary"
          onClick={() => { setOrderKey(operationKey()); setCreating(true); setError(''); setNotice('') }}>
          <Plus size={17} /> New Purchase Order</button>}
      </div>
      {!canOperate && <div className="alert alert-warning" role="status">
        Subscription is inactive. Purchase records are read-only.
      </div>}
      {error && <div className="alert alert-danger" role="alert">{error}</div>}
      {loadError && <div className="alert alert-danger" role="alert">
        {loadError} <button className="btn btn-sm btn-outline-secondary ms-2" type="button"
          onClick={() => setReload(value => value + 1)}>Retry</button>
      </div>}
      {notice && <div className="alert alert-success" role="status">{notice}</div>}

      {creating && <form className="product-form" onSubmit={event => void createOrder(event)}>
        <h3>New purchase order</h3>
        <div className="form-grid">
          <div>
            <label className="form-label" htmlFor={locations.length !== 1 ? 'purchase-location' : undefined}>Location *</label>
            {locations.length === 1 ? <div className="fw-semibold">{locations[0].name}</div> :
              <select className="form-select" id="purchase-location" value={locationId}
                disabled={busy} onChange={event => setLocationId(Number(event.target.value))} required>
                {locations.map(x => <option key={x.id} value={x.id}>{x.name}</option>)}
              </select>}
          </div>
          <div>
            <label className="form-label" htmlFor="purchase-supplier">Supplier *</label>
            <div className="input-group">
              <input id="purchase-supplier" className="form-control" readOnly value={supplierName}
                placeholder="Select supplier" disabled={busy || addingSupplier || resolving}
                onClick={() => openLookup('supplier')}
                onKeyDown={event => { if (event.key === 'Enter') { event.preventDefault(); openLookup('supplier') } }} />
              <button type="button" className="btn btn-outline-secondary" aria-label="Search suppliers"
                disabled={busy || addingSupplier || resolving}
                onClick={() => openLookup('supplier')}><Search size={17}/></button>
            </div>
            <button type="button" className="btn btn-sm btn-outline-secondary mt-2"
              disabled={busy || addingSupplier}
              onClick={() => {
                if (supplierFormOpen) setNewSupplier({ name: '', phone: '', email: '' })
                setSupplierFormOpen(open => !open)
              }}>
              {supplierFormOpen ? 'Cancel adding supplier' : '+ New supplier'}
            </button>
            {supplierFormOpen && <div className="mt-3" role="group" aria-label="New supplier"
              onKeyDown={event => {
                if (event.key === 'Enter' && event.target instanceof HTMLInputElement)
                  event.preventDefault()
              }}>
              <div className="mb-2">
                <label className="form-label" htmlFor="new-supplier-name">Supplier name *</label>
                <input className="form-control" id="new-supplier-name" minLength={2} maxLength={120}
                  value={newSupplier.name} onChange={event =>
                    setNewSupplier(value => ({ ...value, name: event.target.value }))} />
              </div>
              <div className="mb-2">
                <label className="form-label" htmlFor="new-supplier-phone">Phone (or email)</label>
                <input className="form-control" id="new-supplier-phone" type="tel" maxLength={25}
                  value={newSupplier.phone} onChange={event =>
                    setNewSupplier(value => ({ ...value, phone: event.target.value }))} />
              </div>
              <div className="mb-2">
                <label className="form-label" htmlFor="new-supplier-email">Email (or phone)</label>
                <input className="form-control" id="new-supplier-email" type="email" maxLength={160}
                  value={newSupplier.email} onChange={event =>
                    setNewSupplier(value => ({ ...value, email: event.target.value }))} />
              </div>
              <button type="button" className="btn btn-outline-primary"
                disabled={addingSupplier || newSupplier.name.trim().length < 2 ||
                  (!newSupplier.phone.trim() && !newSupplier.email.trim())}
                onClick={() => void createSupplier()}>
                {addingSupplier ? 'Saving supplier…' : 'Save supplier'}
              </button>
            </div>}
          </div>
        </div>
        <div className="mt-3">
          <h3>Order items</h3>
          <div className="table-responsive">
            <table className="table table-sm align-middle business-record-table purchase-entry-table">
              <thead><tr><th>Item code</th><th>Item name</th><th>Unit</th><th>Qty</th>
                <th>Rate ({currencyCode})</th><th className="text-end">Amount</th><th></th></tr></thead>
              <tbody>{lines.map(line => <tr key={line.productId}>
                <td>{line.sku}</td><td>{line.name}</td>
                <td>{line.alternateUnits.length > 0 ? <select className="form-select form-select-sm mt-1"
                    aria-label={`Purchase unit for ${line.name}`} value={line.selectedUnitId} disabled={busy}
                    onChange={event => setLines(values => values.map(x => x.productId === line.productId
                      ? { ...x, selectedUnitId: Number(event.target.value), quantity: '1', unitCost: '' } : x))}>
                    <option value={0}>{line.unitCode ?? 'Base unit'}</option>
                    {line.alternateUnits.map(unit => <option key={unit.unitId} value={unit.unitId}>
                      {unit.unitCode} = {unit.factor} {line.unitCode}
                    </option>)}
                  </select> : <span className="product-secondary">{line.unitCode}</span>}</td>
                <td><input id={'purchase-qty-' + line.productId} type="number" className="form-control" aria-label={`${line.name} quantity`}
                  disabled={busy} min="0.001" max="9999999.999" step="0.001" required value={line.quantity}
                  onChange={event => setLines(values => values.map(x =>
                    x.productId === line.productId ? { ...x, quantity: event.target.value } : x))}/>
                  {line.selectedUnitId !== 0 && <small className="d-block mt-1">
                    {baseQuantity(Number(line.quantity), selectedUnit(line)?.factor ?? 1) === null
                      ? 'Enter an exact base stock quantity.'
                      : `${baseQuantity(Number(line.quantity), selectedUnit(line)?.factor ?? 1)} ${line.unitCode} stock`}
                  </small>}</td>
                <td><input type="number" className="form-control" aria-label={`${line.name} unit cost`}
                  min="0" max="99999999.99" step="0.01" required disabled={busy} value={line.unitCost}
                  onChange={event => setLines(values => values.map(x =>
                    x.productId === line.productId ? { ...x, unitCost: event.target.value } : x))}/></td>
                <td className="text-end text-nowrap">{line.unitCost.trim() === '' ? '—' : money(Number(line.quantity || 0) * Number(line.unitCost))}</td>
                <td><button className="btn btn-sm btn-outline-secondary" type="button" disabled={busy}
                  onClick={() => setLines(values => values.filter(x =>
                    x.productId !== line.productId))}>Remove</button></td>
              </tr>)}
              {lines.length < 50 && <tr>
                <td colSpan={2}>
                  <label className="visually-hidden" htmlFor="purchase-product">Item code or name</label>
                  <div className="input-group">
                    <input id="purchase-product" className="form-control" value={productSearch} autoComplete="off"
                      placeholder="Item code" disabled={busy || resolving} maxLength={100}
                      onChange={event => setProductSearch(event.target.value)}
                      onKeyDown={event => { if (event.key === 'Enter') { event.preventDefault(); void resolveItemCode() } }} />
                    <button type="button" className="btn btn-outline-secondary" aria-label="Search items"
                      disabled={busy || resolving} onClick={() => openLookup('product')}>
                      <Search size={17}/>
                    </button>
                  </div>
                </td>
                <td colSpan={5}></td>
              </tr>}
              </tbody>
            </table>
          </div>
          {lines.length >= 50 && <p className="field-help" role="status">Maximum 50 products per purchase order.</p>}
          {lines.length > 0 && <p className="text-end mb-0">
            <strong>Total: {money(lines.reduce((sum, line) =>
              sum + Number(line.quantity || 0) * Number(line.unitCost || 0), 0))}</strong>
          </p>}
        </div>
        <div className="form-footer">
          <button type="button" className="btn btn-outline-secondary" disabled={busy || resolving || addingSupplier}
            onClick={() => { resetOrderDraft(); setCreating(false) }}>Cancel</button>
          <button type="submit" className="btn btn-primary"
            disabled={busy || resolving || addingSupplier || supplierFormOpen || !supplierId || !validOrderLines}>
            {busy ? 'Saving…' : roleCode === 'Owner' ? 'Save & Approve' : 'Submit for Approval'}
          </button>
          {roleCode === 'Owner' && <button type="submit" data-action="draft"
            className="btn btn-outline-secondary" disabled={busy || resolving || addingSupplier || supplierFormOpen || !supplierId || !validOrderLines}>
            Save Draft
          </button>}
        </div>
        {lookup === 'supplier' && <LookupDialog title="Search suppliers" searchId="purchase-supplier-search"
          search={supplierSearch} placeholder="Supplier name, phone or email" columns={['Supplier', 'Phone']}
          rows={suppliers.map(item => ({ id: item.id, cells: [item.name, item.phone || '—'], label: item.name }))}
          status={suppliersLoading ? 'Searching…' : lookupError || (suppliers.length ? '' : 'No active suppliers on this page')}
          pagination={{ page: lookupPage, hasMore: lookupPage * lookupPageSize < lookupTotal, busy: suppliersLoading, onChange: changeLookupPage }}
          onSearch={value => { setSupplierSearch(value); setLookupPage(1); setSuppliers([]); setSuppliersLoading(true) }}
          onSelect={id => {
            const item = suppliers.find(x => x.id === id)
            if (item) { setSupplierId(item.id); setSupplierName(item.name); setLookup(null) }
          }} onClose={() => setLookup(null)} />}
        {lookup === 'product' && <LookupDialog title="Search items" searchId="purchase-item-search"
          search={productSearch} placeholder="Item code, name or barcode" columns={['Item code', 'Item name', 'Unit', 'Available stock']}
          context={locations.find(location => location.id === locationId)?.name}
          rows={products.filter(item => !lines.some(line => line.productId === item.id))
            .map(item => ({ id: item.id, cells: [item.sku, item.name, item.unitCode ?? '—',
              item.availableStock == null ? '—' : new Intl.NumberFormat(undefined, { maximumFractionDigits: 3 }).format(item.availableStock)] }))}
          status={productsLoading ? 'Searching…' : lookupError || (products.some(item => !lines.some(line => line.productId === item.id)) ? '' : 'No selectable items on this page')}
          pagination={{ page: lookupPage, hasMore: lookupPage * lookupPageSize < lookupTotal, busy: productsLoading, onChange: changeLookupPage }}
          onSearch={value => { setProductSearch(value); setLookupPage(1); setProducts([]); setProductsLoading(true) }}
          onSelect={id => { const item = products.find(x => x.id === id); if (item) addLine(item) }}
          onClose={() => setLookup(null)} />}
      </form>}

      {!creating && selectedId === null && <div className="product-form">
        <h3>Purchase order list</h3>
        {roleCode === 'Owner' && <label><input type="checkbox" checked={approvalsOnly}
          onChange={e => { setApprovalsOnly(e.target.checked); setPage(1) }} /> Awaiting approval only</label>}
        {loading ? <p role="status">Loading purchase orders…</p>
          : loadError ? null
            : !orders.length ? <p className="masters-empty">No purchase orders yet.</p>
              : <div className="table-responsive">
                <table className="table table-sm align-middle business-record-table">
                  <thead><tr><th>Order</th><th>Supplier</th><th>Location</th><th>Status</th><th></th></tr></thead>
                  <tbody>{orders.map(order => <tr key={order.id}>
                    <td>#{order.id}</td><td>{order.supplierName}</td>
                    <td>{order.locationName}</td><td>{order.status}</td>
                    <td><button type="button" className="btn btn-sm btn-outline-secondary"
                      onClick={() => {
                setSelectedId(order.id)
                setReceivingKey(operationKey())
                setReceiving({})
                setReceivingOpen(false)
                setError('')
                setNotice('')
              }}>
                      Details</button></td>
                  </tr>)}</tbody>
                </table>
              </div>}
        {!loading && !loadError && (page > 1 || total > 20) && <div className="masters-pages">
          <span className="small text-secondary">Page {page} · {total} orders</span>
          <div className="btn-group">
            <button type="button" className="btn btn-sm btn-outline-secondary"
              disabled={page === 1} onClick={() => setPage(x => x - 1)}>Previous</button>
            <button type="button" className="btn btn-sm btn-outline-secondary"
              disabled={page * 20 >= total} onClick={() => setPage(x => x + 1)}>Next</button>
          </div>
        </div>}
      </div>}

      {!creating && selectedId !== null && <div className="product-form">
        <div className="masters-heading">
          <h3>Purchase order #{selectedId}</h3>
          <button type="button" className="btn btn-sm btn-outline-secondary"
            disabled={busy} onClick={() => { setSelectedId(null); setDetail(null) }}>Back to orders</button>
        </div>
        {detailError && <div className="alert alert-danger">{detailError}</div>}
        {!detail && !detailError && <p role="status">Loading order…</p>}
        {detail && <>
          <p>Status: <strong>{detail.status === 'Draft' && roleCode === 'Manager' ? 'Awaiting owner approval' : detail.status}</strong></p>
          {canOperate && roleCode === 'Owner' && detail.status === 'Draft' &&
            <button type="button" className="btn btn-primary mb-3" disabled={busy}
              onClick={() => void approve()}>{busy ? 'Saving…' : 'Approve purchase order'}</button>}
          <div className="table-responsive">
            <table className="table table-sm align-middle business-record-table">
              <thead><tr><th>Product</th><th className="text-end">Ordered</th>
                <th className="text-end">Received</th><th className="text-end">Remaining</th>
                <th className="text-end">Unit cost</th></tr></thead>
              <tbody>{detail.lines.map(line => <tr key={line.id}>
                <td>{line.name}<span className="product-secondary">{line.sku}{line.unitCode ? ` · ${line.unitCode}` : ''}</span></td>
                <td className="text-end">{line.orderedQuantity.toFixed(3)}
                  {line.conversionFactor != null && line.conversionFactor !== 1 &&
                    <small className="d-block">{line.baseOrderedQuantity} {line.baseUnitCode}</small>}</td>
                <td className="text-end">{line.receivedQuantity.toFixed(3)}
                  {line.conversionFactor != null && line.conversionFactor !== 1 &&
                    <small className="d-block">{line.baseReceivedQuantity} {line.baseUnitCode}</small>}</td>
                <td className="text-end"><strong>{line.remainingQuantity.toFixed(3)}</strong></td>
                <td className="text-end">{line.unitCost.toFixed(2)} {currencyCode}</td>
              </tr>)}</tbody>
            </table>
          </div>
          <p className="text-end">
            <strong>Ordered value: {money(detail.lines.reduce((sum, line) =>
              sum + line.orderedQuantity * line.unitCost, 0))}</strong>
            <span className="product-secondary">
              Received value: {money(detail.lines.reduce((sum, line) =>
                sum + line.receivedQuantity * line.unitCost, 0))}
            </span>
          </p>
          {canOperate && detail.status === 'Approved' &&
            <>
              <button type="button" className="btn btn-outline-primary"
                onClick={() => { toggleReceiving(); setError('') }}>
                {receivingOpen ? 'Close receiving form' : 'Receive goods'}
              </button>
              {receivingOpen && <form className="product-form"
                onSubmit={event => void saveReceiving(event)}>
                <h3>Goods receiving</h3>
                <button className="btn btn-outline-secondary" type="button" onClick={() => setReceiving(
                  Object.fromEntries(detail.lines.filter(x => x.remainingQuantity > 0).map(x => [x.id, String(x.remainingQuantity)]))
                )}>Receive all remaining</button>
                <p className="field-help">Enter received quantities. Stock updates when you post.</p>
                <div className="table-responsive">
                  <table className="table table-sm align-middle business-record-table">
                    <thead><tr><th>Product</th><th className="text-end">Remaining</th>
                      <th>Receive now</th></tr></thead>
                    <tbody>{detail.lines.filter(x => x.remainingQuantity > 0).map(line => <tr key={line.id}>
                      <td>{line.name}{line.unitCode && <span className="product-secondary">{line.unitCode}</span>}</td>
                      <td className="text-end">{line.remainingQuantity.toFixed(3)}</td>
                      <td><input className="form-control" type="number" step="0.001" min="0"
                        max={line.remainingQuantity} aria-label={`Receive ${line.name}`}
                        value={receiving[line.id] ?? ''}
                        onChange={event => setReceiving(value =>
                          ({ ...value, [line.id]: event.target.value }))} />
                        {line.conversionFactor != null && line.conversionFactor !== 1 && Number(receiving[line.id] ?? '') !== 0 &&
                          <small className="d-block mt-1">
                            {baseQuantity(Number(receiving[line.id]), line.conversionFactor) === null
                              ? 'Enter an exact base stock quantity.'
                              : `${baseQuantity(Number(receiving[line.id]), line.conversionFactor)} ${line.baseUnitCode} stock`}
                          </small>}</td>
                    </tr>)}</tbody>
                  </table>
                </div>
                <div className="form-footer">
                  <button className="btn btn-primary" type="submit" disabled={busy || !validReceiving}>
                    {busy ? 'Processing…' : 'Receive & Post Stock'}
                  </button>
                  <button className="btn btn-outline-secondary" type="submit"
                    data-action="draft" disabled={busy || !validReceiving}>Save Draft</button>
                </div>
              </form>}
            </>}
          <div className="product-form">
            <h3>Receiving history</h3>
            {!detail.receivings.length ? <p className="masters-empty">No receiving vouchers yet.</p>
              : <div className="table-responsive">
                <table className="table table-sm align-middle business-record-table">
                  <thead><tr><th>Receiving</th><th>Items received</th><th>Status</th><th></th></tr></thead>
                  <tbody>{detail.receivings.map(receipt => {
                    const validLines = receipt.lines.length > 0 && receipt.lines.every(item => {
                      const ordered = detail.lines.find(x => x.id === item.purchaseOrderLineId)
                      return !!ordered && Number.isFinite(item.quantity) && item.quantity > 0 &&
                        item.quantity <= ordered.remainingQuantity &&
                        baseQuantity(item.quantity, ordered.conversionFactor ?? 1) !== null
                    })
                    return <tr key={receipt.id}>
                      <td>#{receipt.id}</td>
                      <td>{receipt.lines.map(item => {
                        const ordered = detail.lines.find(x => x.id === item.purchaseOrderLineId)
                        return <div key={item.purchaseOrderLineId}>
                          {ordered?.name ?? 'Unknown PO item'} · {item.quantity.toFixed(3)}{ordered?.unitCode ? ` ${ordered.unitCode}` : ''}
                        </div>
                      })}</td>
                      <td>{receipt.status}</td>
                      <td>{canOperate && receipt.status === 'Draft' &&
                        <button type="button" className="btn btn-sm btn-outline-primary"
                          disabled={busy || !validLines}
                          onClick={() => void postReceiving(receipt.id)}>Post stock</button>}</td>
                    </tr>
                  })}</tbody>
                </table>
              </div>}
          </div>
        </>}
      </div>}
    </section>
  )
}
