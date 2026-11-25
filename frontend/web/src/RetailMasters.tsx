import { useEffect, useRef, useState, type FormEvent } from 'react'
import { api, post, SessionExpiredError } from './api'
import { classificationPath, classificationTrail } from './retailClassification'
import { ActionButton, useActionFeedback } from './ActionFeedback'
import { MasterLookup } from './LookupDialog'

type MasterType = 'groups' | 'subgroups' | 'categories' | 'brands' | 'units'

interface MasterItem {
  id: number
  name: string
  code?: string
  parentId?: number | null
  isActive: boolean
}

interface MasterPage {
  items: MasterItem[]
  total: number
  page: number
  pageSize: number
}

export default function RetailMasters({ businessId, canOperate }: {
  businessId: number
  canOperate: boolean
}) {
  const { run } = useActionFeedback()
  const [type, setType] = useState<MasterType>('groups')
  const classification = type === 'groups' || type === 'subgroups' || type === 'categories'
  const level = type === 'groups' ? 1 : type === 'subgroups' ? 2 : type === 'categories' ? 3 : 0
  const endpoint = classification ? 'categories' : type
  const [items, setItems] = useState<MasterItem[]>([])
  const [total, setTotal] = useState(0)
  const [page, setPage] = useState(1)
  const [search, setSearch] = useState('')
  const [query, setQuery] = useState('')
  const [reload, setReload] = useState(0)
  const [name, setName] = useState('')
  const [code, setCode] = useState('')
  const [parentId, setParentId] = useState(0)
  const [filterParent, setFilterParent] = useState(0)
  const [parentOptions, setParentOptions] = useState<MasterItem[]>([])
  const [active, setActive] = useState(true)
  const [editingId, setEditingId] = useState<number | null>(null)
  const [formOpen, setFormOpen] = useState(false)
  const [loading, setLoading] = useState(true)
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState('')
  const [loadError, setLoadError] = useState('')
  const firstInput = useRef<HTMLInputElement>(null)

  useEffect(() => {
    let mounted = true
    setLoading(true)
    setItems([])
    setTotal(0)
    setLoadError('')

    api<MasterPage>(`/businesses/${businessId}/${endpoint}?page=${page}&search=${encodeURIComponent(query)}${classification ? `&level=${level}${filterParent ? `&parentId=${filterParent}` : ''}` : ''}`)
      .then(result => {
        if (!mounted) return
        setItems(result.items)
        setTotal(result.total)
      })
      .catch(cause => {
        if (mounted && !(cause instanceof SessionExpiredError))
          setLoadError(cause instanceof Error ? cause.message : 'Unable to load records.')
      })
      .finally(() => { if (mounted) setLoading(false) })

    return () => { mounted = false }
  }, [businessId, endpoint, classification, level, page, query, filterParent, reload])

  const needsParents = type === 'subgroups' || type === 'categories'
  useEffect(() => {
    if (!needsParents) return
    let active = true
    api<{ categories: MasterItem[] }>(`/businesses/${businessId}/products/options?includeInactiveCategories=true`)
      .then(data => { if (active) setParentOptions(data.categories) })
      .catch(() => { if (active) setParentOptions([]) })
    return () => { active = false }
  }, [businessId, needsParents, reload])

  useEffect(() => { if (formOpen) firstInput.current?.focus() }, [formOpen, editingId])

  function clearForm() {
    setFormOpen(false)
    setError('')
    setEditingId(null)
    setName('')
    setCode('')
    setParentId(0)
    setActive(true)
  }

  function changeType(next: MasterType) {
    if (next === type) return
    setType(next)
    setFilterParent(0)
    setPage(1)
    setQuery('')
    setSearch('')
    setError('')
    clearForm()
  }

  function edit(item: MasterItem) {
    setFormOpen(true)
    setEditingId(item.id)
    setName(item.name)
    setCode(item.code ?? '')
    setParentId(item.parentId ?? 0)
    setActive(item.isActive)
    setError('')
  }

  async function save(event: FormEvent) {
    event.preventDefault()
    if (!canOperate || saving || !formOpen) return

    setSaving(true)
    setError('')
    const basePath = `/businesses/${businessId}/${endpoint}`
    if ((type === 'subgroups' || type === 'categories') && !parentId) {
      setError(`Choose a ${type === 'subgroups' ? 'group' : 'subgroup'} first.`)
      setSaving(false)
      return
    }
    const body = { name: name.trim(), ...(type === 'units' ? { code: code.trim().toUpperCase() } : {}),
      ...(classification ? { parentId: type === 'groups' ? null : parentId } : {}),
      ...(editingId !== null ? { isActive: active } : {}) }

    try {
      await run(`Saving ${label.toLowerCase()}…`,
        `${label} ${editingId === null ? 'created' : 'updated'}.`, async () => {
          if (editingId === null)
            await post(basePath, body)
          else
            await api(`${basePath}/${editingId}`, { method: 'PUT', body: JSON.stringify(body) })
        })
      clearForm()
      setReload(value => value + 1)
    } catch {
      // Shared popup handles API errors; keep local input and field validation.
    } finally {
      setSaving(false)
    }
  }

  const isUnit = type === 'units'
  const label = isUnit ? 'Unit' : type === 'brands' ? 'Brand' : type === 'groups' ? 'Group' : type === 'subgroups' ? 'Subgroup' : 'Category'
  const parentLabel = type === 'subgroups' ? 'Group' : 'Subgroup'
  const candidateParents = parentOptions.filter(item => item.isActive &&
    classificationTrail(parentOptions, item.id).length === (type === 'subgroups' ? 1 : 2))
  const filterParents = parentOptions.filter(item =>
    classificationTrail(parentOptions, item.id).length === (type === 'subgroups' ? 1 : 2))

  return (
    <section className="content-section retail-masters business-module-card" aria-label="Retail master data">
      <div className="masters-heading business-module-heading">
        <h2>Product Masters</h2>
        <div className="btn-group business-master-tabs" role="group" aria-label="Product master type">
          {(['groups', 'subgroups', 'categories', 'brands', 'units'] as MasterType[]).map(tab =>
            <button key={tab} type="button" className={`btn btn-sm ${type === tab ? 'btn-primary' : 'btn-outline-secondary'}`}
              aria-pressed={type === tab} disabled={saving || formOpen} onClick={() => changeType(tab)}>
              {tab === 'groups' ? 'Groups' : tab === 'subgroups' ? 'Subgroups' :
                tab === 'categories' ? 'Categories' : tab === 'brands' ? 'Brands' : 'Units'}
            </button>)}
        </div>
      </div>

      {!canOperate && <div className="alert alert-warning">Subscription is inactive. You can view saved records but cannot edit them.</div>}
      {error && <div className="alert alert-danger" role="alert">{error}</div>}
      {loadError && <div className="alert alert-danger" role="alert">
        {loadError} <button type="button" className="btn btn-sm btn-outline-secondary ms-2"
          onClick={() => setReload(value => value + 1)}>Retry</button>
      </div>}

      {canOperate && !formOpen && <div className="masters-search">
        <button type="button" className="btn btn-primary" onClick={() => { clearForm(); setFormOpen(true) }}>Add {label.toLowerCase()}</button>
      </div>}

      {canOperate && formOpen && (
        <form className="product-form" aria-busy={saving} onSubmit={event => void save(event)}>
          <h3>{editingId === null ? 'New ' : 'Edit '}{label.toLowerCase()}</h3>
          <div className="form-grid">
          <div>
            <label className="form-label" htmlFor="master-name">{label} name *</label>
            <input ref={firstInput} id="master-name" className="form-control" autoComplete="off" disabled={saving}
              value={name} maxLength={isUnit ? 60 : 80} minLength={2} required
              onChange={event => setName(event.target.value)}
              placeholder={isUnit ? 'e.g. Pieces' : `Enter ${label.toLowerCase()} name`} />
          </div>
          {isUnit && <div>
            <label className="form-label" htmlFor="master-code">Unit code *</label>
            <input id="master-code" className="form-control" autoComplete="off" disabled={saving}
              value={code} maxLength={12} pattern="[a-zA-Z0-9-]{1,12}" required
              onChange={event => setCode(event.target.value.toUpperCase())}
              placeholder="e.g. PCS" />
          </div>}
          {(type === 'subgroups' || type === 'categories') && <div>
            <label className="form-label" htmlFor="master-parent">{parentLabel} *</label>
            <MasterLookup id="master-parent" title={'Select ' + parentLabel.toLowerCase()} value={parentId}
              emptyLabel={'Select ' + parentLabel.toLowerCase()} disabled={saving}
              options={[...candidateParents.filter(item => item.id !== editingId)
                .map(item => ({ id: item.id, name: classificationPath(parentOptions, item.id) })),
                ...(parentId > 0 && !candidateParents.some(item => item.id === parentId)
                  ? [{ id: parentId, name: 'Previous parent (inactive or moved)' }] : [])]}
              onChange={setParentId} />
          </div>}
          {editingId !== null && <div className="form-check form-wide">
            <input id="master-status" type="checkbox" className="form-check-input" checked={active} disabled={saving}
              onChange={event => setActive(event.target.checked)} />
            <label className="form-check-label" htmlFor="master-status">Active</label>
          </div>}
          </div>
          <div className="form-footer">
            <button className="btn btn-outline-secondary" type="button" disabled={saving} onClick={clearForm}>Cancel</button>
            <ActionButton busy={saving}>Save</ActionButton>
          </div>
        </form>
      )}

      {!formOpen && <>
      <form className="masters-search" onSubmit={event => {
        event.preventDefault()
        setPage(1)
        setQuery(search.trim())
      }}>
        <label className="visually-hidden" htmlFor="master-search">Search {type}</label>
        <input id="master-search" className="form-control" type="search"
          placeholder={`Search ${type}`} value={search}
          onChange={event => setSearch(event.target.value)} />
        {(type === 'subgroups' || type === 'categories') && <div>
          <label className="visually-hidden" htmlFor="master-filter-parent">Filter by {parentLabel.toLowerCase()}</label>
          <MasterLookup id="master-filter-parent" title={'Filter by ' + parentLabel.toLowerCase()} value={filterParent}
            emptyLabel={'All ' + type} options={[{ id: 0, name: 'All ' + type },
              ...filterParents.map(item => ({ id: item.id, name: classificationPath(parentOptions, item.id) + (item.isActive ? '' : ' (inactive)') }))]}
            onChange={id => { setPage(1); setFilterParent(id) }} />
        </div>}
        <button className="btn btn-outline-secondary" type="submit">Search</button>
      </form>

      {loading ? <p className="text-secondary" role="status">Loading {type}…</p>
        : loadError ? null : items.length === 0 ? <p className="masters-empty">No {type} found. {canOperate && !query ? `Add your first ${label.toLowerCase()}.` : ''}</p>
          : <div className="table-responsive">
            <table className="table table-sm align-middle business-record-table">
              <thead><tr><th scope="col">Name</th>{isUnit && <th scope="col">Code</th>}
                {(type === 'subgroups' || type === 'categories') && <th scope="col">{parentLabel}</th>}
                <th scope="col">Status</th>{canOperate && <th scope="col" className="text-end">Action</th>}</tr></thead>
              <tbody>{items.map(item => <tr key={item.id}>
                <td>{item.name}</td>{isUnit && <td>{item.code}</td>}
                {(type === 'subgroups' || type === 'categories') && <td>{item.parentId
                  ? classificationPath(parentOptions, item.parentId) || `#${item.parentId}` : '—'}</td>}
                <td>{item.isActive ? 'Active' : 'Inactive'}</td>
                {canOperate && <td className="text-end"><button type="button"
                  className="btn btn-outline-secondary btn-sm" disabled={saving} onClick={() => edit(item)}>Edit</button></td>}
              </tr>)}</tbody>
            </table>
          </div>}
      {!loading && !loadError && (page > 1 || total > 20) && <div className="masters-pages">
        <span className="small text-secondary">Page {page} · {total} {type}</span>
        <div className="btn-group">
          <button className="btn btn-sm btn-outline-secondary" disabled={page === 1}
            onClick={() => setPage(value => value - 1)}>Previous</button>
          <button className="btn btn-sm btn-outline-secondary" disabled={page * 20 >= total}
            onClick={() => setPage(value => value + 1)}>Next</button>
        </div>
      </div>}
      </>}
    </section>
  )
}
