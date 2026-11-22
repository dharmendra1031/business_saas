import { useEffect, useState } from 'react'
import { Activity, ArrowRight, Bell, Database, FileClock, HardDrive, RefreshCw, ServerCog, ShieldCheck } from 'lucide-react'
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
        <p>Platform readiness, audit navigation and future infrastructure administration.</p>
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

    <div className="platform-operations-section-heading">
      <div><h2>Platform Operations</h2>
        <p>Infrastructure actions stay unavailable until backed by real authorized endpoints and audit.</p></div>
      <span className="platform-data-tag">One live check · Design previews</span>
    </div>
    <div className="platform-operations-grid">
      <article className="content-section platform-operations-card">
        <span className="platform-operations-icon"><Database size={23} /></span>
        <h3>Tenant Databases</h3>
        <p>Review isolated tenant infrastructure, connection policy and approved data migrations.</p>
        <span className="platform-operations-card-status">Design preview · Not connected</span>
      </article>
      <article className="content-section platform-operations-card">
        <span className="platform-operations-icon"><HardDrive size={23} /></span>
        <h3>Data & Backups</h3>
        <p>Display real backup, restore-drill results and approved recovery operations.</p>
        <span className="platform-operations-card-status">Design preview · Not verified</span>
      </article>
      <article className="content-section platform-operations-card">
        <span className="platform-operations-icon"><ServerCog size={23} /></span>
        <h3>System Jobs</h3>
        <p>Inspect scheduled job runs, failures, retries and audit-protected operator actions.</p>
        <span className="platform-operations-card-status">Design preview · Not connected</span>
      </article>
      <article className="content-section platform-operations-card">
        <span className="platform-operations-icon"><Activity size={23} /></span>
        <h3>Incident Monitoring</h3>
        <p>Review actual incidents and service metrics when monitoring APIs are integrated.</p>
        <span className="platform-operations-card-status">Design preview · Not connected</span>
      </article>
      <article className="content-section platform-operations-card">
        <span className="platform-operations-icon"><ShieldCheck size={23} /></span>
        <h3>Audit Trail</h3>
        <p>Review recorded platform actions and protected administrative changes.</p>
        <button type="button" className="platform-link-action" onClick={onViewAudit}>
          Open Security & Audit <ArrowRight size={16} />
        </button>
      </article>
      <article className="content-section platform-operations-card">
        <span className="platform-operations-icon"><Bell size={23} /></span>
        <h3>Announcements</h3>
        <p>Draft, review and publish targeted platform notices only after delivery integration.</p>
        <span className="platform-operations-card-status"><FileClock size={15} /> Design preview · Not connected</span>
      </article>
    </div>

    <p className="platform-directory-note">
      A green readiness check is not proof of successful backups, restored data, payments,
      incident monitoring, completed jobs or release readiness.
    </p>
  </div>
}
