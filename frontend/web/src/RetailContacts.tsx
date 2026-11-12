import { useEffect, useRef, useState, type FormEvent } from 'react'
import { api, post, SessionExpiredError } from './api'

type ContactType = 'suppliers' | 'customers'

interface Contact {
  id: number
  name: string
  phone: string
  email: string
  isActive: boolean
}

interface ContactPage {
  items: Contact[]
  total: number
  page: number
  pageSize: number
}

const emptyForm = { name: '', phone: '', email: '', isActive: true }

export default function RetailContacts({ businessId, canOperate, type }: {
  businessId: number
  canOperate: boolean
  type: ContactType
}) {
  const [items, setItems] = useState<Contact[]>([])
  const [total, setTotal] = useState(0)
  const [page, setPage] = useState(1)
  const [search, setSearch] = useState('')
  const [query, setQuery] = useState('')
  const [reload, setReload] = useState(0)
  const [form, setForm] = useState(emptyForm)
  const [editingId, setEditingId] = useState<number | null>(null)
  const [loading, setLoading] = useState(true)
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState('')
  const [notice, setNotice] = useState('')
  const nameInput = useRef<HTMLInputElement>(null)
  const basePath = `/businesses/${businessId}/${type}`
  const supplier = type === 'suppliers'
  const label = supplier ? 'Supplier' : 'Customer'

  useEffect(() => {
    let active = true
    setLoading(true)
    setError('')
    const params = new URLSearchParams({ search: query, page: String(page) })
    api<ContactPage>(basePath + '?' + params)
      .then(value => {
        if (!active) return
        setItems(value.items)
        setTotal(value.total)
      })
      .catch(cause => {
        if (!active || cause instanceof SessionExpiredError) return
        setItems([])
        setTotal(0)
        setError(cause instanceof Error ? cause.message : 'Unable to load records.')
      })
      .finally(() => { if (active) setLoading(false) })
    return () => { active = false }
  }, [basePath, page, query, reload])

  function clearForm() {
    setEditingId(null)
    setForm(emptyForm)
  }

  function edit(item: Contact) {
    setEditingId(item.id)
    setForm({ name: item.name, phone: item.phone, email: item.email, isActive: item.isActive })
    setError('')
    setNotice('')
    nameInput.current?.focus()
  }

  async function save(event: FormEvent) {
    event.preventDefault()
    if (!canOperate || saving) return

    setSaving(true)
    setError('')
    setNotice('')
    const body = {
      name: form.name.trim(),
      phone: form.phone.trim(),
      email: form.email.trim(),
      ...(editingId === null ? {} : { isActive: form.isActive })
    }

    try {
      if (editingId === null)
        await post(basePath, body)
      else
        await api(`${basePath}/${editingId}`, { method: 'PUT', body: JSON.stringify(body) })

      setNotice(`${label} ${editingId === null ? 'created' : 'updated'}.`)
      clearForm()
      setPage(1)
      setReload(value => value + 1)
      nameInput.current?.focus()
    } catch (cause) {
      if (!(cause instanceof SessionExpiredError))
        setError(cause instanceof Error ? cause.message : 'Unable to save changes.')
    } finally {
      setSaving(false)
    }
  }

  return (
    <section className="content-section retail-masters" aria-label={type}>
      <div className="masters-heading">
        <div>
          <h2>{label}s</h2>
          <p>{supplier
            ? 'Add suppliers before purchase receiving. A phone number or email is required.'
            : 'Customers are optional. Walk-in sales will not require a customer.'}</p>
        </div>
      </div>

      {!canOperate && <div className="alert alert-warning" role="status">
        Your subscription is inactive. Records are read-only.
      </div>}
      {error && <div className="alert alert-danger" role="alert">
        {error} {loading ? null : <button type="button" className="btn btn-sm btn-outline-secondary ms-2"
          onClick={() => setReload(value => value + 1)}>Retry</button>}
      </div>}
      {notice && <div className="alert alert-success" role="status">{notice}</div>}

      {canOperate && <form className="masters-form" onSubmit={event => void save(event)}>
        <div>
          <label className="form-label" htmlFor="contact-name">{label} name *</label>
          <input ref={nameInput} id="contact-name" className="form-control" required
            maxLength={120} minLength={2} value={form.name}
            onChange={event => setForm(current => ({ ...current, name: event.target.value }))} />
        </div>
        <div>
          <label className="form-label" htmlFor="contact-phone">Phone {supplier ? '(or email) *' : '(optional)'}</label>
          <input id="contact-phone" className="form-control" type="tel" maxLength={25}
            pattern="[0-9+() -]*" value={form.phone}
            onChange={event => setForm(current => ({ ...current, phone: event.target.value }))} />
        </div>
        <div>
          <label className="form-label" htmlFor="contact-email">Email {supplier ? '(or phone) *' : '(optional)'}</label>
          <input id="contact-email" className="form-control" type="email" maxLength={160}
            value={form.email} onChange={event =>
              setForm(current => ({ ...current, email: event.target.value }))} />
        </div>
        {editingId !== null && <div>
          <label className="form-label" htmlFor="contact-status">Status</label>
          <select id="contact-status" className="form-select" value={form.isActive ? 'active' : 'inactive'}
            onChange={event => setForm(current => ({ ...current, isActive: event.target.value === 'active' }))}>
            <option value="active">Active</option>
            <option value="inactive">Inactive</option>
          </select>
        </div>}
        <button className="btn btn-primary" type="submit" disabled={saving ||
          (supplier && !form.phone.trim() && !form.email.trim())}>
          {saving ? 'Saving…' : editingId === null ? `Add ${label.toLowerCase()}` : 'Save changes'}
        </button>
        {editingId !== null && <button className="btn btn-outline-secondary" type="button"
          onClick={() => { clearForm(); nameInput.current?.focus() }}>Cancel edit</button>}
      </form>}

      <form className="masters-search" onSubmit={event => {
        event.preventDefault()
        setPage(1)
        setQuery(search.trim())
      }}>
        <label htmlFor="contact-search" className="visually-hidden">Search {type}</label>
        <input id="contact-search" className="form-control" type="search"
          placeholder={`Search ${type} by name or contact`}
          maxLength={100} value={search} onChange={event => setSearch(event.target.value)} />
        <button className="btn btn-outline-secondary" type="submit">Search</button>
      </form>

      {loading ? <p className="text-secondary" role="status">Loading {type}…</p>
        : error ? null
          : items.length === 0 ? <p className="masters-empty">
            {query ? 'No matching records.' : `No ${type} yet.`}
          </p>
            : <div className="table-responsive">
              <table className="table table-sm align-middle">
                <thead><tr>
                  <th scope="col">Name</th><th scope="col">Phone</th><th scope="col">Email</th>
                  <th scope="col">Status</th>
                  {canOperate && <th scope="col" className="text-end">Action</th>}
                </tr></thead>
                <tbody>{items.map(item => <tr key={item.id}>
                  <td><strong>{item.name}</strong></td>
                  <td>{item.phone || '—'}</td>
                  <td>{item.email || '—'}</td>
                  <td>{item.isActive ? 'Active' : 'Inactive'}</td>
                  {canOperate && <td className="text-end">
                    <button type="button" className="btn btn-sm btn-outline-secondary"
                      onClick={() => edit(item)} aria-label={`Edit ${item.name}`}>Edit</button>
                  </td>}
                </tr>)}</tbody>
              </table>
            </div>}

      {!loading && !error && total > 0 && <div className="masters-pages">
        <span className="small text-secondary">Page {page} · {total} records</span>
        <div className="btn-group">
          <button type="button" className="btn btn-sm btn-outline-secondary"
            disabled={page === 1} onClick={() => setPage(value => value - 1)}>Previous</button>
          <button type="button" className="btn btn-sm btn-outline-secondary"
            disabled={page * 20 >= total} onClick={() => setPage(value => value + 1)}>Next</button>
        </div>
      </div>}
    </section>
  )
}
