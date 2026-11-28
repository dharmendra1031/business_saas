import { useEffect, useRef, useState, type FormEvent } from 'react'
import { api, post, SessionExpiredError, UncertainWriteError } from './api'
import { ActionButton, useActionFeedback } from './ActionFeedback'

interface Product {
  id: number
  name: string
  barcode: string | null
  brand: string
  category: string
  packSize: string
  unit: string
  isActive: boolean
}
interface ProductPage { items: Product[]; nextAfterId: number | null }
type ProductForm = Omit<Product, 'id' | 'barcode'> & { barcode: string }
const empty: ProductForm = { name: '', barcode: '', brand: '', category: '', packSize: '', unit: 'PCS', isActive: true }
const fields = [
  ['name', 'Product name', 150], ['barcode', 'Manufacturer barcode (optional)', 14],
  ['brand', 'Brand', 80], ['category', 'Category', 80],
  ['packSize', 'Pack size', 40], ['unit', 'Unit', 20]
] as const
const path = '/platform/global-products'

export default function PlatformGlobalProducts() {
  const { run } = useActionFeedback()
  const [search, setSearch] = useState('')
  const [filter, setFilter] = useState({ term: '', afterId: 0 })
  const [history, setHistory] = useState<number[]>([])
  const [result, setResult] = useState<ProductPage | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')
  const [reload, setReload] = useState(0)
  const [editing, setEditing] = useState<number | null>(null)
  const [form, setForm] = useState<ProductForm | null>(null)
  const [saving, setSaving] = useState(false)
  const [uncertain, setUncertain] = useState(false)
  const writeLock = useRef(false)

  useEffect(() => {
    const controller = new AbortController()
    setLoading(true)
    setError('')
    setResult(null)
    const params = new URLSearchParams({ afterId: String(filter.afterId) })
    if (filter.term) {
      if (/^(?:[0-9]{8}|[0-9]{12,14})$/.test(filter.term)) params.set('barcode', filter.term)
      else params.set('search', filter.term)
    }
    api<ProductPage>(path + '?' + params, { signal: controller.signal })
      .then(value => { if (!controller.signal.aborted) setResult(value) })
      .catch(cause => {
        if (!controller.signal.aborted && !(cause instanceof SessionExpiredError))
          setError(cause instanceof Error ? cause.message : 'Unable to load products.')
      })
      .finally(() => { if (!controller.signal.aborted) setLoading(false) })
    return () => controller.abort()
  }, [filter, reload])

  function applySearch(event: FormEvent) {
    event.preventDefault()
    setHistory([])
    setFilter({ term: search.trim(), afterId: 0 })
  }

  function open(product?: Product) {
    setEditing(product?.id ?? null)
    setForm(product ? { name: product.name, barcode: product.barcode ?? '', brand: product.brand,
      category: product.category, packSize: product.packSize, unit: product.unit, isActive: product.isActive } : { ...empty })
    setUncertain(false)
  }

  async function save(event: FormEvent) {
    event.preventDefault()
    if (!form || writeLock.current || uncertain) return
    writeLock.current = true
    setSaving(true)
    try {
      await run('Saving global product…', 'Global product saved.', () => editing === null
        ? post(path, form) : api(path + '/' + editing, { method: 'PUT', body: JSON.stringify(form) }))
      setForm(null)
      setHistory([])
      setFilter({ term: '', afterId: 0 })
      setSearch('')
    } catch (cause) {
      if (cause instanceof UncertainWriteError) setUncertain(true)
    } finally {
      writeLock.current = false
      setSaving(false)
    }
  }

  return <>
    <div className="page-header"><div><h1>Global Products</h1></div>
      {!form && <button type="button" className="btn btn-primary" onClick={() => open()}>Add Product</button>}
    </div>
    <section className="content-section" aria-label="Global catalogue">
      {form ? <form onSubmit={event => void save(event)}>
        <h2>{editing === null ? 'New global product' : 'Edit global product'}</h2>
        <fieldset disabled={saving || uncertain}>
          <div className="row g-2">
            {fields.map(([field, label, max]) => <div className="col-12 col-md-6" key={field}>
              <label className="form-label" htmlFor={'global-' + field}>{label}</label>
              <input id={'global-' + field} className="form-control" maxLength={max}
                required={field === 'name' || field === 'unit'} value={form[field]}
                onChange={event => setForm(current => current && ({ ...current, [field]: event.target.value }))} />
            </div>)}
          </div>
          <label className="form-check mt-3"><input className="form-check-input" type="checkbox" checked={form.isActive}
            onChange={event => setForm(current => current && ({ ...current, isActive: event.target.checked }))} /> Active</label>
        </fieldset>
        {uncertain && <p className="alert alert-warning" role="alert">Save status unknown. Return to the catalogue and check before trying again.</p>}
        <div className="form-footer">
          <button type="button" className="btn btn-outline-secondary" disabled={saving}
            onClick={() => { setForm(null); setReload(value => value + 1) }}>{uncertain ? 'Review catalogue' : 'Cancel'}</button>
          <ActionButton busy={saving} disabled={uncertain}>Save Product</ActionButton>
        </div>
      </form> : <>
        <form className="d-flex flex-wrap gap-2 mb-3" onSubmit={applySearch}>
          <input type="search" className="form-control w-auto flex-grow-1" aria-label="Search global products"
            placeholder="Product name or barcode" maxLength={150} value={search} onChange={event => setSearch(event.target.value)} />
          <button type="submit" className="btn btn-outline-primary">Search</button>
        </form>
        {loading && <p role="status">Loading products…</p>}
        {error && <div className="alert alert-danger" role="alert">{error}{' '}
          <button type="button" className="btn btn-sm btn-outline-secondary" onClick={() => setReload(value => value + 1)}>Retry</button></div>}
        {result && <><div className="table-responsive"><table className="table table-sm align-middle">
          <thead><tr><th>Product</th><th>Barcode</th><th>Brand</th><th>Pack / Unit</th><th>Status</th><th>Action</th></tr></thead>
          <tbody>{result.items.map(product => <tr key={product.id}>
            <td>{product.name}<small className="d-block">{product.category}</small></td>
            <td>{product.barcode ?? '—'}</td><td>{product.brand || '—'}</td>
            <td>{[product.packSize, product.unit].filter(Boolean).join(' / ')}</td>
            <td>{product.isActive ? 'Active' : 'Inactive'}</td>
            <td><button type="button" className="btn btn-sm btn-outline-primary" onClick={() => open(product)}>Edit</button></td>
          </tr>)}</tbody>
        </table></div>
        {result.items.length === 0 && <p>No products found.</p>}
        <div className="d-flex gap-2 justify-content-end">
          <button type="button" className="btn btn-sm btn-outline-secondary" disabled={!history.length} onClick={() => {
            setFilter(current => ({ ...current, afterId: history[history.length - 1] ?? 0 }))
            setHistory(current => current.slice(0, -1))
          }}>Previous</button>
          <button type="button" className="btn btn-sm btn-outline-secondary" disabled={result.nextAfterId === null} onClick={() => {
            setHistory(current => [...current, filter.afterId])
            setFilter(current => ({ ...current, afterId: result.nextAfterId ?? 0 }))
          }}>Next</button>
        </div></>}
      </>}
    </section>
  </>
}
