import { Plus } from 'lucide-react'
import { useEffect, useState, type FormEvent } from 'react'
import { api, post, operationKey, SessionExpiredError } from './api'

interface Supplier {
  id: number
  name: string
  isActive: boolean
}

interface Product {
  id: number
  name: string
  sku: string
  isActive: boolean
}

interface Page<T> {
  items: T[]
  total: number
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
  const [supplierQuery, setSupplierQuery] = useState('')
  const [suppliers, setSuppliers] = useState<Supplier[]>([])
  const [supplierId, setSupplierId] = useState(0)
  const [supplierFormOpen, setSupplierFormOpen] = useState(false)
  const [newSupplier, setNewSupplier] = useState({ name: '', phone: '', email: '' })
  const [addingSupplier, setAddingSupplier] = useState(false)
  const [productSearch, setProductSearch] = useState('')
  const [productQuery, setProductQuery] = useState('')
  const [products, setProducts] = useState<Product[]>([])
  const [lines, setLines] = useState<NewLine[]>([])
  const [selectedId, setSelectedId] = useState<number | null>(null)
  const [detail, setDetail] = useState<OrderDetail | null>(null)
  const [detailError, setDetailError] = useState('')
  const [receiving, setReceiving] = useState<Record<number, string>>({})
  const [receivingOpen, setReceivingOpen] = useState(false)
  const basePath = `/businesses/${businessId}/purchase-orders`
  const money = (amount: number) =>
    new Intl.NumberFormat('en-IN', { style: 'currency', currency: currencyCode }).format(amount)


  useEffect(() => {
    const timer = setTimeout(() => setSupplierQuery(supplierSearch.trim()), 250)
    return () => clearTimeout(timer)
  }, [supplierSearch])

