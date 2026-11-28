import { useEffect, useLayoutEffect, useRef, useState, type FormEvent } from 'react'
import { api, post, SessionExpiredError } from './api'
import { ActionButton, useActionFeedback } from './ActionFeedback'
import { moneyCents } from './posMoney'
import CreditPayment, { readCollection, type CreditInvoice, type PendingCollection } from './CreditPayment'
import SupplierAccount from './SupplierAccount'

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
  totals: { currencyCode: string; debit: number; paid: number; returned?: number; outstanding: number; invoiceCount: number }[]
  canCollect?: boolean
  registerRequired?: boolean
  items: CreditInvoice[]
  hasMore: boolean
}

function CustomerCreditLedger({ path, userId, businessId, onClose }: { path: string; userId: number; businessId: number; onClose: () => void }) {
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
    const controller = new AbortController()
    setLedger(null); setError('')
    api<CreditLedger>(path + '?page=' + page, { signal: controller.signal }).then(value => {
      if (!controller.signal.aborted) setLedger(value)
    }).catch(cause => {
      if (!controller.signal.aborted && !(cause instanceof SessionExpiredError))
        setError(cause instanceof Error ? cause.message : 'Unable to load credit ledger.')
    })
    return () => controller.abort()
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
      businessId={businessId} userId={userId} registerRequired={ledger?.registerRequired === true}
      onPending={setPending} onRecorded={payment => {
        setNotice('Payment #' + payment.id + ' recorded. Reloading ledger from saved transactions.')
        setSelected(null); setReload(x => x + 1)
      }} onClose={() => setSelected(null)} />}
    {error ? <p role="alert" className="mt-2">{error} <button type="button" className="btn btn-sm btn-outline-secondary"
      onClick={() => setReload(x => x + 1)}>Retry</button></p> : !ledger ? <p role="status" className="mt-2">Loading credit ledger…</p> :
      <>
        {ledger.totals.length === 0 ? <p className="mt-2 mb-1">No posted credit invoices.</p> :
          <>{ledger.totals.map(total => <p key={total.currencyCode} className="mt-2 mb-1">
            Credit sales: {total.currencyCode} {total.debit.toFixed(2)} · Received: {total.currencyCode} {total.paid.toFixed(2)} · Returns: {total.currencyCode} {(total.returned ?? 0).toFixed(2)} ·
            Outstanding: <strong>{total.currencyCode} {total.outstanding.toFixed(2)}</strong> · {total.invoiceCount} invoices
          </p>)}
          <div className="table-responsive"><table className="table table-sm align-middle">
            <thead><tr><th>Invoice</th><th>Location</th><th>Due date</th><th className="text-end">Invoice amount</th><th className="text-end">Received</th><th className="text-end">Returns</th><th className="text-end">Outstanding</th><th>Action</th></tr></thead>
            <tbody>{ledger.items.map(invoice => <tr key={invoice.id}>
              <td>#{invoice.id}</td><td>{invoice.locationName ?? '—'}</td><td>{invoice.creditDueDate}</td>
              <td className="text-end">{invoice.currencyCode} {invoice.debit.toFixed(2)}</td>
              <td className="text-end">{invoice.currencyCode} {invoice.paid.toFixed(2)}</td>
              <td className="text-end">{invoice.currencyCode} {(invoice.returned ?? 0).toFixed(2)}</td>
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
  const [formOpen, setFormOpen] = useState(false)
  const [ledgerCustomer, setLedgerCustomer] = useState<number | null>(null)
  const [ledgerSupplier, setLedgerSupplier] = useState<number | null>(null)
  const [originalLimit, setOriginalLimit] = useState<number | null>(null)
  const [creditLimit, setCreditLimit] = useState('')
  const [creditReason, setCreditReason] = useState('')
  const [creditOpen, setCreditOpen] = useState(false)
  const [creditError, setCreditError] = useState('')
  const creditDialog = useRef<HTMLDialogElement>(null)
  const creditSaving = useRef(false)
  const [loading, setLoading] = useState(true)
  const [saving, setSaving] = useState(false)
  const [loadError, setLoadError] = useState('')
  const nameInput = useRef<HTMLInputElement>(null)
  const basePath = `/businesses/${businessId}/${type}`
  const supplier = type === 'suppliers'
  const label = supplier ? 'Supplier' : 'Customer'

  useEffect(() => {
    const controller = new AbortController()
    setLoading(true)
    setLoadError('')
    const params = new URLSearchParams({ search: query, page: String(page) })
    api<ContactPage>(basePath + '?' + params, { signal: controller.signal })
      .then(value => {
        if (controller.signal.aborted) return
        setItems(value.items)
        setTotal(value.total)
      })
      .catch(cause => {
        if (controller.signal.aborted || cause instanceof SessionExpiredError) return
        setItems([])
        setTotal(0)
        setLoadError(cause instanceof Error ? cause.message : 'Unable to load records.')
      })
      .finally(() => { if (!controller.signal.aborted) setLoading(false) })
    return () => controller.abort()
  }, [basePath, page, query, reload])

  useEffect(() => {
    if (formOpen) nameInput.current?.focus()
  }, [formOpen, editingId])

  useLayoutEffect(() => {
    if (!creditOpen) return
    const dialog = creditDialog.current
    if (!dialog) return
    dialog.showModal()
    dialog.querySelector<HTMLInputElement>('input')?.focus()
    return () => { dialog.close() }
  }, [creditOpen])

  function closeCredit() {
    creditDialog.current?.close()
    setCreditOpen(false)
    setCreditError('')
    setCreditLimit(originalLimit == null ? '' : String(originalLimit))
    setCreditReason('')
  }

  function clearForm() {
    setCreditOpen(false)
    setFormOpen(false)
    setEditingId(null)
    setForm(emptyForm)
    setOriginalLimit(null)
    setCreditLimit('')
    setCreditReason('')
  }

  function edit(item: Contact) {
    setFormOpen(true)
    setEditingId(item.id)
    setOriginalLimit(item.creditLimit ?? null)
    setCreditLimit(item.creditLimit == null ? '' : String(item.creditLimit))
    setCreditReason('')
    setForm({ name: item.name, phone: item.phone, email: item.email, address: item.address ?? '', isActive: item.isActive })
  }

  const limitCents = moneyCents(creditLimit)
  const validCredit = originalLimit !== null && Number.isFinite(limitCents) &&
    limitCents <= 999999999999 && creditReason.trim().length >= 10 && limitCents / 100 !== originalLimit

  async function saveCredit(event: FormEvent) {
    event.preventDefault()
    if (!isOwner || supplier || !canOperate || saving || creditSaving.current || !creditOpen || editingId === null || !validCredit) return
    creditSaving.current = true
    setSaving(true)
    setCreditError('')
    try {
      const result = await api<{ id: number; creditLimit: number }>(basePath + '/' + editingId + '/credit-limit', {
        method: 'PUT', body: JSON.stringify({ limit: limitCents / 100, expectedLimit: originalLimit, reason: creditReason.trim() })
      })
      creditDialog.current?.close()
      setCreditOpen(false)
      setOriginalLimit(result.creditLimit)
      setCreditLimit(String(result.creditLimit))
      setCreditReason('')
      setReload(value => value + 1)
    } catch (cause) {
      if (!(cause instanceof SessionExpiredError))
        setCreditError(cause instanceof Error ? cause.message : 'Unable to update credit limit.')
    } finally { creditSaving.current = false; setSaving(false) }
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
        {canOperate && !formOpen && ledgerCustomer === null && <button type="button" className="btn btn-primary"
          onClick={() => { clearForm(); setFormOpen(true) }}>Add {label}</button>}
      </div>

      {!canOperate && <div className="alert alert-warning" role="status">
        Your subscription is inactive. Records are read-only.
      </div>}
      {loadError && <div className="alert alert-danger" role="alert">
        {loadError} <button type="button" className="btn btn-sm btn-outline-secondary ms-2"
          onClick={() => setReload(value => value + 1)}>Retry</button>
      </div>}

      {canOperate && formOpen && <form className="product-form" aria-busy={saving} onSubmit={event => void save(event)}>
        <h3>{editingId === null ? 'New ' : 'Edit '}{label.toLowerCase()}</h3>
        <div className="form-grid">
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
        {editingId !== null && <div className="form-check form-wide">
          <input id="contact-status" type="checkbox" className="form-check-input" checked={form.isActive} disabled={saving}
            onChange={event => setForm(current => ({ ...current, isActive: event.target.checked }))} />
          <label className="form-check-label" htmlFor="contact-status">Active</label>
        </div>}
        </div>
        <div className="form-footer">
          <button className="btn btn-outline-secondary" type="button" disabled={saving} onClick={clearForm}>Cancel</button>
          <ActionButton busy={saving} disabled={supplier && !form.phone.trim() && !form.email.trim()}>
            {editingId === null ? `Save ${label.toLowerCase()}` : 'Save changes'}
          </ActionButton>
        </div>
      </form>}

      {!supplier && isOwner && canOperate && formOpen && editingId !== null && originalLimit !== null &&
        <div className="d-flex align-items-center justify-content-between gap-2 mb-3">
          <span>Credit limit: <strong>{currencyCode} {originalLimit.toFixed(2)}</strong></span>
          <button type="button" className="btn btn-outline-secondary btn-sm" disabled={saving}
            aria-haspopup="dialog" onClick={() => setCreditOpen(true)}>Change credit limit</button>
        </div>}
      {creditOpen && <dialog ref={creditDialog} className="entry-dialog" aria-labelledby="credit-dialog-heading"
        onCancel={event => { event.preventDefault(); if (!creditSaving.current) closeCredit() }}
        onKeyDownCapture={event => {
          if (event.key === 'Escape') { event.preventDefault(); event.stopPropagation(); if (!creditSaving.current) closeCredit() }
        }}>
        <h3 className="h5 mb-3" id="credit-dialog-heading">Change credit limit</h3>
        <p className="small">Current limit: {currencyCode} {originalLimit?.toFixed(2)}. Zero prevents new credit sales.</p>
        <form aria-busy={saving} onSubmit={event => void saveCredit(event)}>
          <fieldset disabled={saving}>
            <div className="mb-3">
              <label className="form-label" htmlFor="customer-credit-limit">Credit limit ({currencyCode})</label>
              <input className="form-control" id="customer-credit-limit" type="number" min="0" max="9999999999.99"
                step="0.01" required value={creditLimit} onChange={event => setCreditLimit(event.target.value)} />
            </div>
            <label className="form-label" htmlFor="customer-credit-reason">Change reason</label>
            <input className="form-control" id="customer-credit-reason" required minLength={10} maxLength={200}
              value={creditReason} onChange={event => setCreditReason(event.target.value)} />
          </fieldset>
          {creditError && <p className="text-danger small mt-2" role="alert">{creditError}</p>}
          <div className="d-flex justify-content-end gap-2 mt-3">
            <button type="button" className="btn btn-outline-secondary" disabled={saving} onClick={closeCredit}>Cancel</button>
            <ActionButton busy={saving} disabled={!validCredit}>Save credit limit</ActionButton>
          </div>
        </form>
      </dialog>}

      {supplier && isOwner && ledgerSupplier !== null && <SupplierAccount key={basePath + ledgerSupplier}
        path={basePath + '/' + ledgerSupplier + '/ledger'} userId={userId} onClose={() => setLedgerSupplier(null)} />}
      {!supplier && isOwner && ledgerCustomer !== null &&
        <CustomerCreditLedger key={basePath + '/' + ledgerCustomer}
          path={basePath + '/' + ledgerCustomer + '/credit-ledger'} userId={userId} businessId={businessId} onClose={() => setLedgerCustomer(null)} />}

      {!formOpen && ledgerCustomer === null && <>
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
                  {(canOperate || isOwner) && <th scope="col" className="text-end">Action</th>}
                </tr></thead>
                <tbody>{items.map(item => <tr key={item.id}>
                  <td><strong>{item.name}</strong></td>
                  <td>{item.phone || '—'}</td>
                  <td>{item.email || '—'}</td>
                  {!supplier && isOwner && <td className="text-end">{item.creditLimit == null ? '—' : item.creditLimit.toFixed(2)}</td>}
                  <td>{item.isActive ? 'Active' : 'Inactive'}</td>
                  {(canOperate || isOwner) && <td className="text-end">
                    <div className="d-flex flex-wrap justify-content-end gap-1">
                      {canOperate && <button type="button" className="btn btn-sm btn-outline-secondary"
                        disabled={saving} onClick={() => edit(item)} aria-label={`Edit ${item.name}`}>Edit</button>}
                      {!supplier && isOwner && <button type="button" className="btn btn-sm btn-outline-secondary"
                        onClick={() => setLedgerCustomer(item.id)} aria-label={`Credit ledger for ${item.name}`}>Ledger</button>}
                       {supplier && isOwner && <button type="button" className="btn btn-sm btn-outline-secondary"
                         onClick={() => setLedgerSupplier(item.id)} aria-label={`Supplier ledger for ${item.name}`}>Ledger</button>}
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
      </>}
    </section>
  )
}
