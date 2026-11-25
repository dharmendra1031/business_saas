import { useEffect, useRef, useState, type FormEvent } from 'react'
import { api, post, SessionExpiredError } from './api'
import { ActionButton, useActionFeedback } from './ActionFeedback'
import { moneyCents } from './posMoney'
import CreditPayment, { readCollection, type CreditInvoice, type PendingCollection } from './CreditPayment'

type ContactType = 'suppliers' | 'customers'

interface Contact {
  id: number
  name: string
  phone: string
  email: string
  address?: string | null
  creditLimit?: number | null
  isActive: boolean
}

interface ContactPage {
  items: Contact[]
  total: number
  page: number
  pageSize: number
}

const emptyForm = { name: '', phone: '', email: '', address: '', isActive: true }

interface CreditLedger {
  customerId: number
  name: string
  creditLimit: number
  totals: { currencyCode: string; debit: number; paid: number; outstanding: number; invoiceCount: number }[]
  canCollect?: boolean
  items: CreditInvoice[]
  hasMore: boolean
}

function CustomerCreditLedger({ path, userId, onClose }: { path: string; userId: number; onClose: () => void }) {
  const storageKey = 'credit-collection:' + userId + ':' + path
  const [recovery] = useState(() => {
    try { return { pending: readCollection(storageKey), error: '' } }
    catch { return { pending: null, error: 'Saved payment could not be read. Reconcile it before recording more cash.' } }
  })
  const [pending, setPending] = useState<PendingCollection | null>(recovery.pending)
  const [selected, setSelected] = useState<CreditInvoice | null>(recovery.pending?.invoice ?? null)
  const [notice, setNotice] = useState('')
  const [page, setPage] = useState(1)
  const [reload, setReload] = useState(0)
  const [ledger, setLedger] = useState<CreditLedger | null>(null)
  const [error, setError] = useState('')
  useEffect(() => {
    let active = true
    setLedger(null); setError('')
    api<CreditLedger>(path + '?page=' + page).then(value => {
      if (active) setLedger(value)
    }).catch(cause => {
      if (active && !(cause instanceof SessionExpiredError))
        setError(cause instanceof Error ? cause.message : 'Unable to load credit ledger.')
    })
    return () => { active = false }
  }, [path, page, reload])
  return <section className="border rounded p-2 mb-3" aria-label="Customer credit ledger">
    <div className="d-flex flex-wrap justify-content-between align-items-center gap-2">
      <h3 className="h6 mb-0">Credit ledger{ledger ? ' — ' + ledger.name : ''}</h3>
      <button type="button" className="btn btn-sm btn-outline-secondary" onClick={onClose}>Close ledger</button>
    </div>
    {notice && <p className="alert alert-success py-2 mt-2" role="status">{notice}</p>}
    {recovery.error && <p className="alert alert-danger py-2 mt-2" role="alert">{recovery.error}</p>}
    {pending && !selected && <button type="button" className="btn btn-sm btn-outline-secondary mt-2"
      onClick={() => setSelected(pending.invoice)}>Recover saved collection</button>}
    {selected && <CreditPayment key={selected.id} customerPath={path.replace(/\/credit-ledger$/, '')}
      invoice={selected} storageKey={storageKey} pending={pending} canCollect={ledger?.canCollect === true && !recovery.error}
      onPending={setPending} onRecorded={payment => {
        setNotice('Payment #' + payment.id + ' recorded. Reloading ledger from saved transactions.')
        setSelected(null); setReload(x => x + 1)
      }} onClose={() => setSelected(null)} />}
    {error ? <p role="alert" className="mt-2">{error} <button type="button" className="btn btn-sm btn-outline-secondary"
      onClick={() => setReload(x => x + 1)}>Retry</button></p> : !ledger ? <p role="status" className="mt-2">Loading credit ledger…</p> :
      <>
        {ledger.totals.length === 0 ? <p className="mt-2 mb-1">No posted credit invoices.</p> :
          <>{ledger.totals.map(total => <p key={total.currencyCode} className="mt-2 mb-1">
            Credit sales: {total.currencyCode} {total.debit.toFixed(2)} · Received: {total.currencyCode} {total.paid.toFixed(2)} ·
            Outstanding: <strong>{total.currencyCode} {total.outstanding.toFixed(2)}</strong> · {total.invoiceCount} invoices
          </p>)}
          <div className="table-responsive"><table className="table table-sm align-middle">
            <thead><tr><th>Invoice</th><th>Location</th><th>Due date</th><th className="text-end">Invoice amount</th><th className="text-end">Received</th><th className="text-end">Outstanding</th><th>Action</th></tr></thead>
            <tbody>{ledger.items.map(invoice => <tr key={invoice.id}>
              <td>#{invoice.id}</td><td>{invoice.locationName ?? '—'}</td><td>{invoice.creditDueDate}</td>
              <td className="text-end">{invoice.currencyCode} {invoice.debit.toFixed(2)}</td>
              <td className="text-end">{invoice.currencyCode} {invoice.paid.toFixed(2)}</td>
              <td className="text-end">{invoice.currencyCode} {invoice.outstanding.toFixed(2)}</td>
              <td><button type="button" className="btn btn-sm btn-outline-secondary"
                disabled={!!pending || selected !== null}
                onClick={() => { setNotice(''); setSelected(invoice) }}>
                {ledger.canCollect && !recovery.error && invoice.outstanding > 0 ? 'Receive payment' : 'Payments'} #{invoice.id}
              </button></td>
            </tr>)}</tbody>
          </table></div>
          {(page > 1 || ledger.hasMore) && <div className="btn-group">
            <button type="button" className="btn btn-sm btn-outline-secondary" disabled={page === 1}
              onClick={() => setPage(x => x - 1)}>Previous</button>
            <button type="button" className="btn btn-sm btn-outline-secondary" disabled={!ledger.hasMore}
              onClick={() => setPage(x => x + 1)}>Next</button>
          </div>}</>}
        <p className="small mb-0 mt-2">Payments reduce the original invoice balance. Cash drawer updates and payment receipt printing are not available yet.</p>
      </>}
  </section>
}

