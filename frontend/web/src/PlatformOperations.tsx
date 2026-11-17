import { useEffect, useState } from 'react'
import { Activity, Database, RefreshCw } from 'lucide-react'
import { api, SessionExpiredError } from './api'

type Readiness = 'checking' | 'ready' | 'unavailable'

export default function PlatformOperations() {
  const [status, setStatus] = useState<Readiness>('checking')
  const [lastChecked, setLastChecked] = useState<Date | null>(null)
  const [reload, setReload] = useState(0)

  useEffect(() => {
    let active = true
    setStatus('checking')
    api<{ status: string }>('/ready')
      .then(result => {
        if (!active) return
        setStatus(result.status === 'ready' ? 'ready' : 'unavailable')
        setLastChecked(new Date())
      })
      .catch(error => {
        if (!active || error instanceof SessionExpiredError) return
        setStatus('unavailable')
        setLastChecked(new Date())
      })
    return () => { active = false }
  }, [reload])

  return <>
    <div className="page-header">
      <div>
        <h1>Operations</h1>
        <p>Read-only service readiness. Full monitoring and support are planned.</p>
      </div>
      <button type="button" className="btn btn-outline-secondary btn-sm"
        disabled={status === 'checking'} onClick={() => setReload(value => value + 1)}>
        <RefreshCw size={15} /> Refresh
      </button>
    </div>
    <section className="content-section" aria-label="Service readiness">
      <h2><Activity size={18} className="me-2" /> Platform Readiness</h2>
      <div className="d-flex align-items-start gap-3">
        <Database size={23} aria-hidden="true" className="text-secondary" />
        <div>
          <strong>API &amp; Database</strong>
          <p className="mb-1" role="status">
            {status === 'checking' ? 'Checking readiness…'
              : status === 'ready' ? 'Ready'
                : 'Readiness could not be confirmed'}
          </p>
          {lastChecked && <p className="small text-secondary mb-0">
            Last checked: {lastChecked.toLocaleString()}
          </p>}
        </div>
      </div>
      <p className="small text-secondary mt-3 mb-0">
        This check covers the existing API readiness endpoint and its database/migration checks.
        It does not verify backup restores, performance or external services.
      </p>
    </section>
    <section className="content-section mt-3">
      <h2>Planned Operations</h2>
      <p className="text-secondary mb-0">
        Error monitoring, backup/restore verification and customer support tickets are
        not yet connected. No operational health or backup status is assumed.
      </p>
    </section>
  </>
}
