import { useEffect, useLayoutEffect, useRef, useState, type FormEvent } from 'react'
import { api, post, operationKey } from './api'
import { ActionButton } from './ActionFeedback'
import { moneyCents } from './posMoney'

type Kind = 'Invoice' | 'CreditNote' | 'Payment'
interface Totals { currencyCode: string; billed: number; credited: number; paid: number; outstanding: number }
interface Row { id: number; receivingId: number; kind: Kind; amount: number; currencyCode: string; reference: string; requestKey: string; purchaseReturnId: number | null; postedAtUtc: string }
interface Account extends Totals {
  name?: string; totals?: Totals[]; invoiceId?: number | null; canPost?: boolean
  returns?: { id: number; quantity: number; reason: string }[]; items: Row[]; hasMore: boolean
}
interface Request { kind: Kind; amount: number; reference: string; requestKey: string; purchaseReturnId: number | null; expectedOutstanding: number; confirmed: true }
function readPending(key: string): { request: Request | null; error: string } {
  try {
    const raw = sessionStorage.getItem(key)
    if (!raw) return { request: null, error: '' }
    const r = JSON.parse(raw) as Request
    if (!['Invoice', 'CreditNote', 'Payment'].includes(r.kind) || !/^[a-f0-9]{32}$/.test(r.requestKey) ||
      !Number.isFinite(r.amount) || moneyCents(r.amount) <= 0 || typeof r.reference !== 'string' ||
      !Number.isFinite(r.expectedOutstanding) || r.confirmed !== true ||
      (r.kind === 'CreditNote' ? !Number.isSafeInteger(r.purchaseReturnId) || !(Number(r.purchaseReturnId) > 0) : r.purchaseReturnId !== null))
      throw new Error()
    return { request: r, error: '' }
  } catch { return { request: null, error: 'Saved supplier transaction cannot be read. Reconcile it before posting another.' } }
}

