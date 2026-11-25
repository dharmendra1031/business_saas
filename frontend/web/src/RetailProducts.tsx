import { useEffect, useLayoutEffect, useRef, useState, type FormEvent } from 'react'
import { api, post, SessionExpiredError, UncertainWriteError } from './api'
import { Package, Plus, Search } from 'lucide-react'
import { classificationPath, classificationTrail } from './retailClassification'
import { ActionButton, useActionFeedback } from './ActionFeedback'
import { moneyCents } from './posMoney'
import { MasterLookup } from './LookupDialog'

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
  conversions?: { unitId: number; factor: number; salePrice?: number | null }[]
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
  isActive?: boolean
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
  conversions: { unitId: number; factor: string; salePrice: string }[]
  brandId: number
  salePrice: string
  reorderLevel: string
  taxClassification: string
  isActive: boolean
}

const initialForm: ProductForm = {
  name: '', sku: '', barcode: '', categoryId: 0, unitId: 0, conversions: [], brandId: 0,
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
  const { run } = useActionFeedback()
  const [products, setProducts] = useState<Product[]>([])
  const [options, setOptions] = useState<CatalogOptions>({ categories: [], units: [], brands: [] })
  const [inlineType, setInlineType] = useState<'categories' | 'units' | 'brands' | null>(null)
  const [inlineName, setInlineName] = useState('')
  const [inlineCode, setInlineCode] = useState('')
  const [inlineParent, setInlineParent] = useState(0)
  const [inlineSaving, setInlineSaving] = useState(false)
  const [inlineNeedsReview, setInlineNeedsReview] = useState(false)
  const [inlineError, setInlineError] = useState('')
  const masterDialog = useRef<HTMLDialogElement>(null)
  const masterSaving = useRef(false)
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
  const [loadError, setLoadError] = useState('')
  const [optionsError, setOptionsError] = useState('')
  const [optionsReload, setOptionsReload] = useState(0)
  const [skuError, setSkuError] = useState('')
  const firstInput = useRef<HTMLInputElement>(null)

  const basePath = `/businesses/${businessId}/products`

  useEffect(() => {
    let active = true
    setOptionsLoading(true)
    setOptions({ categories: [], units: [], brands: [] })
    setOptionsError('')
    api<CatalogOptions>(basePath + '/options?includeInactiveCategories=true')
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
      categoryId: options.categories.find(item => item.parentId == null && item.isActive && item.name === 'General')?.id
        ?? options.categories.find(item => item.parentId == null && item.isActive)?.id ?? 0,
      unitId: options.units.find(item => item.code === 'ITEM')?.id
        ?? options.units[0]?.id ?? 0
    })
    setSkuError('')
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
      conversions: (item.conversions ?? []).map(x => ({ unitId: x.unitId, factor: String(x.factor), salePrice: x.salePrice == null ? '' : String(x.salePrice) })),
      brandId: item.brandId ?? 0,
      salePrice: item.salePrice.toFixed(2),
      reorderLevel: item.reorderLevel == null ? '' : String(item.reorderLevel),
      taxClassification: item.taxClassification,
      isActive: item.isActive
    })
    setSkuError('')
    setFormOpen(true)
  }

  function changeForm<K extends keyof ProductForm>(field: K, value: ProductForm[K]) {
    setForm(previous => ({ ...previous, [field]: value }))
    if (field === 'sku' || field === 'barcode') setSkuError('')
  }

  const conversionValid = form.conversions.length <= 10 &&
    new Set(form.conversions.map(x => x.unitId)).size === form.conversions.length &&
    form.conversions.every(x => x.unitId > 0 && x.unitId !== form.unitId &&
      Number(x.factor) >= 0.001 && Number(x.factor) <= 9999999.999 &&
      /^\d+(?:\.\d{0,3})?$/.test(x.factor) &&
      (x.salePrice === '' || (moneyCents(x.salePrice) > 0 && moneyCents(x.salePrice) <= 999999999999)))
  function changeConversion(index: number, value: Partial<ProductForm['conversions'][number]>) {
    changeForm('conversions', form.conversions.map((x, i) => i === index ? { ...x, ...value } : x))
  }

  async function save(event: FormEvent) {
    event.preventDefault()
    if (!canOperate || saving || inlineSaving || inlineType || optionsLoading || !form.categoryId || !form.unitId || !conversionValid) return

    setSaving(true)
    setSkuError('')

    const body = {
      ...form,
      name: form.name.trim(),
      sku: form.sku.trim().toUpperCase(),
      barcode: form.barcode.trim() || null,
      brandId: form.brandId || null,
      conversions: form.conversions.map(x => ({ unitId: x.unitId, factor: Number(x.factor),
        salePrice: x.salePrice === '' ? null : moneyCents(x.salePrice) / 100 })),
      taxClassification: form.taxClassification.trim().toUpperCase(),
      salePrice: Number(form.salePrice),
      reorderLevel: form.reorderLevel.trim() === '' ? null : Number(form.reorderLevel),
      ...(editingId === null ? { isActive: true } : {})
    }

    try {
      await run('Saving product…',
        editingId === null ? 'Product added. Set its opening quantity from Stock when ready.'
          : 'Product updated successfully.', async () => {
          if (editingId === null)
            await post(basePath, body)
          else
            await api(`${basePath}/${editingId}`, { method: 'PUT', body: JSON.stringify(body) })
        })
      setFormOpen(false)
      setEditingId(null)
      setForm(initialForm)
      setReload(value => value + 1)
    } catch (cause) {
      // Shared feedback reports the failure. Only SKU/barcode gets a field hint.
      if (!(cause instanceof SessionExpiredError)) {
        const message = errorMessage(cause)
        if (message.toLowerCase().includes('sku or barcode')) setSkuError(message)
      }
    } finally {
      setSaving(false)
    }
  }


  useLayoutEffect(() => {
    if (!inlineType || !formOpen || !canOperate) return
    const dialog = masterDialog.current
    if (!dialog) return
    dialog.showModal()
    dialog.querySelector<HTMLInputElement>('input')?.focus()
    return () => { dialog.close() }
  }, [inlineType, formOpen, canOperate])

  function closeInline() {
    masterDialog.current?.close()
    setInlineType(null)
    setInlineError('')
  }

  function beginInline(type: 'categories' | 'units' | 'brands') {
    setInlineType(type)
    setInlineError('')
    setInlineName('')
    setInlineCode('')
    setInlineParent(type === 'categories' ? selectedSubgroup : 0)
    setInlineNeedsReview(false)
  }

  const inlineValid = inlineName.trim().length >= 2 &&
    (inlineType !== 'units' || /^[A-Z0-9-]{1,12}$/.test(inlineCode.trim())) &&
    (inlineType !== 'categories' || inlineParent > 0)

  async function addInline(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (!inlineType || !canOperate || masterSaving.current || inlineSaving || saving || inlineNeedsReview || !inlineValid) return
    masterSaving.current = true
    setInlineError('')
    const type = inlineType
    const name = inlineName.trim()
    const code = inlineCode.trim().toUpperCase()
    const parentId = inlineParent
    setInlineSaving(true)
    let added: { id: number }
    try {
      added = await post<{ id: number }>(`/businesses/${businessId}/${type}`, {
        name,
        ...(type === 'units' ? { code } : {}),
        ...(type === 'categories' ? { parentId } : {})
      })
    } catch (cause) {
      if (cause instanceof UncertainWriteError) setInlineNeedsReview(true)
      else if (!(cause instanceof SessionExpiredError)) setInlineError(errorMessage(cause))
      // Never auto-retry a possibly committed create. Retain inputs for review.
      masterSaving.current = false
      setInlineSaving(false)
      return
    }

    // The master was committed. Close the dialog before the follow-up
    // GET: a failed refresh must not offer a second Create for the same item.
    const field = type === 'categories' ? 'categoryId' : type === 'units' ? 'unitId' : 'brandId'
    const option: CatalogOption = { id: added.id, name, isActive: true,
      ...(type === 'units' ? { code } : {}),
      ...(type === 'categories' ? { parentId } : {}) }
    setOptions(current => ({ ...current, [type]: [...current[type], option] }))
    setForm(current => ({ ...current, [field]: added.id }))
    closeInline()
    setInlineNeedsReview(false)
    setInlineName('')
    setInlineCode('')
    setInlineParent(0)
    try {
      const updated = await api<CatalogOptions>(basePath + '/options?includeInactiveCategories=true')
      setOptions({ ...updated, brands: updated.brands ?? [] })
      setOptionsError('')
    } catch (cause) {
      if (!(cause instanceof SessionExpiredError))
        setOptionsError('Master created and selected, but the options list could not refresh. Use Retry setup.')
    } finally {
      masterSaving.current = false
      setInlineSaving(false)
    }
  }

  const unitSelected = options.units.some(item => item.id === form.unitId)
  const activeNodes = options.categories.filter(item => item.isActive)
  const groups = activeNodes.filter(item => item.parentId == null)
  const selectedTrail = classificationTrail(options.categories, form.categoryId)
  const selectedGroup = selectedTrail[0]?.id ?? 0
  const selectedSubgroup = selectedTrail[1]?.id ?? 0
  const selectedCategory = selectedTrail[2]?.id ?? 0
  const subgroups = activeNodes.filter(item => item.parentId === selectedGroup)
  const categories = activeNodes.filter(item => item.parentId === selectedSubgroup && selectedSubgroup > 0)
  const filterTrail = classificationTrail(options.categories, categoryFilter)
  const filterGroup = filterTrail[0]?.id ?? 0
  const filterSubgroup = filterTrail[1]?.id ?? 0
  const filterCategory = filterTrail[2]?.id ?? 0
  const canCreate = groups.length > 0 && options.units.length > 0

  return (
    <section className="content-section retail-products business-module-card" aria-label="Product master">
      <div className="masters-heading business-module-heading">
        <h2>Products</h2>
        <div className="business-module-actions">
          {!formOpen && !loading && !loadError && total > 0 && <span className="business-module-count">{total.toLocaleString()} products</span>}
          {canOperate && !formOpen && <button type="button" className="btn btn-primary"
            disabled={!canCreate || optionsLoading} onClick={startCreate}><Plus size={17} /> Add Product</button>}
        </div>
      </div>

      {!canOperate && <div className="alert alert-warning" role="status">
        Your subscription is inactive. Products are read-only.
      </div>}
      {loadError && <div className="alert alert-danger" role="alert">
        {loadError} <button type="button" className="btn btn-sm btn-outline-secondary ms-2"
          onClick={() => setReload(value => value + 1)}>Retry</button>
      </div>}
      {optionsError && <div className="alert alert-danger" role="alert">
        {optionsError} <button type="button" className="btn btn-sm btn-outline-secondary ms-2"
          onClick={() => setOptionsReload(value => value + 1)}>Retry setup</button>
      </div>}
      {!optionsLoading && !optionsError && !canCreate && <p className="alert alert-info" role="status">
        A group and unit are required. Add them from Product Masters first.
      </p>}

      {formOpen && canOperate && <form className="product-form" aria-busy={saving || inlineSaving} onSubmit={event => void save(event)}>
        <h3>{editingId === null ? 'New product' : 'Edit product'}</h3>
        <fieldset className="form-grid" disabled={saving || inlineSaving}>
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
          <div>
            <label className="form-label" htmlFor="product-group">Group *</label>
            <MasterLookup id="product-group" title="Select group" value={selectedGroup} emptyLabel="Select group"
              options={[...groups,
                ...(selectedGroup > 0 && !groups.some(item => item.id === selectedGroup)
                  ? [{ id: selectedGroup, name: 'Previous selection (inactive)' }] : [])]}
              disabled={saving || inlineSaving} onChange={id => changeForm('categoryId', id)} />
          </div>
          {selectedGroup > 0 && (subgroups.length > 0 || selectedSubgroup > 0) && <div>
            <label className="form-label" htmlFor="product-subgroup">Subgroup (optional)</label>
            <MasterLookup id="product-subgroup" title="Select subgroup" value={selectedSubgroup} emptyLabel="No subgroup"
              options={[{ id: 0, name: 'No subgroup' }, ...subgroups,
                ...(selectedSubgroup > 0 && !subgroups.some(item => item.id === selectedSubgroup)
                  ? [{ id: selectedSubgroup, name: 'Previous selection (inactive)' }] : [])]}
              disabled={saving || inlineSaving} onChange={id => changeForm('categoryId', id || selectedGroup)} />
          </div>}
          {selectedSubgroup > 0 && <div>
            <label className="form-label" htmlFor="product-category">Category (optional)</label>
            <MasterLookup id="product-category" title="Select category" value={selectedCategory} emptyLabel="No category"
              options={[{ id: 0, name: 'No category' }, ...categories,
                ...(selectedCategory > 0 && !categories.some(item => item.id === selectedCategory)
                  ? [{ id: selectedCategory, name: 'Previous selection (inactive)' }] : [])]}
              disabled={saving || inlineSaving} onChange={id => changeForm('categoryId', id || selectedSubgroup)} />
            <button type="button" className="btn btn-sm btn-link"
              onClick={() => beginInline('categories')}>+ Add category</button>
          </div>}
          <div>
            <label className="form-label" htmlFor="product-unit">Stock unit *</label>
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
            <MasterLookup id="product-brand" title="Select brand" value={form.brandId} emptyLabel="No brand"
              options={[{ id: 0, name: 'No brand' }, ...options.brands,
                ...(form.brandId > 0 && !options.brands.some(item => item.id === form.brandId)
                  ? [{ id: form.brandId, name: 'Previous selection (inactive)' }] : [])]}
              disabled={saving || inlineSaving} onChange={id => changeForm('brandId', id)} />
            <button type="button" className="btn btn-sm btn-link" onClick={() => beginInline('brands')}>+ Add brand</button>
          </div>
          <div>
            <label className="form-label" htmlFor="product-sku">Item code {editingId === null ? '(auto if blank)' : '*'}</label>
            <input id="product-sku" className="form-control" required={editingId !== null} maxLength={50}
              minLength={2} pattern="[a-zA-Z0-9._/-]{2,50}" autoComplete="off"
              aria-invalid={Boolean(skuError)} aria-describedby={skuError ? 'product-code-error' : undefined}
              value={form.sku} onChange={event => changeForm('sku', event.target.value.toUpperCase())} />
            {skuError && <div id="product-code-error" className="field-help text-danger">{skuError}</div>}
          </div>
          <div>
            <label className="form-label" htmlFor="product-barcode">Barcode</label>
            <input id="product-barcode" className="form-control" maxLength={64}
              autoComplete="off" value={form.barcode}
              onChange={event => changeForm('barcode', event.target.value)} />
          </div>
          <div>
            <label className="form-label" htmlFor="product-reorder">Low stock alert</label>
            <input id="product-reorder" className="form-control" type="number" min="0"
              max="9999999999.999" step="0.001" inputMode="decimal"
              value={form.reorderLevel} onChange={event => changeForm('reorderLevel', event.target.value)} />
            <div className="field-help">Leave blank to disable.</div>
          </div>
          {editingId !== null && <div className="form-check form-wide">
            <input id="product-status" type="checkbox" className="form-check-input" checked={form.isActive}
              onChange={event => changeForm('isActive', event.target.checked)} />
            <label className="form-check-label" htmlFor="product-status">Active</label>
          </div>}
          <div className="form-wide">
            <details open={form.conversions.length > 0 || undefined}>
              <summary>Alternate units (optional)</summary>
              <p className="field-help">Example: 1 box = 12 pieces. Leave price blank to use the calculated price.</p>
              {form.conversions.length > 0 && <div className="table-responsive">
                <table className="table table-sm align-middle">
                  <thead><tr><th>Alternate unit</th><th>Quantity per pack</th><th>Selling price (optional)</th><th>Action</th></tr></thead>
                  <tbody>{form.conversions.map((line, index) => <tr key={index}>
                    <td><select className="form-select form-select-sm" aria-label={`Alternate unit ${index + 1}`}
                      required value={line.unitId} onChange={event => changeConversion(index, { unitId: Number(event.target.value), factor: '', salePrice: '' })}>
                      <option value={0}>Select unit</option>
                      {line.unitId > 0 && !options.units.some(x => x.id === line.unitId) &&
                        <option value={line.unitId}>Previous unit (inactive)</option>}
                      {options.units.filter(x => x.id !== form.unitId &&
                        (x.id === line.unitId || !form.conversions.some(row => row.unitId === x.id)))
                        .map(x => <option key={x.id} value={x.id}>{x.name} ({x.code})</option>)}
                    </select></td>
                    <td><input className="form-control form-control-sm" type="number" inputMode="decimal"
                      aria-label={`Conversion factor ${index + 1}`} min="0.001" max="9999999.999" step="0.001"
                      required value={line.factor} onChange={event => changeConversion(index, { factor: event.target.value })} /></td>
                    <td><input className="form-control form-control-sm" type="number" inputMode="decimal"
                      aria-label={`Alternate selling price ${index + 1}`} min="0.01" max="9999999999.99" step="0.01"
                      placeholder="Auto" value={line.salePrice}
                      onChange={event => changeConversion(index, { salePrice: event.target.value })} /></td>
                    <td><button type="button" className="btn btn-sm btn-outline-secondary"
                      onClick={() => changeForm('conversions', form.conversions.filter((_, i) => i !== index))}>Remove</button></td>
                  </tr>)}</tbody>
                </table>
              </div>}
              <button type="button" className="btn btn-sm btn-outline-secondary"
                disabled={!form.unitId || form.conversions.length >= 10 ||
                  !options.units.some(x => x.id !== form.unitId && !form.conversions.some(row => row.unitId === x.id))}
                onClick={() => changeForm('conversions', [...form.conversions, { unitId: 0, factor: '', salePrice: '' }])}>
                + Add alternate unit
              </button>
              {!conversionValid && <p className="field-help">Choose a different unit and enter a valid quantity and price.</p>}
            </details>
          </div>
        </fieldset>
        <div className="form-footer">
          <button type="button" className="btn btn-outline-secondary" disabled={saving || inlineSaving}
            onClick={() => { setFormOpen(false); setSkuError('') }}>Cancel</button>
          <ActionButton busy={saving} disabled={optionsLoading || inlineSaving || !!inlineType || !form.categoryId || !form.unitId || !conversionValid}>Save product</ActionButton>
        </div>
      </form>}

      {formOpen && canOperate && inlineType && <dialog ref={masterDialog} className="entry-dialog"
        aria-labelledby="master-dialog-heading"
        onCancel={event => { event.preventDefault(); if (!masterSaving.current) closeInline() }}
        onKeyDownCapture={event => {
          if (event.key === 'Escape') {
            event.preventDefault()
            event.stopPropagation()
            if (!masterSaving.current) closeInline()
          }
        }}>
        <h3 id="master-dialog-heading" className="h5 mb-3">Add {inlineType === 'categories' ? 'category' : inlineType === 'units' ? 'unit' : 'brand'}</h3>
        <form aria-busy={inlineSaving} onSubmit={event => void addInline(event)}>
          <fieldset disabled={inlineSaving || inlineNeedsReview}>
            {inlineType === 'categories' && <p className="small mb-2">Subgroup: {classificationPath(options.categories, inlineParent)}</p>}
            <label className="form-label" htmlFor="inline-master-name">Name *</label>
            <input id="inline-master-name" className="form-control" required minLength={2} maxLength={80}
              value={inlineName} onChange={event => setInlineName(event.target.value)} />
            {inlineType === 'units' && <div className="mt-3">
              <label className="form-label" htmlFor="inline-master-code">Unit code *</label>
              <input id="inline-master-code" className="form-control" required maxLength={12}
                value={inlineCode} onChange={event => setInlineCode(event.target.value.toUpperCase())} />
            </div>}
          </fieldset>
          {inlineError && <p role="alert" className="text-danger small mt-2">{inlineError}</p>}
          {inlineNeedsReview && <p role="alert" className="alert alert-warning mt-2">
            Creation status is unknown. Check Product Masters before attempting this again.
          </p>}
          <div className="d-flex justify-content-end gap-2 mt-3">
            <button type="button" className="btn btn-outline-secondary" disabled={inlineSaving} onClick={closeInline}>Cancel</button>
            <ActionButton busy={inlineSaving} busyLabel="Adding…" disabled={!inlineValid || inlineNeedsReview || saving}>Add & select</ActionButton>
          </div>
        </form>
      </dialog>}

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
            <label className="visually-hidden" htmlFor="product-filter-group">Group</label>
            <MasterLookup id="product-filter-group" title="Filter groups" value={filterGroup} emptyLabel="All groups"
              options={[{ id: 0, name: 'All groups' }, ...options.categories.filter(item => item.parentId == null)
                .map(item => ({ id: item.id, name: item.name + (item.isActive ? '' : ' (inactive)') }))]}
              onChange={id => { setPage(1); setCategoryFilter(id) }} />
          </div>
          {filterGroup > 0 && <div>
            <label className="visually-hidden" htmlFor="product-filter-subgroup">Subgroup</label>
            <MasterLookup id="product-filter-subgroup" title="Filter subgroups" value={filterSubgroup} emptyLabel="All subgroups"
              options={[{ id: 0, name: 'All subgroups' }, ...options.categories.filter(item => item.parentId === filterGroup && filterGroup > 0)
                .map(item => ({ id: item.id, name: item.name + (item.isActive ? '' : ' (inactive)') }))]}
              onChange={id => { setPage(1); setCategoryFilter(id || filterGroup) }} />
          </div>}
          {filterSubgroup > 0 && <div>
            <label className="visually-hidden" htmlFor="product-filter-category">Category</label>
            <MasterLookup id="product-filter-category" title="Filter categories" value={filterCategory} emptyLabel="All categories"
              options={[{ id: 0, name: 'All categories' }, ...options.categories.filter(item => item.parentId === filterSubgroup && filterSubgroup > 0)
                .map(item => ({ id: item.id, name: item.name + (item.isActive ? '' : ' (inactive)') }))]}
              onChange={id => { setPage(1); setCategoryFilter(id || filterSubgroup) }} />
          </div>}
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
