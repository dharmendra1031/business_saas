import { useEffect, useState, type FormEvent } from 'react'
import { api, post, SessionExpiredError } from './api'

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

export default function RetailPurchases({ businessId, canOperate, locations, roleCode, currencyCode }: {
  businessId: number
  canOperate: boolean
  locations: { id: number; name: string; code: string }[]
  roleCode: 'Owner' | 'Manager'
  currencyCode: string
}) {
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
  const [locationId, setLocationId] = useState(locations[0]?.id ?? 0)
  const [supplierSearch, setSupplierSearch] = useState('')
  const [supplierQuery, setSupplierQuery] = useState('')
  const [suppliers, setSuppliers] = useState<Supplier[]>([])
  const [supplierId, setSupplierId] = useState(0)
  const [productSearch, setProductSearch] = useState('')
  const [productQuery, setProductQuery] = useState('')
  const [products, setProducts] = useState<Product[]>([])
  const [productId, setProductId] = useState(0)
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
    let active = true
    setLoading(true)
    setLoadError('')
    setOrders([])
    setTotal(0)
    api<Page<OrderRow>>(basePath + '?page=' + page)
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
  }, [basePath, page, reload])

  useEffect(() => {
    let active = true
    setSuppliers([])
    api<Page<Supplier>>(`/businesses/${businessId}/suppliers?search=${encodeURIComponent(supplierQuery)}&page=1`)
      .then(data => { if (active) setSuppliers(data.items.filter(x => x.isActive)) })
      .catch(cause => { if (active && !(cause instanceof SessionExpiredError)) setError(message(cause)) })
    return () => { active = false }
  }, [businessId, supplierQuery, reload])

  useEffect(() => {
    let active = true
    setProducts([])
    api<Page<Product>>(`/businesses/${businessId}/products?search=${encodeURIComponent(productQuery)}&page=1`)
      .then(data => { if (active) setProducts(data.items.filter(x => x.isActive)) })
      .catch(cause => { if (active && !(cause instanceof SessionExpiredError)) setError(message(cause)) })
    return () => { active = false }
  }, [businessId, productQuery, reload])

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

  function addLine() {
    const product = products.find(x => x.id === productId)
    if (!product || lines.some(x => x.productId === productId)) return
    setLines(value => [...value, {
      productId: product.id, name: product.name, sku: product.sku, quantity: '1', unitCost: '0'
    }])
    setProductId(0)
    setError('')
  }

  async function createOrder(event: FormEvent) {
    event.preventDefault()
    if (!canOperate || busy) return
    if (!locationId || !supplierId || lines.length === 0 ||
        lines.some(x => Number(x.quantity) <= 0 || Number(x.unitCost) < 0)) {
      setError('Select location, supplier and at least one valid product line.')
      return
    }

    setBusy(true)
    setError('')
    setNotice('')
    try {
      const result = await post<{ id: number }>(basePath, {
        locationId, supplierId, lines: lines.map(x => ({
          productId: x.productId, quantity: Number(x.quantity), unitCost: Number(x.unitCost)
        }))
      })
      setCreating(false)
      setLines([])
      setSupplierId(0)
      setNotice('Purchase order saved as Draft. Owner approval is required.')
      setPage(1)
      setSelectedId(result.id)
      setReload(value => value + 1)
    } catch (cause) {
      if (!(cause instanceof SessionExpiredError)) setError(message(cause))
    } finally {
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

  async function saveReceiving(event: FormEvent) {
    event.preventDefault()
    if (!detail || busy || !canOperate) return
    const receivingLines = detail.lines.filter(line => Number(receiving[line.id] ?? '0') > 0)
      .map(line => ({
        purchaseOrderLineId: line.id, quantity: Number(receiving[line.id])
      }))
    if (!receivingLines.length || receivingLines.some((line) => {
      const item = detail.lines.find(x => x.id === line.purchaseOrderLineId)
      return !item || line.quantity > item.remainingQuantity ||
        Math.round(line.quantity * 1000) / 1000 !== line.quantity
    })) {
      setError('Enter valid quantities within the remaining ordered amounts.')
      return
    }
    setBusy(true)
    setError('')
    setNotice('')
    try {
      const receipt = await post<{ id: number }>(
        basePath + '/' + detail.id + '/receivings', { lines: receivingLines })
      setNotice(`Receiving #${receipt.id} saved as Draft. Post it to update stock.`)
      setReload(value => value + 1)
    } catch (cause) {
      if (!(cause instanceof SessionExpiredError)) setError(message(cause))
    } finally {
      setBusy(false)
    }
  }

  async function postReceiving(receivingId: number) {
    if (!detail || busy || !canOperate) return
    if (!window.confirm('Post this receiving to stock? The movement will be recorded in the Stock Ledger.'))
      return
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
    <section className="content-section retail-masters" aria-label="Purchase orders">
      <div className="masters-heading">
        <div>
          <h2>Purchase orders</h2>
          <p>Create a supplier order, approve it, and receive goods in parts. Only posting updates stock.</p>
        </div>
        {canOperate && !creating && <button type="button" className="btn btn-primary"
          onClick={() => { setCreating(true); setError(''); setNotice('') }}>New purchase order</button>}
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
                onChange={event => setSupplierSearch(event.target.value)} />
              <button type="button" className="btn btn-outline-secondary"
                onClick={() => { setSupplierId(0); setSupplierQuery(supplierSearch.trim()) }}>
                Find
              </button>
            </div>
            <select className="form-select" id="purchase-supplier" value={supplierId}
              onChange={event => setSupplierId(Number(event.target.value))} required>
              <option value={0}>Select supplier</option>
              {suppliers.map(x => <option key={x.id} value={x.id}>{x.name}</option>)}
            </select>
          </div>
        </div>
        <div className="product-form">
          <h3>Order items</h3>
          <div className="masters-search">
            <input type="search" className="form-control" aria-label="Find product"
              placeholder="Find product by name or SKU" value={productSearch}
              onChange={event => setProductSearch(event.target.value)} />
            <button type="button" className="btn btn-outline-secondary"
              onClick={() => setProductQuery(productSearch.trim())}>Find</button>
          </div>
          <div className="masters-form">
            <div>
              <label className="form-label" htmlFor="purchase-product">Product</label>
              <select id="purchase-product" className="form-select" value={productId}
                onChange={event => setProductId(Number(event.target.value))}>
                <option value={0}>Select product</option>
                {products.filter(x => !lines.some(line => line.productId === x.id))
                  .map(x => <option key={x.id} value={x.id}>{x.name} ({x.sku})</option>)}
              </select>
            </div>
            <button className="btn btn-outline-secondary" type="button"
              onClick={addLine} disabled={!productId}>Add product</button>
          </div>
          {lines.length > 0 && <div className="table-responsive">
            <table className="table table-sm align-middle">
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
          <button type="button" className="btn btn-outline-secondary"
            onClick={() => setCreating(false)}>Cancel</button>
          <button type="submit" className="btn btn-primary"
            disabled={busy || !supplierId || !lines.length}>
            {busy ? 'Saving…' : 'Save draft order'}
          </button>
        </div>
      </form>}

      <div className="product-form">
        <h3>Purchase order list</h3>
        {loading ? <p role="status">Loading purchase orders…</p>
          : loadError ? null
            : !orders.length ? <p className="masters-empty">No purchase orders yet.</p>
              : <div className="table-responsive">
                <table className="table table-sm align-middle">
                  <thead><tr><th>Order</th><th>Supplier</th><th>Location</th><th>Status</th><th></th></tr></thead>
                  <tbody>{orders.map(order => <tr key={order.id}>
                    <td>#{order.id}</td><td>{order.supplierName}</td>
                    <td>{order.locationName}</td><td>{order.status}</td>
                    <td><button type="button" className="btn btn-sm btn-outline-secondary"
                      onClick={() => { setSelectedId(order.id); setError(''); setNotice('') }}>
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
          <p>Status: <strong>{detail.status}</strong></p>
          {canOperate && roleCode === 'Owner' && detail.status === 'Draft' &&
            <button type="button" className="btn btn-primary mb-3" disabled={busy}
              onClick={() => void approve()}>{busy ? 'Saving…' : 'Approve purchase order'}</button>}
          <div className="table-responsive">
            <table className="table table-sm align-middle">
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
                onClick={() => { setReceivingOpen(x => !x); setError('') }}>
                {receivingOpen ? 'Close receiving form' : 'Create receiving draft'}
              </button>
              {receivingOpen && <form className="product-form"
                onSubmit={event => void saveReceiving(event)}>
                <h3>Goods received</h3>
                <p className="field-help">Only enter quantities physically received now.</p>
                <div className="table-responsive">
                  <table className="table table-sm align-middle">
                    <thead><tr><th>Product</th><th className="text-end">Remaining</th>
                      <th>Receive now</th></tr></thead>
                    <tbody>{detail.lines.filter(x => x.remainingQuantity > 0).map(line => <tr key={line.id}>
                      <td>{line.name}</td><td className="text-end">{line.remainingQuantity.toFixed(3)}</td>
                      <td><input className="form-control" type="number" step="0.001" min="0"
                        max={line.remainingQuantity} aria-label={`Receive ${line.name}`}
                        value={receiving[line.id] ?? '0'}
                        onChange={event => setReceiving(value =>
                          ({ ...value, [line.id]: event.target.value }))} /></td>
                    </tr>)}</tbody>
                  </table>
                </div>
                <button className="btn btn-primary" type="submit" disabled={busy}>
                  {busy ? 'Saving…' : 'Save receiving draft'}
                </button>
              </form>}
            </>}
          <div className="product-form">
            <h3>Receiving history</h3>
            {!detail.receivings.length ? <p className="masters-empty">No receiving vouchers yet.</p>
              : <div className="table-responsive">
                <table className="table table-sm align-middle">
                  <thead><tr><th>Receiving</th><th>Status</th><th></th></tr></thead>
                  <tbody>{detail.receivings.map(receipt => <tr key={receipt.id}>
                    <td>#{receipt.id}</td><td>{receipt.status}</td>
                    <td>{canOperate && receipt.status === 'Draft' &&
                      <button type="button" className="btn btn-sm btn-outline-primary"
                        disabled={busy} onClick={() => void postReceiving(receipt.id)}>
                        Post to stock
                      </button>}</td>
                  </tr>)}</tbody>
                </table>
              </div>}
          </div>
        </>}
      </div>}
    </section>
  )
}