export default function SupplierAccount({ path, receiptId, userId, onClose }: {
  path: string; receiptId?: number; userId: number; onClose: () => void
}) {
  const storageKey = 'supplier-document:' + userId + ':' + path
  const [recovery] = useState(() => receiptId ? readPending(storageKey) : { request: null, error: '' })
  const [pending, setPending] = useState<Request | null>(recovery.request)
  const [data, setData] = useState<Account | null>(null)
  const [kind, setKind] = useState<Kind>('Invoice')
  const [amount, setAmount] = useState('')
  const [reference, setReference] = useState('')
  const [returnId, setReturnId] = useState(0)
  const [confirmed, setConfirmed] = useState(false)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  const [notice, setNotice] = useState('')
  const [page, setPage] = useState(1)
  const [reload, setReload] = useState(0)
  const dialog = useRef<HTMLDialogElement>(null)
  const lock = useRef(false)
  useLayoutEffect(() => { const el = dialog.current; el?.showModal(); return () => { el?.close() } }, [])
  useEffect(() => {
    const controller = new AbortController()
    setData(null); setError('')
    api<Account>(path + '?page=' + page, { signal: controller.signal })
      .then(value => { if (!controller.signal.aborted) { setData(value); setKind(value.invoiceId ? 'Payment' : 'Invoice') } })
      .catch(cause => { if (!controller.signal.aborted) setError(cause instanceof Error ? cause.message : 'Unable to load account.') })
    return () => controller.abort()
  }, [path, page, reload])
  const cents = moneyCents(amount)
  const valid = !!data && data.canPost === true && !recovery.error && cents > 0 && cents <= 99999999999999 &&
    reference.trim().length > 0 && confirmed &&
    (kind === 'Invoice' ? !data.invoiceId : kind === 'Payment' ? cents <= moneyCents(data.outstanding) :
      returnId > 0 && cents <= moneyCents(data.billed) - moneyCents(data.credited))
  function verify(row: Row, request: Request) {
    if (!(row.id > 0) || row.receivingId !== receiptId || row.kind !== request.kind ||
      moneyCents(row.amount) !== moneyCents(request.amount) || row.reference !== request.reference ||
      row.requestKey !== request.requestKey || row.purchaseReturnId !== request.purchaseReturnId)
      throw new Error('Saved document confirmation did not match. Verify the original request.')
    sessionStorage.removeItem(storageKey)
    setPending(null); setAmount(''); setReference(''); setReturnId(0); setConfirmed(false)
    setNotice('Supplier document #' + row.id + ' verified.'); setPage(1); setReload(x => x + 1)
  }
  async function recover(lookupOnly: boolean) {
    if (!pending || lock.current || recovery.error) return
    lock.current = true; setBusy(true); setError('')
    try {
      const result = await api<Account>(path + '?requestKey=' + encodeURIComponent(pending.requestKey))
      if (result.items.length === 1) verify(result.items[0], pending)
      else if (!lookupOnly && result.canPost) verify(await post<Row>(path, pending), pending)
      else setError('No posted document found. Verify actual settlement before retrying the same request.')
    } catch (cause) { setError(cause instanceof Error ? cause.message : 'Unable to recover document.') }
    finally { lock.current = false; setBusy(false) }
  }
  async function submit(event: FormEvent) {
    event.preventDefault()
    if (!data || !valid || pending || lock.current) return
    const request: Request = { kind, amount: cents / 100, reference: reference.trim().toUpperCase(),
      purchaseReturnId: kind === 'CreditNote' ? returnId : null, expectedOutstanding: data.outstanding,
      requestKey: operationKey(), confirmed: true }
    lock.current = true; setBusy(true); setError(''); setNotice('')
    try {
      sessionStorage.setItem(storageKey, JSON.stringify(request))
      setPending(request)
      verify(await post<Row>(path, request), request)
    } catch (cause) { setError(cause instanceof Error ? cause.message : 'Unable to post document.') }
    finally { lock.current = false; setBusy(false) }
  }
  const totals = data ? data.totals ?? [data] : []
  return <dialog ref={dialog} className="entry-dialog" aria-label="Supplier account"
    onCancel={event => { event.preventDefault(); if (!lock.current) onClose() }}>
    <div className="d-flex justify-content-between align-items-center gap-2 mb-3">
      <h3 className="h5 mb-0">{receiptId ? 'Supplier account — receiving #' + receiptId : data?.name ?? 'Supplier ledger'}</h3>
      <button type="button" className="btn btn-sm btn-outline-secondary" disabled={busy} onClick={onClose}>Close</button>
    </div>
    {recovery.error && <p role="alert" className="text-danger">{recovery.error}</p>}
    {error && <p role="alert" className="text-danger">{error}</p>}
    {notice && <p role="status" className="text-success">{notice}</p>}
    {pending && <div className="border rounded p-2 mb-2">
      <p>Unresolved {pending.kind} · {pending.reference} · {pending.amount.toFixed(2)}. Do not pay again.</p>
      <button type="button" className="btn btn-sm btn-outline-secondary me-2" disabled={busy} onClick={() => void recover(true)}>Check saved document</button>
      <button type="button" className="btn btn-sm btn-primary" disabled={busy || !data?.canPost} onClick={() => void recover(false)}>Retry same record</button>
    </div>}
    {!data && !error && <p role="status">Loading supplier account…</p>}
    {!data && error && <button type="button" className="btn btn-outline-secondary" onClick={() => setReload(x => x + 1)}>Retry</button>}
    {data && <>
      {totals.length === 0 && <p>No posted financial documents.</p>}
      {totals.map(t => <div className="border rounded p-2 mb-2" key={t.currencyCode}>
        <strong>{t.outstanding < 0 ? 'Net supplier credit' : 'Net outstanding'}: {t.currencyCode} {Math.abs(t.outstanding).toFixed(2)}</strong>
        <div className="small">Invoiced {t.billed.toFixed(2)} · Credited {t.credited.toFixed(2)} · Paid {t.paid.toFixed(2)}</div>
      </div>)}
      {receiptId && !data.canPost && <p className="small">Financial posting is not released or the subscription is inactive.</p>}
      {receiptId && data.canPost && !pending && !recovery.error && <form onSubmit={event => void submit(event)} className="mb-3">
        <fieldset disabled={busy}>
          <label className="form-label" htmlFor="supplier-document-kind">Document</label>
          <select id="supplier-document-kind" className="form-select mb-2" value={kind} onChange={event => { setKind(event.target.value as Kind); setConfirmed(false) }}>
            {!data.invoiceId ? <option value="Invoice">Supplier invoice</option> : <>
              <option value="Payment">Record external supplier payment</option><option value="CreditNote">Supplier credit note</option>
            </>}
          </select>
          {kind === 'CreditNote' && <>
            <label className="form-label" htmlFor="supplier-return-id">Posted purchase return</label>
            <select id="supplier-return-id" className="form-select mb-2" value={returnId} onChange={event => setReturnId(Number(event.target.value))}>
              <option value={0}>Select return</option>
              {data.returns?.map(r => <option key={r.id} value={r.id}>#{r.id} · {r.quantity} · {r.reason}</option>)}
            </select>
          </>}
          <label className="form-label" htmlFor="supplier-document-ref">Actual document / settlement reference</label>
          <input id="supplier-document-ref" required maxLength={120} className="form-control mb-2" value={reference}
            onChange={event => setReference(event.target.value)} />
          <label className="form-label" htmlFor="supplier-document-amount">Amount ({data.currencyCode})</label>
          <input id="supplier-document-amount" required type="number" min="0.01" step="0.01" max="999999999999.99"
            className="form-control mb-2" value={amount} onChange={event => setAmount(event.target.value)} />
          <label className="d-flex gap-2 small mb-2"><input type="checkbox" checked={confirmed} onChange={event => setConfirmed(event.target.checked)} />
            {kind === 'Payment' ? 'Payment already settled outside this software. No cash drawer or bank transfer will occur.' : 'I verified the actual supplier document and amount.'}</label>
        </fieldset>
        <ActionButton busy={busy} disabled={!valid}>Post supplier document</ActionButton>
      </form>}
      <div className="table-responsive"><table className="table table-sm">
        <thead><tr><th>Document</th><th>Reference</th><th className="text-end">Amount</th></tr></thead>
        <tbody>{data.items.map(row => <tr key={row.id}><td>{row.kind} #{row.id}<small className="d-block">Receiving #{row.receivingId} · {row.postedAtUtc.slice(0, 10)}</small></td>
          <td>{row.reference}</td><td className="text-end">{row.kind === 'Invoice' ? '+' : '−'}{row.currencyCode} {row.amount.toFixed(2)}</td></tr>)}</tbody>
      </table></div>
      {data.items.length === 0 && <p>No transactions.</p>}
      <div className="d-flex gap-2"><button type="button" className="btn btn-sm btn-outline-secondary" disabled={page === 1 || busy} onClick={() => setPage(x => x - 1)}>Previous</button>
        <button type="button" className="btn btn-sm btn-outline-secondary" disabled={!data.hasMore || busy} onClick={() => setPage(x => x + 1)}>Next</button></div>
    </>}
  </dialog>
}
