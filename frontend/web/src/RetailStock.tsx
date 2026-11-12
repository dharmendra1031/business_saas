import { useEffect, useRef, useState, type FormEvent } from 'react'
import { api, post, SessionExpiredError } from './api'

interface Location {
  id: number
  name: string
  code: string
}

interface StockItem {
  id: number
  name: string
  sku: string
  isActive: boolean
  quantity: number
  hasStockRecord: boolean
}

interface StockPage {
  items: StockItem[]
  total: number
  page: number
  pageSize: number
}

interface StockMovement {
  id: number
  productId: number
  name: string
  sku: string
  quantityChange: number
  balanceAfter: number
  sourceType: string
  postedAtUtc: string
  actor: string | null
}

interface MovementPage {
  items: StockMovement[]
  total: number
  page: number
  pageSize: number
}

function message(error: unknown): string {
  return error instanceof Error ? error.message : 'Request failed. Please try again.'
}

export default function RetailStock({ businessId, locations, canOperate }: {
  businessId: number
  locations: Location[]
  canOperate: boolean
}) {
  const [locationId, setLocationId] = useState(locations[0]?.id ?? 0)
  const [items, setItems] = useState<StockItem[]>([])
  const [total, setTotal] = useState(0)
  const [page, setPage] = useState(1)
  const [search, setSearch] = useState('')
  const [query, setQuery] = useState('')
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState('')
  const [reload, setReload] = useState(0)
  const [opening, setOpening] = useState<StockItem | null>(null)
  const [quantity, setQuantity] = useState('')
  const [saving, setSaving] = useState(false)
  const [saveError, setSaveError] = useState('')
  const [notice, setNotice] = useState('')
  const [historyProductId, setHistoryProductId] = useState<number | null>(null)
  const [historyPage, setHistoryPage] = useState(1)
  const [history, setHistory] = useState<StockMovement[]>([])
  const [historyTotal, setHistoryTotal] = useState(0)
  const [historyLoading, setHistoryLoading] = useState(false)
  const [historyError, setHistoryError] = useState('')
  const quantityInput = useRef<HTMLInputElement>(null)

  const basePath = `/businesses/${businessId}/stock/locations/${locationId}`

  useEffect(() => {
    setPage(1)
    setSearch('')
    setQuery('')
    setOpening(null)
    setHistoryProductId(null)
    setNotice('')
  }, [locationId])

  useEffect(() => {
    if (!locationId) return
    let active = true
    setLoading(true)
    setError('')
    setItems([])
    setTotal(0)
    api<StockPage>(basePath + '?' + new URLSearchParams({ page: String(page), search: query }))
      .then(data => {
        if (!active) return
        setItems(data.items)
        setTotal(data.total)
      })
      .catch(cause => {
        if (active && !(cause instanceof SessionExpiredError)) setError(message(cause))
      })
      .finally(() => { if (active) setLoading(false) })
    return () => { active = false }
  }, [basePath, page, query, reload])

  useEffect(() => {
    if (!historyProductId) return
    let active = true
    setHistoryLoading(true)
    setHistoryError('')
    setHistory([])
    setHistoryTotal(0)
    const params = new URLSearchParams({
      productId: String(historyProductId), page: String(historyPage)
    })
    api<MovementPage>(basePath + '/movements?' + params)
      .then(data => {
        if (!active) return
        setHistory(data.items)
        setHistoryTotal(data.total)
      })
      .catch(cause => {
        if (active && !(cause instanceof SessionExpiredError)) setHistoryError(message(cause))
      })
      .finally(() => { if (active) setHistoryLoading(false) })
    return () => { active = false }
  }, [basePath, historyProductId, historyPage, reload])

  useEffect(() => {
    if (opening) quantityInput.current?.focus()
  }, [opening])

  function chooseOpening(item: StockItem) {
    setOpening(item)
    setQuantity('0')
    setSaveError('')
    setNotice('')
  }

  async function postOpening(event: FormEvent) {
    event.preventDefault()
    if (!opening || !canOperate || saving || !quantity.trim()) return
    const amount = Number(quantity)
    if (!Number.isFinite(amount) || amount < 0 || amount > 9999999999.999 ||
      Math.round(amount * 1000) / 1000 !== amount) {
      setSaveError('Enter a non-negative quantity with up to three decimal places.')
      return
    }
    if (!window.confirm(`Post opening stock of ${quantity} for ${opening.name}? This can only be done once.`))
      return

    setSaving(true)
    setSaveError('')
    setNotice('')
    try {
      await post(basePath + '/opening', { productId: opening.id, quantity: amount })
      setNotice(`Opening stock posted for ${opening.name}.`)
      setOpening(null)
      setReload(value => value + 1)
    } catch (cause) {
      if (!(cause instanceof SessionExpiredError)) setSaveError(message(cause))
    } finally {
      setSaving(false)
    }
  }

  if (locations.length === 0)
    return <section className="content-section retail-masters">
      <h2>Stock</h2><p>No assigned business location is available.</p>
    </section>

  return (
    <section className="content-section retail-masters" aria-label="Retail stock">
      <div className="masters-heading">
        <div>
          <h2>Stock</h2>
          <p>View current quantities and record initial stock once per product and location.</p>
        </div>
        <div>
          <label className="form-label" htmlFor="stock-location">Location</label>
          <select id="stock-location" className="form-select" value={locationId}
            onChange={event => setLocationId(Number(event.target.value))}>
            {locations.map(location => <option key={location.id} value={location.id}>
              {location.name} ({location.code})
            </option>)}
          </select>
        </div>
      </div>

      {!canOperate && <div className="alert alert-warning" role="status">
        Your subscription is inactive. Stock is read-only.
      </div>}
      {error && <div className="alert alert-danger" role="alert">
        {error} <button type="button" className="btn btn-sm btn-outline-secondary ms-2"
          onClick={() => setReload(value => value + 1)}>Retry</button>
      </div>}
      {notice && <div className="alert alert-success" role="status">{notice}</div>}

      {opening && canOperate && <form className="product-form" onSubmit={event => void postOpening(event)}>
        <h3>Opening stock — {opening.name}</h3>
        <p className="field-help">
          This is an initial quantity, not a stock adjustment. It can only be posted once.
        </p>
        {saveError && <div className="alert alert-danger" role="alert">{saveError}</div>}
        <div className="form-grid">
          <div>
            <label className="form-label" htmlFor="stock-opening-quantity">Opening quantity *</label>
            <input ref={quantityInput} id="stock-opening-quantity" className="form-control"
              type="number" min="0" max="9999999999.999" step="0.001" required
              value={quantity} onChange={event => setQuantity(event.target.value)} />
          </div>
        </div>
        <div className="form-footer">
          <button type="button" className="btn btn-outline-secondary"
            onClick={() => { setOpening(null); setSaveError('') }}>Cancel</button>
          <button type="submit" className="btn btn-primary" disabled={saving}>
            {saving ? 'Posting…' : 'Post opening stock'}
          </button>
        </div>
      </form>}

      <form className="masters-search" onSubmit={event => {
        event.preventDefault()
        setPage(1)
        setQuery(search.trim())
      }}>
        <label htmlFor="stock-search" className="visually-hidden">Search stock products</label>
        <input id="stock-search" className="form-control" type="search" maxLength={100}
          placeholder="Product name, SKU or barcode" value={search}
          onChange={event => setSearch(event.target.value)} />
        <button type="submit" className="btn btn-outline-secondary">Search</button>
      </form>

      {loading ? <p role="status" className="text-secondary">Loading stock…</p>
        : error ? null
          : items.length === 0 ? <p className="masters-empty">
            {query ? 'No matching products.' : 'No products yet. Add products in Catalog first.'}
          </p>
            : <div className="table-responsive">
              <table className="table table-sm align-middle">
                <thead><tr><th scope="col">Product</th><th scope="col">SKU</th>
                  <th scope="col" className="text-end">Available stock</th>
                  <th scope="col" className="text-end">Actions</th></tr></thead>
                <tbody>{items.map(item => <tr key={item.id}>
                  <td><strong>{item.name}</strong>{!item.isActive && <small className="product-secondary">Inactive</small>}</td>
                  <td>{item.sku}</td>
                  <td className="text-end">
                    {item.hasStockRecord
                      ? <strong className={item.quantity > 0 ? 'text-success' : ''}>
                        {item.quantity.toFixed(3)}
                      </strong>
                      : <span className="text-secondary">Not initialized</span>}
                  </td>
                  <td className="text-end text-nowrap">
                    {canOperate && item.isActive && !item.hasStockRecord &&
                      <button type="button" className="btn btn-sm btn-outline-secondary me-2"
                        onClick={() => chooseOpening(item)}>Set opening</button>}
                    {item.hasStockRecord &&
                      <button type="button" className="btn btn-sm btn-outline-secondary"
                        onClick={() => {
                          setHistoryProductId(item.id)
                          setHistoryPage(1)
                        }}>History</button>}
                  </td>
                </tr>)}</tbody>
              </table>
            </div>}

      {!loading && !error && total > 0 && <div className="masters-pages">
        <span className="small text-secondary">Page {page} · {total} products</span>
        <div className="btn-group">
          <button type="button" className="btn btn-sm btn-outline-secondary" disabled={page === 1}
            onClick={() => setPage(current => current - 1)}>Previous</button>
          <button type="button" className="btn btn-sm btn-outline-secondary" disabled={page * 20 >= total}
            onClick={() => setPage(current => current + 1)}>Next</button>
        </div>
      </div>}

      {historyProductId !== null && <div className="product-form">
        <div className="masters-heading">
          <h3>Stock movement history</h3>
          <button type="button" className="btn btn-sm btn-outline-secondary"
            onClick={() => setHistoryProductId(null)}>Close</button>
        </div>
        {historyError && <div className="alert alert-danger" role="alert">{historyError}</div>}
        {historyLoading ? <p role="status">Loading movements…</p>
          : historyError ? null
            : history.length === 0 ? <p>No stock movements found.</p>
              : <div className="table-responsive">
                <table className="table table-sm align-middle">
                  <thead><tr><th scope="col">Date / time (local)</th><th scope="col">Source</th>
                    <th scope="col" className="text-end">Change</th>
                    <th scope="col" className="text-end">Balance after</th>
                    <th scope="col">Posted by</th></tr></thead>
                  <tbody>{history.map(entry => <tr key={entry.id}>
                    <td>{new Date(entry.postedAtUtc + (entry.postedAtUtc.endsWith('Z') ? '' : 'Z')).toLocaleString()}</td>
                    <td>{entry.sourceType}</td>
                    <td className="text-end">{entry.quantityChange.toFixed(3)}</td>
                    <td className="text-end">{entry.balanceAfter.toFixed(3)}</td>
                    <td>{entry.actor ?? '—'}</td>
                  </tr>)}</tbody>
                </table>
              </div>}
        {!historyLoading && !historyError && historyTotal > 0 && <div className="masters-pages">
          <span className="small text-secondary">Page {historyPage} · {historyTotal} movements</span>
          <div className="btn-group">
            <button type="button" className="btn btn-sm btn-outline-secondary" disabled={historyPage === 1}
              onClick={() => setHistoryPage(current => current - 1)}>Previous</button>
            <button type="button" className="btn btn-sm btn-outline-secondary"
              disabled={historyPage * 20 >= historyTotal}
              onClick={() => setHistoryPage(current => current + 1)}>Next</button>
          </div>
        </div>}
      </div>}
    </section>
  )
}
