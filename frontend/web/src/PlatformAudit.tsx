import { useEffect, useState } from 'react'
import { api, SessionExpiredError } from './api'

interface AuditEvent {
  id: number
  actorUserId: number
  action: string
  targetType: string
  targetId: number | null
  reason: string | null
  createdAtUtc: string
}

interface AuditPage {
  items: AuditEvent[]
  total: number
  page: number
  pageSize: number
}

export default function PlatformAudit() {
  const [page, setPage] = useState(1)
  const [reload, setReload] = useState(0)
  const [list, setList] = useState<AuditPage | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')

  useEffect(() => {
    let active = true
    setLoading(true)
    setError('')
    api<AuditPage>(`/platform/audit?page=${page}`)
      .then(result => { if (active) setList(result) })
      .catch(cause => {
        if (!active || cause instanceof SessionExpiredError) return
        setList(null)
        setError('Unable to load platform audit history.')
      })
      .finally(() => { if (active) setLoading(false) })
    return () => { active = false }
  }, [page, reload])

  return <>
    <div className="page-header">
      <div>
        <h1>Security & Audit</h1>
        <p>Platform owner sign-in history. Administrative changes will be audited when enabled.</p>
      </div>
      <button className="btn btn-outline-secondary btn-sm" type="button"
        disabled={loading} onClick={() => setReload(value => value + 1)}>Refresh</button>
    </div>
    <section className="content-section mb-3">
      <h2>Security Status</h2>
      <p className="mb-1">Super Admin MFA: <strong>Not configured</strong></p>
      <p className="small text-secondary mb-0">
        Multi-factor authentication is required before production launch. This screen is read-only.
      </p>
    </section>
    <section className="content-section">
      <h2>Audit History</h2>
      {error && <div className="alert alert-danger" role="alert">{error}
        <button className="btn btn-outline-secondary btn-sm ms-2" type="button"
          onClick={() => setReload(value => value + 1)}>Retry</button>
      </div>}
      {loading ? <p role="status">Loading audit history…</p>
        : list?.total === 0 ? <p className="text-secondary">No recorded platform events yet.</p>
        : list && <>
          <div className="table-responsive">
            <table className="table table-sm platform-table">
              <thead><tr><th>When</th><th>Action</th><th>Actor</th><th>Target</th></tr></thead>
              <tbody>{list.items.map(event => <tr key={event.id}>
                <td>{new Date(event.createdAtUtc).toLocaleString()}</td>
                <td>{event.action}<small className="d-block text-secondary">
                  {event.reason ?? ''}
                </small></td>
                <td>#{event.actorUserId}</td>
                <td>{event.targetType}{event.targetId === null ? '' : ` #${event.targetId}`}</td>
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
  </>
}
