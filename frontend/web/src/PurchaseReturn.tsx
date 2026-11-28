import { useEffect, useLayoutEffect, useRef, useState, type FormEvent } from 'react'
import { api, post, operationKey, UncertainWriteError } from './api'
import { ActionButton } from './ActionFeedback'
import { baseQuantity } from './stockQuantity'

interface Line { id: number; name: string; quantity: number; returned: number; remaining: number; unitCode: string; factor: number }
interface ReturnRow { id: number; receivingLineId: number; quantity: number; reason: string; postedAtUtc: string }
interface ReturnData { lines: Line[]; history: ReturnRow[] }
interface Request { receivingLineId: number; quantity: number; expectedReturned: number; reason: string; requestKey: string }

export default function PurchaseReturn({ path, receiptId, canOperate, onClose }: {
  path: string; receiptId: number; canOperate: boolean; onClose: () => void
}) {
  const dialog = useRef<HTMLDialogElement>(null)
  const lock = useRef(false)
  const [data, setData] = useState<ReturnData | null>(null)
  const [lineId, setLineId] = useState(0)
  const [quantity, setQuantity] = useState('')
  const [reason, setReason] = useState('')
  const [error, setError] = useState('')
  const [notice, setNotice] = useState('')
  const [busy, setBusy] = useState(false)
  const [reload, setReload] = useState(0)
  const [pending, setPending] = useState<Request | null>(null)
  useLayoutEffect(() => {
    const element = dialog.current
    element?.showModal()
    return () => { element?.close() }
  }, [])
  useEffect(() => {
    const controller = new AbortController()
    setData(null); setError('')
    api<ReturnData>(path, { signal: controller.signal })
      .then(value => { if (!controller.signal.aborted) setData(value) })
      .catch(cause => { if (!controller.signal.aborted) setError(cause instanceof Error ? cause.message : 'Unable to load returns.') })
    return () => controller.abort()
  }, [path, reload])
  const line = data?.lines.find(item => item.id === lineId)
  const amount = Number(quantity)
  const valid = !!line && amount > 0 && amount <= line.remaining &&
    baseQuantity(amount, line.factor) !== null && reason.trim().length >= 3
  function close() {
    if (lock.current) return
    if (pending && !window.confirm('Return status is unresolved. Check receiving return history before making another return. Close?')) return
    onClose()
  }
  async function submit(event: FormEvent) {
    event.preventDefault()
    if (!canOperate || lock.current || (!pending && (!valid || !line))) return
    if (!pending && !window.confirm('Confirm goods were returned to the supplier. This reduces available stock, not the supplier balance.')) return
    const request = pending ?? { receivingLineId: line!.id, quantity: amount,
      expectedReturned: line!.returned, reason: reason.trim(), requestKey: operationKey() }
    lock.current = true; setBusy(true); setPending(request); setError(''); setNotice('')
    try {
      const result = await post<{ id: number; receivingLineId: number; quantity: number; requestKey: string }>(path, request)
      if (!(result.id > 0) || result.receivingLineId !== request.receivingLineId ||
          result.quantity !== request.quantity || result.requestKey !== request.requestKey)
        throw new UncertainWriteError('Return confirmation did not match the request.')
      setPending(null); setLineId(0); setQuantity(''); setReason('')
      setNotice('Purchase return #' + result.id + ' posted. Stock updated.')
      setReload(value => value + 1)
    } catch (cause) {
      setError(cause instanceof Error ? cause.message : 'Return failed.')
      // Preserve the exact operation key even after a conflict: the first write may have succeeded.
    } finally { lock.current = false; setBusy(false) }
  }
  return <dialog ref={dialog} className="entry-dialog" aria-label="Purchase returns" onCancel={event => { event.preventDefault(); close() }}>
    <div className="d-flex align-items-center justify-content-between gap-2 mb-2">
      <h3 className="h5 mb-0">Purchase returns — receiving #{receiptId}</h3>
      <button type="button" className="btn btn-sm btn-outline-secondary" disabled={busy} onClick={close}>Close</button>
    </div>
    <p className="small">Return quantities use the original purchase unit. This posts stock only, not a refund or supplier payment.</p>
    {notice && <p role="status" className="text-success">{notice}</p>}
    {error && <p role="alert" className="text-danger">{error}</p>}
    {!data && !error && <p role="status">Loading return details…</p>}
    {!data && error && <button type="button" className="btn btn-outline-secondary" onClick={() => setReload(value => value + 1)}>Retry</button>}
    {data && <>
      <div className="table-responsive"><table className="table table-sm">
        <thead><tr><th>Item</th><th>Received</th><th>Returned</th><th>Remaining</th></tr></thead>
        <tbody>{data.lines.map(item => <tr key={item.id}><td>{item.name} · {item.unitCode}</td>
          <td>{item.quantity}</td><td>{item.returned}</td><td>{item.remaining}</td></tr>)}</tbody>
      </table></div>
      {canOperate && <form onSubmit={event => void submit(event)}>
        <fieldset disabled={busy || pending !== null}>
          <label className="form-label" htmlFor="purchase-return-line">Item</label>
          <select id="purchase-return-line" required className="form-select mb-2" value={lineId}
            onChange={event => { setLineId(Number(event.target.value)); setQuantity('') }}>
            <option value={0}>Select item</option>
            {data.lines.filter(item => item.remaining > 0).map(item => <option key={item.id} value={item.id}>{item.name} ({item.remaining} {item.unitCode})</option>)}
          </select>
          <label className="form-label" htmlFor="purchase-return-quantity">Return quantity</label>
          <input id="purchase-return-quantity" required className="form-control mb-2" type="number"
            min="0.001" max={line?.remaining ?? 0} step="0.001" value={quantity} onChange={event => setQuantity(event.target.value)} />
          <label className="form-label" htmlFor="purchase-return-reason">Reason</label>
          <input id="purchase-return-reason" required minLength={3} maxLength={200} className="form-control mb-2"
            value={reason} onChange={event => setReason(event.target.value)} />
        </fieldset>
        <ActionButton busy={busy} disabled={!pending && !valid}>{pending ? 'Retry same return' : 'Post purchase return'}</ActionButton>
      </form>}
      <h4 className="h6 mt-3">Recent posted returns (up to 50)</h4>
      {data.history.length === 0 ? <p>No returns posted.</p> : <div className="table-responsive"><table className="table table-sm">
        <thead><tr><th>Return</th><th>Item / Qty</th><th>Reason</th></tr></thead>
        <tbody>{data.history.map(item => <tr key={item.id}><td>#{item.id}</td>
          <td>{data.lines.find(line => line.id === item.receivingLineId)?.name} · {item.quantity}</td><td>{item.reason}</td></tr>)}</tbody>
      </table></div>}
    </>}
  </dialog>
}
