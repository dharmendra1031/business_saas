import { useEffect, useRef, useState, type FormEvent } from 'react'
import { api, post, SessionExpiredError } from './api'
import { Package, Plus, Search } from 'lucide-react'

interface Product {
  id: number
  name: string
  sku: string
  barcode: string | null
  categoryId: number
  categoryName: string
  brandId?: number | null
  brandName?: string | null
  unitId: number
  unitCode: string
  salePrice: number
  reorderLevel: number | null
  taxClassification: string
  isActive: boolean
}

interface ProductPage {
  items: Product[]
  total: number
  page: number
  pageSize: number
}

interface CatalogOption {
  id: number
  name: string
  code?: string
  parentId?: number | null
}

interface CatalogOptions {
  categories: CatalogOption[]
  units: CatalogOption[]
  brands: CatalogOption[]
}

interface ProductForm {
  name: string
  sku: string
  barcode: string
  categoryId: number
  unitId: number
  brandId: number
  salePrice: string
  reorderLevel: string
  taxClassification: string
  isActive: boolean
}

const initialForm: ProductForm = {
  name: '', sku: '', barcode: '', categoryId: 0, unitId: 0, brandId: 0,
  salePrice: '', reorderLevel: '', taxClassification: '', isActive: true
}

function errorMessage(error: unknown): string {
  return error instanceof Error ? error.message : 'Request failed. Please try again.'
}

