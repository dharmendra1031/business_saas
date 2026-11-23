import { useEffect, useState } from 'react'
import { api, SessionExpiredError } from './api'
import { ChevronDown, FileClock, RefreshCw, ShieldCheck } from 'lucide-react'

interface AuditEvent {
  id: number
  actorUserId: number
  action: string
  targetType: string
  targetId: number | null
  reason: string | null
  previousValue: string | null
  newValue: string | null
  createdAtUtc: string
}

interface AuditPage {
  items: AuditEvent[]
  total: number
  page: number
  pageSize: number
}

export default function PlatformAudit({ eventId, onFindEvent, onClearEvent }: {
  eventId: number | null
  onFindEvent: (id: number) => void
  onClearEvent: () => void
}) {
  const [page, setPage] = useState(1)
  const [actorId, setActorId] = useState<number | null>(null)
  const [eventInput, setEventInput] = useState(eventId?.toString() ?? '')
  const searchId = Number(eventInput.trim())
  const validEventId = /^\d+$/.test(eventInput.trim()) && Number.isSafeInteger(searchId) && searchId > 0
  const [reload, setReload] = useState(0)
  const [list, setList] = useState<AuditPage | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')
  const [mfaStatus, setMfaStatus] = useState('Checking…')

  useEffect(() => {
    let active = true
    setLoading(true)
    setError('')
    const params = new URLSearchParams({ page: String(page) })
    if (eventId !== null) params.set('eventId', String(eventId))
    if (actorId !== null) params.set('actorUserId', String(actorId))
    api<AuditPage>(`/platform/audit?${params}`)
      .then(result => { if (active) setList(result) })
      .catch(cause => {
        if (!active || cause instanceof SessionExpiredError) return
        setList(null)
        setError('Unable to load platform audit history.')
      })
      .finally(() => { if (active) setLoading(false) })
    return () => { active = false }
  }, [page, reload, eventId, actorId])

  useEffect(() => {
    let active = true
    setMfaStatus('Checking…')
    api<{ enabled: boolean; verified: boolean }>('/auth/mfa/status')
      .then(result => {
        if (active) setMfaStatus(result.enabled && result.verified ? 'Verified' : 'Not verified')
      })
      .catch(cause => {
        if (active && !(cause instanceof SessionExpiredError)) setMfaStatus('Unavailable')
      })
    return () => { active = false }
  }, [reload])

  return <div className="platform-audit-page">
    <div className="page-header">
      <div>
        <span className="platform-kicker">SECURITY & COMPLIANCE</span>
        <h1>Security & Audit</h1>
        <p>Read-only, server-recorded platform actions and identity protections.</p>
      </div>
      <button className="btn btn-outline-secondary btn-sm" type="button"
        disabled={loading} onClick={() => setReload(value => value + 1)}><RefreshCw size={15} /> Refresh</button>
    </div>
    <section className="content-section platform-audit-security-card" aria-label="Security status">
      <span className="platform-audit-symbol"><ShieldCheck size={24} /></span>
      <div><span className="platform-kicker">SUPER ADMIN AUTHENTICATION</span>
        <h2>Authenticator verification</h2>
        <strong className={`platform-state-pill ${mfaStatus === 'Verified' ? 'is-active' : 'is-muted'}`}>{mfaStatus}</strong>
        <p>Verified with the authenticated platform account endpoint. Shop operations remain tenant-scoped.</p>
      </div>
      <span className="platform-audit-lock"><FileClock size={17} /> Read-only audit</span>
    </section>
    <section className="content-section platform-directory-card" aria-label="Platform audit log">
      <div className="platform-directory-heading"><div>
        <h2>Platform Activity Log</h2>
        <p>{eventId === null ? 'Recorded actor, entity, action, reason and timestamp' :
          `Showing audit event #${eventId}`}</p></div>
        {eventId !== null && <button className="btn btn-outline-secondary btn-sm" type="button"
          onClick={onClearEvent}>Show all events</button>}
        {actorId !== null && <button className="btn btn-outline-secondary btn-sm" type="button"
          onClick={() => { setActorId(null); setPage(1) }}>Clear actor #{actorId}</button>}
      </div>
      <form className="platform-search platform-directory-search" role="search" aria-label="Audit event lookup"
        onSubmit={event => {
          event.preventDefault()
          if (validEventId) onFindEvent(searchId)
        }}>
        <input className="form-control platform-directory-filter" type="text" inputMode="numeric"
          aria-label="Audit event ID" placeholder="Audit event ID" value={eventInput}
          onChange={event => setEventInput(event.target.value)} />
        <button className="btn btn-primary btn-sm" type="submit" disabled={!validEventId}>Find event</button>
      </form>
      {error && <div className="alert alert-danger" role="alert">{error}
        <button className="btn btn-outline-secondary btn-sm ms-2" type="button"
          onClick={() => setReload(value => value + 1)}>Retry</button>
      </div>}
      {loading ? <div className="platform-directory-loading" role="status">Loading audit history…</div>
        : list?.total === 0 ? <p className="platform-audit-note">{eventId !== null
          ? 'Audit event not found.' : actorId !== null ? 'No events for this actor.' :
            'No platform events have been recorded yet.'}</p>
        : list && <>
          <div className="table-responsive">
            <table className="table table-sm platform-table platform-directory-table">
              <thead><tr><th scope="col">Time</th><th scope="col">Action</th>
                <th scope="col">Actor</th><th scope="col">Target</th><th scope="col">Details</th></tr></thead>
              <tbody>{list.items.map(event => <tr key={event.id}>
                <td>{new Date(event.createdAtUtc).toLocaleString()}</td>
                <td><strong>{event.action}</strong><small className="d-block text-secondary">Event #{event.id}</small></td>
                <td>{eventId === null && actorId === null && event.actorUserId > 0
                  ? <button className="btn btn-link p-0" type="button"
                    aria-label={`Filter by actor #${event.actorUserId}`}
                    onClick={() => { setActorId(event.actorUserId); setPage(1) }}>#{event.actorUserId}</button>
                  : `#${event.actorUserId}`}</td>
                <td>{event.targetType}{event.targetId === null ? '' : ` #${event.targetId}`}</td>
                <td><details className="platform-audit-detail"><summary><ChevronDown size={14} /> View details</summary>
                  <dl><div><dt>Reason</dt><dd>{event.reason ?? 'Not recorded'}</dd></div>
                    <div><dt>Previous value</dt><dd>{event.previousValue ?? 'Not recorded'}</dd></div>
                    <div><dt>New value</dt><dd>{event.newValue ?? 'Not recorded'}</dd></div></dl>
                </details></td>
              </tr>)}</tbody>
            </table>
          </div>
          <div className="platform-summary">
            <p className="small text-secondary">
              {list.items.length
                ? `Showing ${(page - 1) * list.pageSize + 1}–${(page - 1) * list.pageSize + list.items.length} of ${list.total}`
                : `No events on page ${page}`}
            </p>
            <div className="d-flex align-items-center gap-2">
              <button className="btn btn-outline-secondary btn-sm" type="button" disabled={page <= 1}
                onClick={() => setPage(value => value - 1)}>Previous</button>
              <span className="small">Page {page}</span>
              <button className="btn btn-outline-secondary btn-sm" type="button"
                disabled={page * list.pageSize >= list.total}
                onClick={() => setPage(value => value + 1)}>Next</button>
            </div>
          </div>
        </>}
    </section>
  </div>
}