  useEffect(() => {
    const timer = setTimeout(() => setProductQuery(productSearch.trim()), 250)
    return () => clearTimeout(timer)
  }, [productSearch])

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
    // The order register and approval review do not need create-form catalogs.
    if (!creating) return
    let active = true
    setSuppliers([])
    api<Page<Supplier>>(`/businesses/${businessId}/suppliers?search=${encodeURIComponent(supplierQuery)}&page=1`)
      .then(data => { if (active) setSuppliers(data.items.filter(x => x.isActive)) })
      .catch(cause => { if (active && !(cause instanceof SessionExpiredError)) setError(message(cause)) })
    return () => { active = false }
  }, [businessId, supplierQuery, reload, creating])

  useEffect(() => {
    if (!creating) return
    let active = true
    setProducts([])
    api<Page<Product>>(`/businesses/${businessId}/products?search=${encodeURIComponent(productQuery)}&page=1`)
      .then(data => { if (active) setProducts(data.items.filter(x => x.isActive)) })
      .catch(cause => { if (active && !(cause instanceof SessionExpiredError)) setError(message(cause)) })
    return () => { active = false }
  }, [businessId, productQuery, reload, creating])

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
      setSupplierSearch(added.name)
      setSupplierQuery(added.name)
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
    setSupplierQuery('')
    setSupplierFormOpen(false)
    setNewSupplier({ name: '', phone: '', email: '' })
    setProductSearch('')
    setProductQuery('')
    setLines([])
  }

  function addLine(id: number) {
    const product = products.find(x => x.id === id)
    if (!product || lines.some(x => x.productId === id) || lines.length >= 50) return
    setLines(value => [...value, {
      productId: product.id, name: product.name, sku: product.sku, quantity: '1', unitCost: '0'
    }])
    setError('')
  }

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
    if (!canOperate || busy) return
    const approveNow = roleCode === 'Owner' &&
      (event.nativeEvent as SubmitEvent).submitter?.getAttribute('data-action') !== 'draft'
    if (!locationId || !supplierId || lines.length === 0 || lines.length > 50 ||
        lines.some(x => Number(x.quantity) <= 0 || Number(x.unitCost) < 0)) {
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
          productId: x.productId, quantity: Number(x.quantity), unitCost: Number(x.unitCost)
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
    if (!receivingLines.length || Object.values(receiving).some(value =>
        value.trim() !== '' && (!Number.isFinite(Number(value)) || Number(value) < 0 ||
          Math.round(Number(value) * 1000) / 1000 !== Number(value))) ||
        receivingLines.some(line => {
          const item = detail.lines.find(x => x.id === line.purchaseOrderLineId)
          return !item || line.quantity > item.remainingQuantity
        })) {
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
        {canOperate && !creating && <button type="button" className="btn btn-primary"
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
            <label className="form-label" htmlFor="purchase-location">Receiving location *</label>
            <select className="form-select" id="purchase-location" value={locationId}
              onChange={event => setLocationId(Number(event.target.value))} required>
              {locations.map(x => <option key={x.id} value={x.id}>{x.name}</option>)}
            </select>
          </div>
          <div>
            <label className="form-label" htmlFor="purchase-supplier">Supplier *</label>
            <div className="masters-search">
              <input className="form-control" aria-label="Find supplier" type="search"
                placeholder="Supplier search" value={supplierSearch}
                onChange={event => {
                  setSupplierId(0)
                  setSupplierSearch(event.target.value)
                }} />
            </div>
            <select className="form-select" id="purchase-supplier" value={supplierId}
              onChange={event => setSupplierId(Number(event.target.value))} required>
              <option value={0}>Select supplier</option>
              {suppliers.map(x => <option key={x.id} value={x.id}>{x.name}</option>)}
            </select>
            <button type="button" className="btn btn-sm btn-outline-secondary mt-2"
              disabled={addingSupplier}
              onClick={() => {
                if (supplierFormOpen) setNewSupplier({ name: '', phone: '', email: '' })
                setSupplierFormOpen(open => !open)
              }}>
              {supplierFormOpen ? 'Cancel adding supplier' : '+ New supplier'}
            </button>
            {supplierFormOpen && <div className="product-form" role="group" aria-label="New supplier"
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
        <div className="product-form">
          <h3>Order items</h3>
          <div className="masters-search">
            <input type="search" className="form-control" aria-label="Find product"
              placeholder="Find product by name or SKU" value={productSearch}
              onChange={event => setProductSearch(event.target.value)} />
          </div>
          <div className="masters-form">
            <div>
              <label className="form-label" htmlFor="purchase-product">Product</label>
              <select id="purchase-product" className="form-select" value={0}
                disabled={lines.length >= 50}
                onChange={event => addLine(Number(event.target.value))}>
                <option value={0}>Select a product to add</option>
                {products.filter(x => !lines.some(line => line.productId === x.id))
                  .map(x => <option key={x.id} value={x.id}>{x.name} ({x.sku})</option>)}
              </select>
            </div>
          </div>
          {lines.length >= 50 && <p className="field-help" role="status">Maximum 50 products per purchase order.</p>}
          {lines.length > 0 && <div className="table-responsive">
            <table className="table table-sm align-middle business-record-table">
              <thead><tr><th>Product</th><th>Order quantity</th>
                <th>Unit cost ({currencyCode})</th><th></th></tr></thead>
              <tbody>{lines.map(line => <tr key={line.productId}>
                <td>{line.name}<span className="product-secondary">{line.sku}</span></td>
                <td><input type="number" className="form-control" aria-label={`${line.name} quantity`}
                  min="0.001" max="9999999.999" step="0.001" required value={line.quantity}
                  onChange={event => setLines(values => values.map(x =>
                    x.productId === line.productId ? { ...x, quantity: event.target.value } : x))}/></td>
                <td><input type="number" className="form-control" aria-label={`${line.name} unit cost`}
                  min="0" max="99999999.99" step="0.01" required value={line.unitCost}
                  onChange={event => setLines(values => values.map(x =>
                    x.productId === line.productId ? { ...x, unitCost: event.target.value } : x))}/></td>
                <td><button className="btn btn-sm btn-outline-secondary" type="button"
                  onClick={() => setLines(values => values.filter(x =>
                    x.productId !== line.productId))}>Remove</button></td>
              </tr>)}</tbody>
            </table>
          </div>}
          {lines.length > 0 && <p className="text-end mb-0">
            <strong>Estimated order value: {money(lines.reduce((sum, line) =>
              sum + Number(line.quantity || 0) * Number(line.unitCost || 0), 0))}</strong>
          </p>}
        </div>
        <div className="form-footer">
          <button type="button" className="btn btn-outline-secondary" disabled={busy || addingSupplier}
            onClick={() => { resetOrderDraft(); setCreating(false) }}>Cancel</button>
          <button type="submit" className="btn btn-primary"
            disabled={busy || !supplierId || !lines.length}>
            {busy ? 'Saving…' : roleCode === 'Owner' ? 'Save & Approve' : 'Submit for Approval'}
          </button>
          {roleCode === 'Owner' && <button type="submit" data-action="draft"
            className="btn btn-outline-secondary" disabled={busy || !supplierId || !lines.length}>
            Save Draft
          </button>}
        </div>
      </form>}

      <div className="product-form">
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
        {!loading && !loadError && total > 0 && <div className="masters-pages">
          <span className="small text-secondary">Page {page} · {total} orders</span>
          <div className="btn-group">
            <button type="button" className="btn btn-sm btn-outline-secondary"
              disabled={page === 1} onClick={() => setPage(x => x - 1)}>Previous</button>
            <button type="button" className="btn btn-sm btn-outline-secondary"
              disabled={page * 20 >= total} onClick={() => setPage(x => x + 1)}>Next</button>
          </div>
        </div>}
      </div>

      {selectedId !== null && <div className="product-form">
        <div className="masters-heading">
          <h3>Purchase order #{selectedId}</h3>
          <button type="button" className="btn btn-sm btn-outline-secondary"
            onClick={() => { setSelectedId(null); setDetail(null) }}>Close</button>
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
                <td>{line.name}<span className="product-secondary">{line.sku}</span></td>
                <td className="text-end">{line.orderedQuantity.toFixed(3)}</td>
                <td className="text-end">{line.receivedQuantity.toFixed(3)}</td>
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
                <p className="field-help">Enter the delivered quantities. Stock updates only when a receiving is posted.</p>
                <div className="table-responsive">
                  <table className="table table-sm align-middle business-record-table">
                    <thead><tr><th>Product</th><th className="text-end">Remaining</th>
                      <th>Receive now</th></tr></thead>
                    <tbody>{detail.lines.filter(x => x.remainingQuantity > 0).map(line => <tr key={line.id}>
                      <td>{line.name}</td><td className="text-end">{line.remainingQuantity.toFixed(3)}</td>
                      <td><input className="form-control" type="number" step="0.001" min="0"
                        max={line.remainingQuantity} aria-label={`Receive ${line.name}`}
                        value={receiving[line.id] ?? ''}
                        onChange={event => setReceiving(value =>
                          ({ ...value, [line.id]: event.target.value }))} /></td>
                    </tr>)}</tbody>
                  </table>
                </div>
                <div className="form-footer">
                  <button className="btn btn-primary" type="submit" disabled={busy}>
                    {busy ? 'Processing…' : 'Receive & Post Stock'}
                  </button>
                  <button className="btn btn-outline-secondary" type="submit"
                    data-action="draft" disabled={busy}>Save Draft</button>
                </div>
              </form>}
            </>}
          <div className="product-form">
            <h3>Receiving history</h3>
            {!detail.receivings.length ? <p className="masters-empty">No receiving vouchers yet.</p>
              : <div className="table-responsive">
                <table className="table table-sm align-middle business-record-table">
                  <thead><tr><th>Receiving</th><th>Status</th><th></th></tr></thead>
                  <tbody>{detail.receivings.map(receipt => <tr key={receipt.id}>
                    <td>#{receipt.id}</td><td>{receipt.status}</td>
                    <td>{canOperate && receipt.status === 'Draft' &&
                      <button type="button" className="btn btn-sm btn-outline-primary" disabled={busy ||
                        receipt.lines.some(item => item.quantity >
                          (detail.lines.find(x => x.id === item.purchaseOrderLineId)?.remainingQuantity ?? 0))}
                        onClick={() => void postReceiving(receipt.id)}>Post stock</button>}</td>
                  </tr>)}</tbody>
                </table>
              </div>}
          </div>
        </>}
      </div>}
    </section>
  )
}
