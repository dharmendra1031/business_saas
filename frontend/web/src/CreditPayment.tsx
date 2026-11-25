import { useEffect, useRef, useState } from 'react'
import { api, operationKey, post } from './api'
import { moneyCents } from './posMoney'

export interface CreditInvoice {
  id: number; currencyCode: string; locationName?: string | null;
  debit: number; paid: number; outstanding: number; creditDueDate: string; postedAtUtc: string
}
interface Request {
  requestKey: string; amount: number; expectedOutstanding: number; cashReceivedConfirmed: true
}
export interface PendingCollection { invoice: CreditInvoice; request: Request }
interface Payment {
  id: number; saleId: number; requestKey: string; amount: number; method: string; postedAtUtc: string
}
interface History { items: Payment[]; hasMore: boolean }

// A failed storage read must not be treated as a fresh payment session.
export function readCollection(key: string): PendingCollection | null {
  const raw = sessionStorage.getItem(key)
  if (raw === null) return null
  const value = JSON.parse(raw) as PendingCollection
  if (!Number.isSafeInteger(value?.invoice?.id) || value.invoice.id <= 0 ||
      !/^[A-Z]{3}$/.test(value.invoice.currencyCode) ||
      !/^[a-f0-9]{32}$/.test(value.request?.requestKey) ||
      value.request.cashReceivedConfirmed !== true ||
      !(moneyCents(value.request.amount) > 0) ||
      !(moneyCents(value.request.expectedOutstanding) >= moneyCents(value.request.amount)))
    throw new Error('Saved collection is invalid. Reconcile it before taking another payment.')
  return value
}