export default function RetailContacts({ businessId, canOperate, type, isOwner, currencyCode, userId }: {
  userId: number
  businessId: number
  canOperate: boolean
  type: ContactType
  isOwner: boolean
  currencyCode: string
}) {
  const { run } = useActionFeedback()
  const [items, setItems] = useState<Contact[]>([])
  const [total, setTotal] = useState(0)
  const [page, setPage] = useState(1)
  const [search, setSearch] = useState('')
  const [query, setQuery] = useState('')
  const [reload, setReload] = useState(0)
  const [form, setForm] = useState(emptyForm)
  const [editingId, setEditingId] = useState<number | null>(null)
  const [ledgerCustomer, setLedgerCustomer] = useState<number | null>(null)
  const [originalLimit, setOriginalLimit] = useState<number | null>(null)
  const [creditLimit, setCreditLimit] = useState('')
  const [creditReason, setCreditReason] = useState('')
  const [loading, setLoading] = useState(true)
  const [saving, setSaving] = useState(false)
  const [loadError, setLoadError] = useState('')
  const nameInput = useRef<HTMLInputElement>(null)
  const basePath = `/businesses/${businessId}/${type}`
  const supplier = type === 'suppliers'
  const label = supplier ? 'Supplier' : 'Customer'

  useEffect(() => {
    let active = true
    setLoading(true)
    setLoadError('')
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
        setLoadError(cause instanceof Error ? cause.message : 'Unable to load records.')
      })
      .finally(() => { if (active) setLoading(false) })
    return () => { active = false }
  }, [basePath, page, query, reload])

  function clearForm() {
    setEditingId(null)
    setForm(emptyForm)
    setOriginalLimit(null)
    setCreditLimit('')
    setCreditReason('')
  }

  function edit(item: Contact) {
    setEditingId(item.id)
    setOriginalLimit(item.creditLimit ?? null)
    setCreditLimit(item.creditLimit == null ? '' : String(item.creditLimit))
    setCreditReason('')
    setForm({ name: item.name, phone: item.phone, email: item.email, address: item.address ?? '', isActive: item.isActive })
    nameInput.current?.focus()
  }

  const limitCents = moneyCents(creditLimit)
  const validCredit = originalLimit !== null && Number.isFinite(limitCents) &&
    limitCents <= 999999999999 && creditReason.trim().length >= 10 && limitCents / 100 !== originalLimit

  async function saveCredit(event: FormEvent) {
    event.preventDefault()
    if (!isOwner || supplier || !canOperate || saving || editingId === null || !validCredit) return
    setSaving(true)
    try {
      await run('Saving credit limit…', 'Credit limit updated.', async () => {
        const result = await api<{ id: number; creditLimit: number }>(basePath + '/' + editingId + '/credit-limit', {
          method: 'PUT', body: JSON.stringify({ limit: limitCents / 100, expectedLimit: originalLimit, reason: creditReason.trim() })
        })
        setOriginalLimit(result.creditLimit)
        setCreditLimit(String(result.creditLimit))
        setCreditReason('')
      })
      setReload(value => value + 1)
    } catch { /* Shared write feedback preserves the input. */ }
    finally { setSaving(false) }
  }

  async function save(event: FormEvent) {
    event.preventDefault()
    if (!canOperate || saving) return

    setSaving(true)
    const body = {
      name: form.name.trim(),
      phone: form.phone.trim(),
      email: form.email.trim(),
      ...(!supplier ? { address: form.address.trim() } : {}),
      ...(editingId === null ? {} : { isActive: form.isActive })
    }

    try {
      await run(`Saving ${label.toLowerCase()}…`,
        `${label} ${editingId === null ? 'created' : 'updated'}.`, async () => {
          if (editingId === null)
            await post(basePath, body)
          else
            await api(`${basePath}/${editingId}`, { method: 'PUT', body: JSON.stringify(body) })
        })
      clearForm()
      setPage(1)
      setReload(value => value + 1)
      nameInput.current?.focus()
    } catch {
      // Shared failure/unknown popup owns write feedback; keep form values.
    } finally {
      setSaving(false)
    }
  }

  return (
    <section className="content-section retail-masters business-module-card" aria-label={type}>
      <div className="masters-heading business-module-heading">
        <h2>{label}s</h2>
      </div>

      {!canOperate && <div className="alert alert-warning" role="status">
        Your subscription is inactive. Records are read-only.
      </div>}
      {loadError && <div className="alert alert-danger" role="alert">
        {loadError} <button type="button" className="btn btn-sm btn-outline-secondary ms-2"
          onClick={() => setReload(value => value + 1)}>Retry</button>
      </div>}

      {canOperate && <form className="masters-form" aria-busy={saving} onSubmit={event => void save(event)}>
        <div>
          <label className="form-label" htmlFor="contact-name">{label} name *</label>
          <input ref={nameInput} id="contact-name" className="form-control" required disabled={saving}
            maxLength={120} minLength={2} value={form.name}
            onChange={event => setForm(current => ({ ...current, name: event.target.value }))} />
        </div>
        <div>
          <label className="form-label" htmlFor="contact-phone">Phone {supplier ? '(or email required)' : '(optional)'}</label>
          <input id="contact-phone" className="form-control" type="tel" maxLength={25} disabled={saving}
            pattern="[0-9+() -]*" value={form.phone}
            onChange={event => setForm(current => ({ ...current, phone: event.target.value }))} />
        </div>
        <div>
          <label className="form-label" htmlFor="contact-email">Email {supplier ? '(or phone required)' : '(optional)'}</label>
          <input id="contact-email" className="form-control" type="email" maxLength={160} disabled={saving}
            value={form.email} onChange={event =>
              setForm(current => ({ ...current, email: event.target.value }))} />
        </div>
        {!supplier && <div>
          <label className="form-label" htmlFor="contact-address">Address (optional)</label>
          <input id="contact-address" className="form-control" maxLength={300} disabled={saving}
            value={form.address} onChange={event => setForm(current => ({ ...current, address: event.target.value }))} />
        </div>}
        {editingId !== null && <div>
          <label className="form-label" htmlFor="contact-status">Status</label>
          <select id="contact-status" className="form-select" value={form.isActive ? 'active' : 'inactive'} disabled={saving}
            onChange={event => setForm(current => ({ ...current, isActive: event.target.value === 'active' }))}>
            <option value="active">Active</option>
            <option value="inactive">Inactive</option>
          </select>
        </div>}
        <ActionButton busy={saving} disabled={supplier && !form.phone.trim() && !form.email.trim()}>
          {editingId === null ? `Add ${label.toLowerCase()}` : 'Save changes'}
        </ActionButton>
        {editingId !== null && <button className="btn btn-outline-secondary" type="button"
          disabled={saving} onClick={() => { clearForm(); nameInput.current?.focus() }}>Cancel edit</button>}
      </form>}

      {!supplier && isOwner && canOperate && editingId !== null && originalLimit !== null &&
        <form className="border rounded p-2 mb-3" onSubmit={event => void saveCredit(event)}>
          <h3 className="h6">Credit limit — {items.find(x => x.id === editingId)?.name ?? 'Selected customer'}</h3>
          <p className="small mb-2">Maximum allowed customer credit. Set to zero to prevent new credit sales.</p>
          <div className="row g-2 align-items-end">
            <div className="col-12 col-md-4">
              <label className="form-label" htmlFor="customer-credit-limit">Credit limit ({currencyCode})</label>
              <input className="form-control" id="customer-credit-limit" type="number" min="0" max="9999999999.99"
                step="0.01" required disabled={saving} value={creditLimit} onChange={event => setCreditLimit(event.target.value)} />
            </div>
            <div className="col-12 col-md">
              <label className="form-label" htmlFor="customer-credit-reason">Change reason</label>
              <input className="form-control" id="customer-credit-reason" required minLength={10} maxLength={200}
                disabled={saving} value={creditReason} onChange={event => setCreditReason(event.target.value)} />
            </div>
            <div className="col-12 col-md-auto">
              <ActionButton busy={saving} disabled={!validCredit}>Save credit limit</ActionButton>
            </div>
          </div>
        </form>}

      {!supplier && isOwner && ledgerCustomer !== null &&
        <CustomerCreditLedger key={basePath + '/' + ledgerCustomer}
          path={basePath + '/' + ledgerCustomer + '/credit-ledger'} userId={userId} onClose={() => setLedgerCustomer(null)} />}

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
        : loadError ? null
          : items.length === 0 ? <p className="masters-empty">
            {query ? 'No matching records.' : `No ${type} yet.`}
          </p>
            : <div className="table-responsive">
              <table className="table table-sm align-middle business-record-table">
                <thead><tr>
                  <th scope="col">Name</th><th scope="col">Phone</th><th scope="col">Email</th>
                  {!supplier && isOwner && <th scope="col" className="text-end">Credit limit ({currencyCode})</th>}
                  <th scope="col">Status</th>
                  {(canOperate || (!supplier && isOwner)) && <th scope="col" className="text-end">Action</th>}
                </tr></thead>
                <tbody>{items.map(item => <tr key={item.id}>
                  <td><strong>{item.name}</strong></td>
                  <td>{item.phone || '—'}</td>
                  <td>{item.email || '—'}</td>
                  {!supplier && isOwner && <td className="text-end">{item.creditLimit == null ? '—' : item.creditLimit.toFixed(2)}</td>}
                  <td>{item.isActive ? 'Active' : 'Inactive'}</td>
                  {(canOperate || (!supplier && isOwner)) && <td className="text-end">
                    <div className="d-flex flex-wrap justify-content-end gap-1">
                      {canOperate && <button type="button" className="btn btn-sm btn-outline-secondary"
                        disabled={saving} onClick={() => edit(item)} aria-label={`Edit ${item.name}`}>Edit</button>}
                      {!supplier && isOwner && <button type="button" className="btn btn-sm btn-outline-secondary"
                        onClick={() => setLedgerCustomer(item.id)} aria-label={`Credit ledger for ${item.name}`}>Ledger</button>}
                    </div>
                  </td>}
                </tr>)}</tbody>
              </table>
            </div>}

      {!loading && !loadError && (page > 1 || total > 20) && <div className="masters-pages">
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
