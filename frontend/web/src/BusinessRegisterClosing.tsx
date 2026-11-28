import { useEffect, useRef, useState } from 'react'
import { api, post, operationKey, ApiResponseError, type RetailDashboard } from './api'
import { moneyCents } from './posMoney'
import { MasterLookup } from './LookupDialog'

interface Session {
  id: number; actorUserId: number; currencyCode: string; locationId: number; openingCash: number
  openedAtUtc: string; closedAtUtc: string | null; closingExpectedCash: number | null
  closingActualCash: number | null; variance: number | null; closingReason: string | null
  closedByUserId: number | null; closingRequestKey: string | null
}
interface RegisterPage { items: Session[]; hasMore: boolean; canClose: boolean }
interface Summary {
  sessionId: number; currencyCode: string; openingCash: number; cashSales: number; cashRefunds: number
  netExchangeSettlement: number; creditCollections: number; cashIn: number; cashOut: number; expectedCash: number
}
interface CloseRequest { locationId: number; expectedCash: number; actualCash: number; reason: string; requestKey: string }

function ClosingDetail({ session, base, canClose, timeZone, onClosed, onBack }: {
  session: Session; base: string; canClose: boolean; timeZone: string; onClosed: () => void; onBack: () => void
}) {
  const [summary, setSummary] = useState<Summary | null>(null)
  const [actual, setActual] = useState('')
  const [reason, setReason] = useState('')
  const [confirmed, setConfirmed] = useState(false)
  const [error, setError] = useState('')
  const [busy, setBusy] = useState(false)
  const [reload, setReload] = useState(0)
  const [pending, setPending] = useState<CloseRequest | null>(null)
  const [recount, setRecount] = useState(false)
  const lock = useRef(false)
  useEffect(() => {
    const controller = new AbortController()
    setSummary(null); setConfirmed(false); setError('')
    api<Summary>(base + '/' + session.id + '/summary?locationId=' + session.locationId, { signal: controller.signal })
      .then(value => { if (!controller.signal.aborted) setSummary(value) })
      .catch(cause => { if (!controller.signal.aborted) setError(cause instanceof Error ? cause.message : 'Unable to reconcile register.') })
    return () => controller.abort()
  }, [base, session.id, session.locationId, reload])
  const cents = moneyCents(actual)
  const variance = summary && Number.isFinite(cents) ? cents / 100 - summary.expectedCash : null
  const valid = summary && Number.isFinite(cents) && Number.isSafeInteger(Math.round(summary.expectedCash * 100)) &&
    (Math.round((variance ?? 0) * 100) === 0 || reason.trim().length >= 5) && confirmed
  const money = (n: number) => session.currencyCode + ' ' + n.toFixed(2)
  async function close() {
    if (lock.current || !canClose || session.closedAtUtc || (!pending && !valid) || !summary) return
    const request = pending ?? { locationId: session.locationId, expectedCash: summary.expectedCash,
      actualCash: cents / 100, reason: reason.trim(), requestKey: operationKey() }
    lock.current = true; setBusy(true); setError(''); setRecount(false); setPending(request)
    try {
      const saved = await post<Session>(base + '/' + session.id + '/close', request)
      if (saved.id !== session.id || !saved.closedAtUtc || saved.closingRequestKey !== request.requestKey ||
        saved.closingExpectedCash !== request.expectedCash || saved.closingActualCash !== request.actualCash ||
        saved.closingReason !== request.reason) throw new Error('Closing confirmation did not match.')
      onClosed()
    } catch (cause) {
      if (cause instanceof ApiResponseError && cause.code === 'REGISTER_BALANCE_CHANGED') {
        // A definite server rejection of this request permits a fresh count. Unknown writes still use the original key.
        setPending(null); setConfirmed(false); setActual(''); setReason('')
        setReload(n => n + 1)
        setRecount(true)
      } else {
        setError((cause instanceof Error ? cause.message : 'Unable to verify closing.') + ' Check register history before counting or recording again.')
      }
    } finally { lock.current = false; setBusy(false) }
  }
  return <section aria-label="Register closing details">
    <div className="d-flex justify-content-between align-items-center mb-2">
      <h4 className="h6 mb-0">Shift #{session.id} · Operator #{session.actorUserId}</h4>
      <button type="button" className="btn btn-sm btn-outline-secondary" disabled={busy} onClick={onBack}>Back to shifts</button>
    </div>
    <p className="small">Opened {new Date(session.openedAtUtc).toLocaleString(undefined, { timeZone })}</p>
    {session.closedAtUtc && <div className="border rounded p-2 mb-2">
      <strong>Closed {new Date(session.closedAtUtc).toLocaleString(undefined, { timeZone })}</strong>
      {session.closingActualCash !== null && session.closingExpectedCash !== null && session.variance !== null
        ? <p className="mb-0">Expected {money(session.closingExpectedCash)} · Counted {money(session.closingActualCash)} · Variance {money(session.variance)}<br />Closed by #{session.closedByUserId} · {session.closingReason || 'Balanced'}</p>
        : <p>Historical closure has no counted-cash snapshot.</p>}
    </div>}
    {error && <p role="alert" className="text-danger">{error}</p>}
    {recount && <p role="status">Cash changed before closing. Review the refreshed summary and recount.</p>}
    {!summary && !error && <p role="status">Reconciling posted cash…</p>}
    {!summary && error && <button className="btn btn-sm btn-outline-secondary" onClick={() => setReload(n => n + 1)}>Retry summary</button>}
    {summary && <>
      <div className="table-responsive"><table className="table table-sm">
        <tbody>{([
          ['Opening cash', summary.openingCash], ['Cash sales', summary.cashSales],
          ['Cash refunds', -summary.cashRefunds], ['Net exchange settlement', summary.netExchangeSettlement],
          ['Credit collections', summary.creditCollections], ['Cash in', summary.cashIn],
          ['Cash out', -summary.cashOut], ['Expected cash', summary.expectedCash]
        ] as const).map(([label, amount]) => <tr key={label}><th scope="row">{label}</th><td className="text-end">{money(amount)}</td></tr>)}</tbody>
      </table></div>
      {!session.closedAtUtc && (canClose ? <form onSubmit={event => { event.preventDefault(); void close() }}>
        <fieldset disabled={busy || !!pending}>
          <label className="form-label" htmlFor="register-actual">Actual counted cash</label>
          <input id="register-actual" className="form-control mb-2" required type="number" min="0" step="0.01"
            value={actual} onChange={event => { setActual(event.target.value); setConfirmed(false) }} />
          {variance !== null && <p role="status">Cash variance: <strong>{money(variance)}</strong> ({variance < -0.005 ? 'Shortage' : variance > 0.005 ? 'Excess' : 'Balanced'})</p>}
          <label className="form-label" htmlFor="register-reason">Variance reason (required for differences)</label>
          <input id="register-reason" className="form-control mb-2" maxLength={200} value={reason}
            onChange={event => { setReason(event.target.value); setConfirmed(false) }} />
          <label className="form-check mb-2"><input type="checkbox" className="form-check-input" checked={confirmed}
            onChange={event => setConfirmed(event.target.checked)} />I counted this shift's cash and confirm closing.</label>
        </fieldset>
        {pending && <p role="status">Unresolved closing request. Retry the same record or open shift history.</p>}
        <button className="btn btn-primary" type="submit" disabled={busy || (!pending && !valid)}>
          {busy ? 'Closing…' : pending ? 'Retry same closing' : 'Close shift'}
        </button>
      </form> : <p>Register closing is unavailable: check subscription, release state or maintenance.</p>)}
    </>}
  </section>
}

