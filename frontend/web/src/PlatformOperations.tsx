import { useEffect, useState } from 'react'
import { Activity, ArrowRight, RefreshCw } from 'lucide-react'
import { api, SessionExpiredError } from './api'

type Readiness = 'checking' | 'ready' | 'unavailable'

export default function PlatformOperations({ onViewAudit }: { onViewAudit: () => void }) {
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

  return <div className="platform-operations-page">
    <div className="page-header">
      <div>
        <span className="platform-kicker">PLATFORM MANAGEMENT</span>
        <h1>Operations</h1>
        <p>System readiness and administrative audit.</p>
      </div>
      <button type="button" className="btn btn-outline-secondary btn-sm"
        disabled={status === 'checking'} onClick={() => setReload(value => value + 1)}>
        <RefreshCw size={15} /> Refresh readiness
      </button>
    </div>

    <section className="content-section platform-readiness-panel" aria-label="Verified readiness check">
      <span className="platform-operations-icon is-blue"><Activity size={26} /></span>
      <div>
        <span className="platform-kicker">LIVE API & SQL CHECK</span>
        <h2>Platform Readiness</h2>
        <p className="mb-2">This indicator uses the existing API readiness endpoint and database/migration check only.</p>
        {lastChecked && <small>Last checked: <time dateTime={lastChecked.toISOString()}>{lastChecked.toLocaleString()}</time></small>}
      </div>
      <span role="status" className={`platform-operations-status ${status === 'ready' ? 'is-ready' :
        status === 'unavailable' ? 'is-unavailable' : 'is-checking'}`}>
        {status === 'checking' ? 'Checking…' : status === 'ready' ? 'Readiness confirmed' : 'Not confirmed'}
      </span>
    </section>

    <button type="button" className="btn btn-outline-secondary mb-3" onClick={onViewAudit}>
      Open Security & Audit <ArrowRight size={16} />
    </button>

    <p className="platform-directory-note">
      A green readiness check is not proof of successful backups, restored data, payments,
      incident monitoring, completed jobs or release readiness.
    </p>
  </div>
}