export default function RetailProducts({ businessId, canOperate, currencyCode }: {
  businessId: number
  canOperate: boolean
  currencyCode: string
}) {
  const [products, setProducts] = useState<Product[]>([])
  const [options, setOptions] = useState<CatalogOptions>({ categories: [], units: [], brands: [] })
  const [inlineType, setInlineType] = useState<'categories' | 'units' | 'brands' | null>(null)
  const [inlineName, setInlineName] = useState('')
  const [inlineCode, setInlineCode] = useState('')
  const [inlineParent, setInlineParent] = useState(0)
  const [inlineSaving, setInlineSaving] = useState(false)
  const [page, setPage] = useState(1)
  const [total, setTotal] = useState(0)
  const [search, setSearch] = useState('')
  const [query, setQuery] = useState('')
  const [status, setStatus] = useState('all')
  const [categoryFilter, setCategoryFilter] = useState(0)
  const [form, setForm] = useState<ProductForm>(initialForm)
  const [editingId, setEditingId] = useState<number | null>(null)
  const [formOpen, setFormOpen] = useState(false)
  const [reload, setReload] = useState(0)
  const [loading, setLoading] = useState(true)
  const [optionsLoading, setOptionsLoading] = useState(true)
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState('')
  const [loadError, setLoadError] = useState('')
  const [optionsError, setOptionsError] = useState('')
  const [optionsReload, setOptionsReload] = useState(0)
  const [notice, setNotice] = useState('')
  const [skuError, setSkuError] = useState('')
  const firstInput = useRef<HTMLInputElement>(null)

  const basePath = `/businesses/${businessId}/products`

  useEffect(() => {
    let active = true
    setOptionsLoading(true)
    setOptions({ categories: [], units: [], brands: [] })
    setOptionsError('')
    api<CatalogOptions>(basePath + '/options')
      .then(value => { if (active) setOptions({ ...value, brands: value.brands ?? [] }) })
      .catch(cause => { if (active && !(cause instanceof SessionExpiredError)) setOptionsError(errorMessage(cause)) })
      .finally(() => { if (active) setOptionsLoading(false) })
    return () => { active = false }
  }, [basePath, optionsReload])

  useEffect(() => {
    let active = true
    setLoading(true)
    setProducts([])
    setTotal(0)
    setLoadError('')
    const params = new URLSearchParams({
      search: query, status, page: String(page),
      ...(categoryFilter ? { categoryId: String(categoryFilter) } : {})
    })
    api<ProductPage>(basePath + '?' + params)
      .then(value => {
        if (!active) return
        setProducts(value.items)
        setTotal(value.total)
      })
      .catch(cause => { if (active && !(cause instanceof SessionExpiredError)) setLoadError(errorMessage(cause)) })
      .finally(() => { if (active) setLoading(false) })
    return () => { active = false }
  }, [basePath, query, status, page, categoryFilter, reload])

  useEffect(() => { if (formOpen) firstInput.current?.focus() }, [formOpen, editingId])

  useEffect(() => {
    setPage(1)
    setSearch('')
    setQuery('')
    setStatus('all')
    setCategoryFilter(0)
    setFormOpen(false)
    setEditingId(null)
  }, [businessId])

  function startCreate() {
    setEditingId(null)
    setInlineType(null)
    setForm({
      ...initialForm,
      categoryId: options.categories.find(item => item.name === 'General')?.id
        ?? options.categories[0]?.id ?? 0,
      unitId: options.units.find(item => item.code === 'ITEM')?.id
        ?? options.units[0]?.id ?? 0
    })
    setSkuError('')
    setError('')
    setNotice('')
    setFormOpen(true)
  }

  function startEdit(item: Product) {
    setEditingId(item.id)
    setInlineType(null)
    setForm({
      name: item.name,
      sku: item.sku,
      barcode: item.barcode ?? '',
      categoryId: item.categoryId,
      unitId: item.unitId,
      brandId: item.brandId ?? 0,
      salePrice: item.salePrice.toFixed(2),
      reorderLevel: item.reorderLevel == null ? '' : String(item.reorderLevel),
      taxClassification: item.taxClassification,
      isActive: item.isActive
    })
    setSkuError('')
    setError('')
    setNotice('')
    setFormOpen(true)
  }

  function changeForm<K extends keyof ProductForm>(field: K, value: ProductForm[K]) {
    setForm(previous => ({ ...previous, [field]: value }))
    if (field === 'sku' || field === 'barcode') setSkuError('')
  }

  async function save(event: FormEvent) {
    event.preventDefault()
    if (!canOperate || saving) return

    setSaving(true)
    setError('')
    setSkuError('')
    setNotice('')

    const body = {
      ...form,
      name: form.name.trim(),
      sku: form.sku.trim().toUpperCase(),
      barcode: form.barcode.trim() || null,
      brandId: form.brandId || null,
      taxClassification: form.taxClassification.trim().toUpperCase(),
      salePrice: Number(form.salePrice),
      reorderLevel: form.reorderLevel.trim() === '' ? null : Number(form.reorderLevel),
      ...(editingId === null ? { isActive: true } : {})
    }

    try {
      if (editingId === null)
        await post(basePath, body)
      else
        await api(`${basePath}/${editingId}`, { method: 'PUT', body: JSON.stringify(body) })

      setFormOpen(false)
      setEditingId(null)
      setForm(initialForm)
      setNotice(editingId === null
        ? 'Product added. Set its opening quantity from Stock when ready.'
        : 'Product updated successfully.')
      setReload(value => value + 1)
    } catch (cause) {
      if (!(cause instanceof SessionExpiredError)) {
        const message = errorMessage(cause)
        setError(message)
        if (message.toLowerCase().includes('sku or barcode')) setSkuError(message)
      }
    } finally {
      setSaving(false)
    }
  }


  function categoryPath(id: number): string {
    const names: string[] = []
    const seen = new Set<number>()
    let current: number | null = id
    while (current && !seen.has(current) && names.length < 3) {
      seen.add(current)
      const category = options.categories.find(x => x.id === current)
      if (!category) break
      names.unshift(category.name)
      current = category.parentId ?? null
    }
    return names.join(' / ')
  }

  function beginInline(type: 'categories' | 'units' | 'brands') {
    setInlineType(type === inlineType ? null : type)
    setInlineName('')
    setInlineCode('')
    setInlineParent(0)
    setError('')
  }

  async function addInline() {
    if (!inlineType || !canOperate || inlineSaving) return
    setInlineSaving(true)
    setError('')
    try {
      const added = await post<{ id: number }>(`/businesses/${businessId}/${inlineType}`, {
        name: inlineName.trim(),
        ...(inlineType === 'units' ? { code: inlineCode.trim().toUpperCase() } : {}),
        ...(inlineType === 'categories' ? { parentId: inlineParent || null } : {})
      })
      const updated = await api<CatalogOptions>(basePath + '/options')
      setOptions({ ...updated, brands: updated.brands ?? [] })
      const field = inlineType === 'categories' ? 'categoryId' : inlineType === 'units' ? 'unitId' : 'brandId'
      setForm(value => ({ ...value, [field]: added.id }))
      setNotice(`${inlineType === 'categories' ? 'Category' : inlineType === 'units' ? 'Unit' : 'Brand'} added to this product.`)
      setInlineType(null)
      setInlineName('')
      setInlineCode('')
      setInlineParent(0)
    } catch (cause) {
      if (!(cause instanceof SessionExpiredError))
        setError(errorMessage(cause))
      setOptionsReload(value => value + 1)
    } finally {
      setInlineSaving(false)
    }
  }

  const categorySelected = options.categories.some(item => item.id === form.categoryId)
  const unitSelected = options.units.some(item => item.id === form.unitId)
  const canCreate = options.categories.length > 0 && options.units.length > 0

  return (
    <section className="content-section retail-products business-module-card" aria-label="Product master">
      <div className="masters-heading business-module-heading">
        <h2>Products</h2>
        <div className="business-module-actions">
          {!loading && !loadError && total > 0 && <span className="business-module-count">{total.toLocaleString()} products</span>}
          {canOperate && !formOpen && <button type="button" className="btn btn-primary"
            disabled={!canCreate || optionsLoading} onClick={startCreate}><Plus size={17} /> Add Product</button>}
        </div>
      </div>

      {!canOperate && <div className="alert alert-warning" role="status">
        Your subscription is inactive. Products are read-only.
      </div>}
      {error && <div className="alert alert-danger" role="alert">{error}</div>}
      {loadError && <div className="alert alert-danger" role="alert">
        {loadError} <button type="button" className="btn btn-sm btn-outline-secondary ms-2"
          onClick={() => setReload(value => value + 1)}>Retry</button>
      </div>}
      {optionsError && <div className="alert alert-danger" role="alert">
        {optionsError} <button type="button" className="btn btn-sm btn-outline-secondary ms-2"
          onClick={() => setOptionsReload(value => value + 1)}>Retry setup</button>
      </div>}
      {notice && <div className="alert alert-success" role="status">{notice}</div>}
      {!optionsLoading && !optionsError && !canCreate && <p className="alert alert-info" role="status">
        A category and unit are required. Add them from Product Masters first.
      </p>}

      {formOpen && canOperate && <form className="product-form" onSubmit={event => void save(event)}>
        <h3>{editingId === null ? 'New product' : 'Edit product'}</h3>
        <div className="form-grid">
          <div className="form-wide">
            <label className="form-label" htmlFor="product-name">Product name *</label>
            <input ref={firstInput} id="product-name" className="form-control" required
              minLength={2} maxLength={150} value={form.name}
              onChange={event => changeForm('name', event.target.value)} />
          </div>
          <div>
            <label className="form-label" htmlFor="product-price">Sale price ({currencyCode}) *</label>
            <input id="product-price" className="form-control" type="number" min={0}
              max={9999999999.99} step="0.01" inputMode="decimal" required
              value={form.salePrice} onChange={event => changeForm('salePrice', event.target.value)} />
          </div>
          <details className="form-wide" key={editingId ?? 'new'} open={editingId !== null || !canCreate}>
            <summary>More details (category, unit, brand, SKU, barcode, stock alerts, tax)</summary>
            <div className="form-grid mt-3">
          <div>
            <label className="form-label" htmlFor="product-sku">SKU {editingId === null ? '(auto if blank)' : '*'}</label>
            <input id="product-sku" className="form-control" required={editingId !== null} maxLength={50}
              minLength={2} pattern="[a-zA-Z0-9._/-]{2,50}" autoComplete="off"
              aria-invalid={Boolean(skuError)} aria-describedby={skuError ? 'product-code-error' : undefined}
              value={form.sku} onChange={event => changeForm('sku', event.target.value.toUpperCase())} />
            {skuError && <div id="product-code-error" className="field-help text-danger">{skuError}</div>}
          </div>
          <div>
            <label className="form-label" htmlFor="product-barcode">Barcode (optional)</label>
            <input id="product-barcode" className="form-control" maxLength={64}
              autoComplete="off" value={form.barcode}
              aria-describedby="barcode-help"
              onChange={event => changeForm('barcode', event.target.value)} />
            <div id="barcode-help" className="field-help">Scan a barcode or enter an existing code. No barcode is generated.</div>
          </div>
          <div>
            <label className="form-label" htmlFor="product-category">Category *</label>
            <select id="product-category" className="form-select" required value={form.categoryId}
              onChange={event => changeForm('categoryId', Number(event.target.value))}>
              <option value={0}>Select category</option>
              {!categorySelected && form.categoryId > 0 &&
                <option value={form.categoryId}>Previous category (inactive)</option>}
              {options.categories.map(item => <option key={item.id} value={item.id}>{categoryPath(item.id)}</option>)}
            </select>
            <button type="button" className="btn btn-sm btn-link" onClick={() => beginInline('categories')}>+ Add category</button>
          </div>
          <div>
            <label className="form-label" htmlFor="product-unit">Stock / sales unit *</label>
            <select id="product-unit" className="form-select" required value={form.unitId}
              onChange={event => changeForm('unitId', Number(event.target.value))}>
              <option value={0}>Select unit</option>
              {!unitSelected && form.unitId > 0 &&
                <option value={form.unitId}>Previous unit (inactive)</option>}
              {options.units.map(item => <option key={item.id} value={item.id}>
                {item.name} ({item.code})
              </option>)}
            </select>
            <button type="button" className="btn btn-sm btn-link" onClick={() => beginInline('units')}>+ Add unit</button>
          </div>
          <div>
            <label className="form-label" htmlFor="product-brand">Brand (optional)</label>
            <select id="product-brand" className="form-select" value={form.brandId}
              onChange={event => changeForm('brandId', Number(event.target.value))}>
              <option value={0}>No brand</option>
              {form.brandId > 0 && !options.brands.some(x => x.id === form.brandId) &&
                <option value={form.brandId}>Previous brand (inactive)</option>}
              {options.brands.map(item => <option key={item.id} value={item.id}>{item.name}</option>)}
            </select>
            <button type="button" className="btn btn-sm btn-link" onClick={() => beginInline('brands')}>+ Add brand</button>
          </div>
          {inlineType && <div className="form-wide product-form" role="group"
            aria-label={`Add ${inlineType === 'categories' ? 'category' : inlineType === 'units' ? 'unit' : 'brand'}`}
            onKeyDown={event => { if (event.key === 'Enter') { event.preventDefault(); void addInline() } }}>
            <label className="form-label" htmlFor="inline-master-name">New {inlineType === 'categories' ? 'category' : inlineType === 'units' ? 'unit' : 'brand'} name</label>
            <input id="inline-master-name" className="form-control" minLength={2} maxLength={80}
              value={inlineName} onChange={event => setInlineName(event.target.value)} />
            {inlineType === 'units' && <>
              <label className="form-label mt-2" htmlFor="inline-master-code">Unit code</label>
              <input id="inline-master-code" className="form-control" maxLength={12}
                value={inlineCode} onChange={event => setInlineCode(event.target.value.toUpperCase())} />
            </>}
            {inlineType === 'categories' && <>
              <label className="form-label mt-2" htmlFor="inline-master-parent">Parent (optional)</label>
              <select id="inline-master-parent" className="form-select" value={inlineParent}
                onChange={event => setInlineParent(Number(event.target.value))}>
                <option value={0}>No parent</option>
                {options.categories.filter(x => categoryPath(x.id).split(' / ').length < 3)
                  .map(item => <option key={item.id} value={item.id}>{categoryPath(item.id)}</option>)}
              </select>
            </>}
            <div className="d-flex gap-2 mt-2">
              <button type="button" className="btn btn-sm btn-primary"
                disabled={inlineSaving || inlineName.trim().length < 2 ||
                  inlineType === 'units' && !/^[A-Z0-9-]{1,12}$/.test(inlineCode.trim())}
                onClick={() => void addInline()}>{inlineSaving ? 'Adding…' : 'Add & select'}</button>
              <button type="button" className="btn btn-sm btn-outline-secondary" disabled={inlineSaving}
                onClick={() => setInlineType(null)}>Cancel</button>
            </div>
          </div>}
          <div>
            <label className="form-label" htmlFor="product-reorder">Reorder level (optional)</label>
            <input id="product-reorder" className="form-control" type="number" min="0"
              max="9999999999.999" step="0.001" inputMode="decimal"
              value={form.reorderLevel} onChange={event => changeForm('reorderLevel', event.target.value)} />
            <div className="field-help">Alert when location stock reaches this quantity.
              Leave blank to disable; stock must be initialized first.</div>
          </div>
          <div>
            <label className="form-label" htmlFor="product-tax">Tax classification (optional)</label>
            <input id="product-tax" className="form-control" maxLength={30}
              pattern="[A-Za-z0-9_-]{0,30}" value={form.taxClassification}
              onChange={event => changeForm('taxClassification', event.target.value.toUpperCase())} />
            <div className="field-help">Reference code only; tax calculation is not enabled.</div>
          </div>
          {editingId !== null && <div>
            <label className="form-label" htmlFor="product-status">Product status</label>
            <select id="product-status" className="form-select" value={form.isActive ? 'active' : 'inactive'}
              onChange={event => changeForm('isActive', event.target.value === 'active')}>
              <option value="active">Active</option>
              <option value="inactive">Inactive (hide from barcode lookup)</option>
            </select>
          </div>}
            </div>
          </details>
        </div>
        <div className="form-footer">
          <button type="button" className="btn btn-outline-secondary"
            onClick={() => { setFormOpen(false); setError(''); setSkuError('') }}>Cancel</button>
          <button type="submit" className="btn btn-primary" disabled={saving || optionsLoading}>
            {saving ? 'Saving…' : 'Save product'}
          </button>
        </div>
      </form>}

      {!formOpen && <>
        <form className="product-filters" onSubmit={event => {
          event.preventDefault()
          setPage(1)
          setQuery(search.trim())
        }}>
          <div className="product-filter-search">
            <label className="visually-hidden" htmlFor="product-search">Product name, SKU or barcode</label>
            <Search className="business-filter-icon" size={18} aria-hidden="true" />
            <input id="product-search" className="form-control" type="search"
              placeholder="Search name, SKU or scan barcode"
              maxLength={150} value={search} onChange={event => setSearch(event.target.value)} />
          </div>
          <div>
            <label className="visually-hidden" htmlFor="product-filter-status">Status</label>
            <select id="product-filter-status" className="form-select" value={status}
              onChange={event => { setPage(1); setStatus(event.target.value) }}>
              <option value="all">All statuses</option>
              <option value="active">Active</option>
              <option value="inactive">Inactive</option>
            </select>
          </div>
          <div>
            <label className="visually-hidden" htmlFor="product-filter-category">Category</label>
            <select id="product-filter-category" className="form-select" value={categoryFilter}
              onChange={event => { setPage(1); setCategoryFilter(Number(event.target.value)) }}>
              <option value={0}>All categories</option>
              {options.categories.map(item => <option key={item.id} value={item.id}>{categoryPath(item.id)}</option>)}
            </select>
          </div>
          <button type="submit" className="btn btn-outline-secondary">Search</button>
        </form>

        {loading ? <div className="business-module-loading" role="status">Loading products…</div>
          : loadError ? null : products.length === 0 ? <div className="business-empty-state" role="status">
            <Package size={28} /><strong>{query || status !== 'all' || categoryFilter
              ? 'No matching products' : 'No products yet'}</strong>
            <p>{query || status !== 'all' || categoryFilter
              ? 'Adjust the name, status or category filters to find your product.'
              : 'Add your first product using the button above. Opening stock is managed in Inventory.'}</p>
          </div> : <div className="table-responsive">
            <table className="table table-sm align-middle business-record-table">
              <thead><tr><th scope="col">Product</th><th scope="col" className="product-detail-column">SKU / barcode</th>
                <th scope="col" className="product-detail-column">Category / unit</th><th scope="col" className="text-end">Sale price</th>
                <th scope="col" className="product-status-column">Status</th>{canOperate && <th scope="col" className="text-end">Action</th>}</tr></thead>
              <tbody>{products.map(item => <tr key={item.id}>
                <td><strong>{item.name}</strong>
                  {item.brandName && <small className="product-secondary">{item.brandName}</small>}
                  <small className="product-mobile-meta">
                    <span>{item.sku}</span>
                    <span className={`business-record-status ${item.isActive ? 'is-active' : 'is-inactive'}`}>
                      {item.isActive ? 'Active' : 'Inactive'}
                    </span>
                  </small>
                </td>
                <td className="product-detail-column"><span>{item.sku}</span>{item.barcode && <small className="product-secondary">{item.barcode}</small>}</td>
                <td className="product-detail-column">{item.categoryName}<small className="product-secondary">{item.unitCode}</small></td>
                <td className="text-end text-nowrap">{currencyCode} {item.salePrice.toFixed(2)}</td>
                <td className="product-status-column"><span className={`business-record-status ${item.isActive ? 'is-active' : 'is-inactive'}`}>
                  {item.isActive ? 'Active' : 'Inactive'}</span></td>
                {canOperate && <td className="text-end"><button type="button"
                  className="btn btn-sm btn-outline-secondary" onClick={() => startEdit(item)}>Edit</button></td>}
              </tr>)}</tbody>
            </table>
          </div>}

        {!loading && !loadError && total > 20 && <div className="masters-pages">
          <span className="small text-secondary">Page {page} of {Math.ceil(total / 20)}</span>
          <div className="btn-group">
            <button className="btn btn-sm btn-outline-secondary" type="button" disabled={page === 1}
              onClick={() => setPage(value => value - 1)}>Previous</button>
            <button className="btn btn-sm btn-outline-secondary" type="button" disabled={page * 20 >= total}
              onClick={() => setPage(value => value + 1)}>Next</button>
          </div>
        </div>}
      </>}
    </section>
  )
}