export default function CreditPayment({ customerPath, invoice, storageKey, pending, canCollect, onPending, onRecorded, onClose }: {
  customerPath: string; invoice: CreditInvoice; storageKey: string; pending: PendingCollection | null;
  canCollect: boolean; onPending: (value: PendingCollection | null) => void;
  onRecorded: (payment: Payment) => void; onClose: () => void
}) {
  const [amount, setAmount] = useState('')
  const [confirmed, setConfirmed] = useState(false)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  const [page, setPage] = useState(1)
  const [history, setHistory] = useState<History | null>(null)
  const [historyError, setHistoryError] = useState('')
  const [reload, setReload] = useState(0)
  const lock = useRef(false)
  const mounted = useRef(true)
  const path = customerPath + '/credit-invoices/' + invoice.id + '/payments'
  useEffect(() => { mounted.current = true; return () => { mounted.current = false } }, [])
  useEffect(() => {
    let active = true
    setHistory(null); setHistoryError('')
    api<History>(path + '?page=' + page).then(value => {
      if (active) setHistory(value)
    }).catch(cause => {
      if (active) setHistoryError(cause instanceof Error ? cause.message : 'Unable to load payments.')
    })
    return () => { active = false }
  }, [path, page, reload])

  const cents = moneyCents(amount)
  const outstandingCents = moneyCents(invoice.outstanding)
  const valid = cents > 0 && cents <= outstandingCents && confirmed
  function verify(payment: Payment, request: Request) {
    if (!Number.isSafeInteger(payment.id) || payment.id <= 0 || payment.saleId !== invoice.id ||
        payment.requestKey !== request.requestKey || moneyCents(payment.amount) !== moneyCents(request.amount) ||
        payment.method !== 'Cash') throw new Error('Payment response could not be verified. Check the saved request.')
  }
  function complete(payment: Payment, request: Request) {
    verify(payment, request)
    sessionStorage.removeItem(storageKey)
    if (mounted.current) {
      onPending(null)
      onRecorded(payment)
    }
  }
  async function recover(lookupOnly: boolean) {
    if (lock.current || !pending) return
    lock.current = true; setBusy(true); setError('')
    try {
      const request = pending.request
      const result = await api<History>(path + '?requestKey=' + encodeURIComponent(request.requestKey))
      if (result.items.length === 1) complete(result.items[0], request)
      else if (result.items.length !== 0) throw new Error('Unexpected payment history. Reconcile before continuing.')
      else if (lookupOnly) setError('No matching payment found. Do not take cash again. Retry the same saved request only after checking the cash received.')
      else complete(await post<Payment>(path, request), request)
    } catch (cause) {
      if (mounted.current) setError(cause instanceof Error ? cause.message : 'Unable to verify payment.')
    } finally { lock.current = false; if (mounted.current) setBusy(false) }
  }
  async function record() {
    if (lock.current || pending || !canCollect || !valid) return
    lock.current = true; setBusy(true); setError('')
    try {
      if (readCollection(storageKey)) throw new Error('An earlier collection is unresolved. Reopen the ledger to recover it.')
      const request: Request = { requestKey: operationKey(), amount: cents / 100,
        expectedOutstanding: outstandingCents / 100, cashReceivedConfirmed: true }
      const saved = { invoice, request }
      sessionStorage.setItem(storageKey, JSON.stringify(saved))
      onPending(saved)
      complete(await post<Payment>(path, request), request)
    } catch (cause) {
      if (mounted.current) setError(cause instanceof Error ? cause.message : 'Unable to record payment.')
    } finally { lock.current = false; if (mounted.current) setBusy(false) }
  }
  return <section className="border rounded p-2 my-2" aria-label="Invoice payment">
    <div className="d-flex flex-wrap justify-content-between align-items-center gap-2">
      <h4 className="h6 mb-0">Invoice #{invoice.id} — Payment history</h4>
      <button type="button" className="btn btn-sm btn-outline-secondary" disabled={busy} onClick={onClose}>Close payment</button>
    </div>
    <p className="small mt-2 mb-1">Collection location: {invoice.locationName ?? 'Original invoice location'} · {invoice.currencyCode}</p>
    {pending ? <>
      <p role="status" className="alert alert-warning py-2">Unresolved collection of {invoice.currencyCode} {pending.request.amount.toFixed(2)}.
        Do not receive cash again. Check history or retry the exact saved request.</p>
      <div className="d-flex flex-wrap gap-2">
        <button type="button" className="btn btn-sm btn-outline-secondary" disabled={busy} onClick={() => void recover(true)}>Check saved payment</button>
        <button type="button" className="btn btn-sm btn-primary" disabled={busy || !canCollect} onClick={() => void recover(false)}>Retry same payment</button>
      </div>
    </> : canCollect && outstandingCents > 0 ? <form className="mt-2" onSubmit={event => { event.preventDefault(); void record() }}>
      <p className="mb-2">Outstanding: <strong>{invoice.currencyCode} {invoice.outstanding.toFixed(2)}</strong></p>
      <label className="form-label" htmlFor="credit-payment-amount">Cash received ({invoice.currencyCode})</label>
      <input id="credit-payment-amount" className="form-control" type="number" min="0.01" max={invoice.outstanding}
        step="0.01" required disabled={busy} value={amount} onChange={event => { setAmount(event.target.value); setConfirmed(false) }} />
      {cents > 0 && cents <= outstandingCents && <p className="small mt-1">Remaining: {invoice.currencyCode} {((outstandingCents - cents) / 100).toFixed(2)}</p>}
      <label className="form-check my-2"><input className="form-check-input" type="checkbox" disabled={busy}
        checked={confirmed} onChange={event => setConfirmed(event.target.checked)} /> Cash already received for this invoice.</label>
      <button type="submit" className="btn btn-primary btn-sm" disabled={busy || !valid}>Record payment</button>
    </form> : <p className="small mt-2">No new payment can be entered here.</p>}
    {error && <p role="alert" className="alert alert-danger py-2 mt-2">{error}</p>}
    <hr className="my-2" />
    {historyError ? <p role="alert">{historyError} <button type="button" className="btn btn-sm btn-outline-secondary" onClick={() => setReload(x => x + 1)}>Retry history</button></p>
      : !history ? <p role="status">Loading payments…</p>
      : history.items.length === 0 ? <p>No recorded payments.</p>
      : <><div className="table-responsive"><table className="table table-sm mb-1">
          <thead><tr><th>Payment</th><th>Posted (UTC)</th><th>Method</th><th className="text-end">Amount</th></tr></thead>
          <tbody>{history.items.map(item => <tr key={item.id}><td>#{item.id}</td><td>{item.postedAtUtc.replace('T', ' ').slice(0, 16)}</td>
            <td>{item.method}</td><td className="text-end">{invoice.currencyCode} {item.amount.toFixed(2)}</td></tr>)}</tbody>
        </table></div>{(page > 1 || history.hasMore) && <div className="btn-group">
          <button type="button" className="btn btn-sm btn-outline-secondary" disabled={page === 1 || busy} onClick={() => setPage(x => x - 1)}>Previous payments</button>
          <button type="button" className="btn btn-sm btn-outline-secondary" disabled={!history.hasMore || busy} onClick={() => setPage(x => x + 1)}>Next payments</button>
        </div>}</>}
  </section>
}
