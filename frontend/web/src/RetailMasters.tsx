import { useEffect, useRef, useState, type FormEvent } from 'react'
import { api, post, SessionExpiredError } from './api'

type MasterType = 'categories' | 'units'

interface MasterItem {
  id: number
  name: string
  code?: string
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
  const [type, setType] = useState<MasterType>('categories')
  const [items, setItems] = useState<MasterItem[]>([])
  const [total, setTotal] = useState(0)
  const [page, setPage] = useState(1)
  const [search, setSearch] = useState('')
  const [query, setQuery] = useState('')
  const [reload, setReload] = useState(0)
  const [name, setName] = useState('')
  const [code, setCode] = useState('')
  const [active, setActive] = useState(true)
  const [editingId, setEditingId] = useState<number | null>(null)
  const [loading, setLoading] = useState(true)
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState('')
  const [loadError, setLoadError] = useState('')
  const [notice, setNotice] = useState('')
  const firstInput = useRef<HTMLInputElement>(null)

  useEffect(() => {
    let mounted = true
    setLoading(true)
    setItems([])
    setTotal(0)
    setLoadError('')

    api<MasterPage>(`/businesses/${businessId}/${type}?page=${page}&search=${encodeURIComponent(query)}`)
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
  }, [businessId, type, page, query, reload])

  function clearForm() {
    setEditingId(null)
    setName('')
    setCode('')
    setActive(true)
  }

  function changeType(next: MasterType) {
    if (next === type) return
    setType(next)
    setPage(1)
    setQuery('')
    setSearch('')
    setError('')
    setNotice('')
    clearForm()
  }

  function edit(item: MasterItem) {
    setEditingId(item.id)
    setName(item.name)
    setCode(item.code ?? '')
    setActive(item.isActive)
    setError('')
    setNotice('')
    firstInput.current?.focus()
  }

  async function save(event: FormEvent) {
    event.preventDefault()
    if (!canOperate || saving) return

    setSaving(true)
    setError('')
    setNotice('')
    const basePath = `/businesses/${businessId}/${type}`
    const body = { name: name.trim(), ...(type === 'units' ? { code: code.trim().toUpperCase() } : {}),
      ...(editingId !== null ? { isActive: active } : {}) }

    try {
      if (editingId === null)
        await post(basePath, body)
      else
        await api(`${basePath}/${editingId}`, { method: 'PUT', body: JSON.stringify(body) })

      setNotice(`${type === 'units' ? 'Unit' : 'Category'} ${editingId === null ? 'created' : 'updated'}.`)
      clearForm()
      setReload(value => value + 1)
      firstInput.current?.focus()
    } catch (cause) {
      if (!(cause instanceof SessionExpiredError))
        setError(cause instanceof Error ? cause.message : 'Unable to save changes.')
    } finally {
      setSaving(false)
    }
  }

  const isUnit = type === 'units'
  const label = isUnit ? 'Unit' : 'Category'

  return (
    <section className="content-section retail-masters" aria-label="Retail master data">
      <div className="masters-heading">
        <div><h2>Catalog setup</h2><p>Manage categories and stock units for this business.</p></div>
        <div className="btn-group" role="group" aria-label="Catalog master type">
          <button type="button" className={`btn btn-sm ${isUnit ? 'btn-outline-secondary' : 'btn-primary'}`}
            aria-pressed={!isUnit} onClick={() => changeType('categories')}>Categories</button>
          <button type="button" className={`btn btn-sm ${isUnit ? 'btn-primary' : 'btn-outline-secondary'}`}
            aria-pressed={isUnit} onClick={() => changeType('units')}>Units</button>
        </div>
      </div>

      {!canOperate && <div className="alert alert-warning">Subscription is inactive. You can view saved records but cannot edit them.</div>}
      {error && <div className="alert alert-danger" role="alert">{error}</div>}
      {loadError && <div className="alert alert-danger" role="alert">
        {loadError} <button type="button" className="btn btn-sm btn-outline-secondary ms-2"
          onClick={() => setReload(value => value + 1)}>Retry</button>
      </div>}
      {notice && <div className="alert alert-success" role="status">{notice}</div>}

      {canOperate && (
        <form className="masters-form" onSubmit={event => void save(event)}>
          <div>
            <label className="form-label" htmlFor="master-name">{label} name *</label>
            <input ref={firstInput} id="master-name" className="form-control" autoComplete="off"
              value={name} maxLength={isUnit ? 60 : 80} minLength={2} required
              onChange={event => setName(event.target.value)}
              placeholder={isUnit ? 'e.g. Pieces' : 'Category name'} />
          </div>
          {isUnit && <div>
            <label className="form-label" htmlFor="master-code">Unit code *</label>
            <input id="master-code" className="form-control" autoComplete="off"
              value={code} maxLength={12} pattern="[a-zA-Z0-9-]{1,12}" required
              onChange={event => setCode(event.target.value.toUpperCase())}
              placeholder="e.g. PCS" />
          </div>}
          {editingId !== null && <div>
            <label className="form-label" htmlFor="master-status">Status</label>
            <select id="master-status" className="form-select" value={active ? 'active' : 'inactive'}
              onChange={event => setActive(event.target.value === 'active')}>
              <option value="active">Active</option>
              <option value="inactive">Inactive</option>
            </select>
          </div>}
          <button className="btn btn-primary" disabled={saving} type="submit">
            {saving ? 'Saving…' : editingId === null ? `Add ${label.toLowerCase()}` : 'Save changes'}
          </button>
          {editingId !== null && <button className="btn btn-outline-secondary" type="button"
            onClick={() => { clearForm(); firstInput.current?.focus() }}>Cancel edit</button>}
        </form>
      )}

      <form className="masters-search" onSubmit={event => {
        event.preventDefault()
        setPage(1)
        setQuery(search.trim())
      }}>
        <label className="visually-hidden" htmlFor="master-search">Search {type}</label>
        <input id="master-search" className="form-control" type="search"
          placeholder={`Search ${type}`} value={search}
          onChange={event => setSearch(event.target.value)} />
        <button className="btn btn-outline-secondary" type="submit">Search</button>
      </form>

      {loading ? <p className="text-secondary" role="status">Loading {type}…</p>
        : loadError ? null : items.length === 0 ? <p className="masters-empty">No {type} found. {canOperate && !query ? `Add your first ${label.toLowerCase()} above.` : ''}</p>
          : <div className="table-responsive">
            <table className="table table-sm align-middle">
              <thead><tr><th scope="col">Name</th>{isUnit && <th scope="col">Code</th>}
                <th scope="col">Status</th>{canOperate && <th scope="col" className="text-end">Action</th>}</tr></thead>
              <tbody>{items.map(item => <tr key={item.id}>
                <td>{item.name}</td>{isUnit && <td>{item.code}</td>}
                <td>{item.isActive ? 'Active' : 'Inactive'}</td>
                {canOperate && <td className="text-end"><button type="button"
                  className="btn btn-outline-secondary btn-sm" onClick={() => edit(item)}>Edit</button></td>}
              </tr>)}</tbody>
            </table>
          </div>}
      {!loading && !loadError && total > 0 && <div className="masters-pages">
        <span className="small text-secondary">Page {page} · {total} records</span>
        <div className="btn-group">
          <button className="btn btn-sm btn-outline-secondary" disabled={page === 1}
            onClick={() => setPage(value => value - 1)}>Previous</button>
          <button className="btn btn-sm btn-outline-secondary" disabled={page * 20 >= total}
            onClick={() => setPage(value => value + 1)}>Next</button>
        </div>
      </div>}
    </section>
  )
}