export default function BusinessRegisterClosing({ dashboard }: { dashboard: RetailDashboard }) {
  const { business, locations } = dashboard
  const [locationId, setLocationId] = useState(locations[0]?.id ?? 0)
  const [openOnly, setOpenOnly] = useState(true)
  const [page, setPage] = useState(1)
  const [reload, setReload] = useState(0)
  const [data, setData] = useState<RegisterPage | null>(null)
  const [selected, setSelected] = useState<Session | null>(null)
  const [error, setError] = useState('')
  const [notice, setNotice] = useState('')
  const base = '/businesses/' + business.id + '/cash-register'
  useEffect(() => {
    if (!locationId) return
    const controller = new AbortController()
    setData(null); setError('')
    api<RegisterPage>(base + '?locationId=' + locationId + '&openOnly=' + openOnly + '&page=' + page, { signal: controller.signal })
      .then(value => { if (!controller.signal.aborted) setData(value) })
      .catch(cause => { if (!controller.signal.aborted) setError(cause instanceof Error ? cause.message : 'Unable to load registers.') })
    return () => controller.abort()
  }, [base, locationId, openOnly, page, reload])
  function refresh() { setSelected(null); setReload(n => n + 1) }
  if (selected && data) return <ClosingDetail key={selected.id} session={selected} base={base} canClose={data.canClose}
    timeZone={business.timeZoneId} onBack={refresh} onClosed={() => { setNotice('Shift #' + selected.id + ' closed successfully.'); setOpenOnly(false); setPage(1); refresh() }} />
  return <>
    <div className="d-flex flex-wrap align-items-end gap-2 mb-3">
      <div><label className="form-label" htmlFor="closing-location">Location</label>
        <MasterLookup id="closing-location" title="Select register location" value={locationId}
          emptyLabel="Select location" options={locations} onChange={id => { setLocationId(id); setPage(1); setNotice('') }} /></div>
      <label className="form-label mb-0">Shifts<select className="form-select" value={openOnly ? 'open' : 'all'}
        onChange={event => { setOpenOnly(event.target.value === 'open'); setPage(1) }}>
        <option value="open">Open shifts</option><option value="all">All shifts / history</option></select></label>
      <button type="button" className="btn btn-outline-secondary" onClick={refresh}>Refresh</button>
    </div>
    {notice && <p role="status" className="text-success">{notice}</p>}
    {error && <p role="alert" className="text-danger">{error}</p>}
    {!data && !error && locationId > 0 && <p role="status">Loading registers…</p>}
    {!locationId && <p>Choose a location.</p>}
    {data && <>
      <div className="table-responsive"><table className="table table-sm">
        <thead><tr><th>Shift / operator</th><th>Opened</th><th>Status</th><th>Variance</th><th>Action</th></tr></thead>
        <tbody>{data.items.map(session => <tr key={session.id}>
          <td>#{session.id} / #{session.actorUserId}</td>
          <td>{new Date(session.openedAtUtc).toLocaleString(undefined, { timeZone: business.timeZoneId })}</td>
          <td>{session.closedAtUtc ? 'Closed' : 'Open'}</td>
          <td>{session.variance == null ? '—' : session.currencyCode + ' ' + session.variance.toFixed(2)}</td>
          <td><button type="button" className="btn btn-sm btn-outline-secondary" onClick={() => setSelected(session)}>
            {session.closedAtUtc ? 'View closing' : 'Review shift'}</button></td>
        </tr>)}</tbody>
      </table></div>
      {!data.items.length && <p>No matching shifts.</p>}
      <div className="d-flex gap-2"><button className="btn btn-sm btn-outline-secondary" disabled={page === 1} onClick={() => setPage(n => n - 1)}>Previous</button>
        <button className="btn btn-sm btn-outline-secondary" disabled={!data.hasMore} onClick={() => setPage(n => n + 1)}>Next</button></div>
    </>}
  </>
}
