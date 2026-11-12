import { useEffect, useRef, useState, type FormEvent } from 'react'
import { api, post, SessionExpiredError } from './api'

interface Category {
  id: number
  name: string
}

export default function RetailCategories({ businessId, roleCode, canOperate, onBack }: {
  businessId: number
  roleCode: 'Owner' | 'Manager' | 'Cashier'
  canOperate: boolean
  onBack: () => void
}) {
  const [categories, setCategories] = useState<Category[]>([])
  const [search, setSearch] = useState('')
  const [name, setName] = useState('')
  const [editingId, setEditingId] = useState<number | null>(null)
  const [loading, setLoading] = useState(true)
  const [loadFailed, setLoadFailed] = useState(false)
  const [reload, setReload] = useState(0)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  const [notice, setNotice] = useState('')
  const input = useRef<HTMLInputElement>(null)
  const path = `/businesses/${businessId}/categories`
  const canEdit = roleCode !== 'Cashier' && canOperate

  useEffect(() => {
    let active = true
    setLoading(true)
    setLoadFailed(false)
    setError('')
    setCategories([])
    api<Category[]>(path)
      .then(items => { if (active) setCategories(items) })
      .catch(cause => {
        if (!active || cause instanceof SessionExpiredError) return
        setLoadFailed(true)
        setError(cause instanceof Error ? cause.message : 'Unable to load categories.')
      })
      .finally(() => { if (active) setLoading(false) })
    return () => { active = false }
  }, [path, reload])

  function cancelEdit() {
    setEditingId(null)
    setName('')
    input.current?.focus()
  }

  function edit(category: Category) {
    setEditingId(category.id)
    setName(category.name)
    setError('')
    setNotice('')
    input.current?.focus()
    input.current?.select()
  }

  async function save(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (busy || !canEdit) return
    setBusy(true)
    setError('')
    setNotice('')
    try {
      const item = editingId === null
        ? await post<Category>(path, { name: name.trim() })
        : await api<Category>(`${path}/${editingId}`, {
            method: 'PUT', body: JSON.stringify({ name: name.trim() })
          })
      setCategories(current => [...current.filter(value => value.id !== item.id), item]
        .sort((a, b) => a.name.localeCompare(b.name)))
      setNotice(editingId === null ? 'Category added.' : 'Category updated.')
      cancelEdit()
    } catch (cause) {
      if (!(cause instanceof SessionExpiredError))
        setError(cause instanceof Error ? cause.message : 'Unable to save category.')
    } finally {
      setBusy(false)
    }
  }

  const filtered = categories.filter(item =>
    item.name.toLowerCase().includes(search.trim().toLowerCase()))

  return (
    <>
      <div className="page-header">
        <div>
          <button className="back-link" type="button" onClick={onBack}>Back to dashboard</button>
          <h1>Categories</h1>
          <p>Organize products for this retail business. No sample data is added.</p>
        </div>
      </div>
      {error && <div className="alert alert-danger" role="alert">{error}</div>}
      {notice && <div className="alert alert-success" role="status">{notice}</div>}
      {roleCode !== 'Cashier' && !canOperate &&
        <div className="alert alert-warning" role="status">An active trial or subscription is required to change categories.</div>}
      {roleCode !== 'Cashier' &&
        <section className="content-section mb-3">
          <h2>{editingId === null ? 'New Category' : 'Edit Category'}</h2>
          <form className="d-flex flex-wrap gap-2 align-items-end" onSubmit={event => void save(event)}>
            <div className="flex-grow-1">
              <label htmlFor="category-name" className="form-label">Category name *</label>
              <input ref={input} id="category-name" type="text" className="form-control"
                autoFocus maxLength={100} minLength={2} required value={name}
                onChange={event => setName(event.target.value)}
                disabled={!canEdit || busy || loading || loadFailed} />
            </div>
            <button type="submit" className="btn btn-primary" disabled={!canEdit || busy || loading || loadFailed}>
              {busy ? 'Saving…' : editingId === null ? 'Add Category' : 'Save Changes'}
            </button>
            {editingId !== null &&
              <button type="button" className="btn btn-outline-secondary" onClick={cancelEdit}
                disabled={busy}>Cancel</button>}
          </form>
        </section>}
      <section className="content-section">
        <div className="d-flex justify-content-between align-items-center gap-3 flex-wrap mb-3">
          <h2 className="mb-0">Category List</h2>
          <div>
            <label htmlFor="category-search" className="form-label">Search categories</label>
            <input id="category-search" type="search" className="form-control form-control-sm"
              value={search} onChange={event => setSearch(event.target.value)}
              disabled={loading || loadFailed} />
          </div>
        </div>
        {loading ? <p className="text-secondary" role="status">Loading categories…</p>
          : loadFailed ? <button className="btn btn-outline-secondary" type="button"
              onClick={() => setReload(value => value + 1)}>Retry loading</button>
            : categories.length === 0 ? <p className="text-secondary">No categories yet. Add the first category to begin.</p>
              : filtered.length === 0 ? <p className="text-secondary">No categories match your search.</p>
                : <div className="table-responsive">
                  <table className="table table-sm align-middle mb-0">
                    <thead><tr><th scope="col">Category</th>{roleCode !== 'Cashier' && <th scope="col" className="text-end">Action</th>}</tr></thead>
                    <tbody>{filtered.map(category => (
                      <tr key={category.id}>
                        <td>{category.name}</td>
                        {roleCode !== 'Cashier' && <td className="text-end">
                          <button type="button" className="btn btn-outline-secondary btn-sm"
                            disabled={!canEdit || busy} onClick={() => edit(category)}
                            aria-label={`Edit ${category.name}`}>Edit</button>
                        </td>}
                      </tr>
                    ))}</tbody>
                  </table>
                </div>}
      </section>
    </>
  )
}
