import { useEffect, useState } from 'react'
import {
  AlertTriangle, BellRing, Building2, CalendarClock, ChevronRight,
  CirclePause, Construction, Eye, LockKeyhole, Megaphone, Package,
  RefreshCw, ShieldCheck, Wrench
} from 'lucide-react'
import { api, post, SessionExpiredError } from './api'

export type AvailabilityView = 'types' | 'modules' | 'maintenance' | 'announcements'
type ReleaseState = 'Available' | 'Coming Soon' | 'Under Construction' | 'Paused' | 'Retired'
type MaintenanceMode = 'Notice Only' | 'Read Only' | 'Unavailable'
type NoticeLevel = 'Information' | 'Warning' | 'Critical'

const releaseStates: { state: ReleaseState; summary: string }[] = [
  { state: 'Available', summary: 'Can be offered after server checks' },
  { state: 'Coming Soon', summary: 'Show availability notice; do not provision' },
  { state: 'Under Construction', summary: 'Visible as in development; do not provision' },
  { state: 'Paused', summary: 'Stop new provisioning; protect existing tenants' },
  { state: 'Retired', summary: 'No new provisioning; migration policy required' }
]

const moduleNames = [
  'Catalog & Products', 'Purchase Orders', 'Inventory & Stock', 'POS & Sales',
  'Returns & Refunds', 'Cash Register', 'Reports'
]

const viewDetails = {
  types: { title: 'Business Type Availability', description: 'Control which verticals can be offered when the real release-policy service is integrated.', Icon: Building2 },
  modules: { title: 'Module Release Status', description: 'Plan rollout states by module, without weakening role or subscription authorization.', Icon: Package },
  maintenance: { title: 'Maintenance Management', description: 'Design planned maintenance by scope, operating mode and a precise time window.', Icon: Wrench },
  announcements: { title: 'Announcements & Status Messages', description: 'Prepare visible update notices without pretending they are published.', Icon: Megaphone }
} as const

function StateBadge({ state }: { state: ReleaseState }) {
  const css = state === 'Available' ? 'is-available' : state === 'Paused' || state === 'Retired' ? 'is-paused' : 'is-pending'
  return <span className={`release-state-badge ${css}`}>{state}</span>
}

function DesignCaution({ message }: { message?: string }) {
  return <div className="release-caution" role="status"><LockKeyhole size={17} aria-hidden="true" />
    <span><strong>Design preview only.</strong> {message ??
      'These choices are local draft interactions. They are not saved, published or enforced by the backend.'}</span>
  </div>
}

type TypeControl = { id: number; code: string; name: string; isSupported: boolean;
  releaseState: ReleaseState; releaseRevision: number; canProvision: boolean }

function BusinessTypePreview() {
  const [types, setTypes] = useState<TypeControl[]>([])
  const [selected, setSelected] = useState<number | null>(null)
  const [desiredStatus, setDesiredStatus] = useState<ReleaseState>('Available')
  const [reason, setReason] = useState('')
  const [search, setSearch] = useState('')
  const [loading, setLoading] = useState(true)
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState('')
  const [notice, setNotice] = useState('')
  const [reload, setReload] = useState(0)
  const current = types.find(x => x.id === selected)
  const visible = types.filter(x => x.name.toLowerCase().includes(search.toLowerCase()) ||
    x.code.toLowerCase().includes(search.toLowerCase()))

  useEffect(() => {
    let active = true
    setLoading(true)
    setError('')
    api<TypeControl[]>('/platform/business-type-controls')
      .then(rows => {
        if (!active) return
        setTypes(rows)
        setSelected(previous => rows.some(x => x.id === previous) ? previous : rows[0]?.id ?? null)
      })
      .catch(cause => {
        if (!active || cause instanceof SessionExpiredError) return
        setTypes([])
        setError(cause instanceof Error ? cause.message : 'Unable to load business type policies.')
      })
      .finally(() => { if (active) setLoading(false) })
    return () => { active = false }
  }, [reload])

  useEffect(() => {
    if (current) setDesiredStatus(current.releaseState)
  }, [selected, current?.releaseRevision])

  async function save() {
    if (!current || saving || desiredStatus === current.releaseState || reason.trim().length < 10) return
    setSaving(true)
    setError('')
    setNotice('')
    try {
      await post(`/platform/business-type-controls/${current.code}/release`, {
        state: desiredStatus, expectedRevision: current.releaseRevision, reason: reason.trim()
      })
      setReason('')
      setNotice(`${current.name} release policy saved and audited.`)
      setReload(x => x + 1)
    } catch (cause) {
      if (!(cause instanceof SessionExpiredError))
        setError(cause instanceof Error ? cause.message : 'Unable to save release policy.')
    } finally { setSaving(false) }
  }

  return <>
    <div className="alert alert-info" role="status">
      Live policy for <strong>new business creation only.</strong> Existing businesses remain accessible.
      Only installed, verified types can be Available; a policy cannot install a new business module.
    </div>
    {error && <div className="alert alert-danger" role="alert">{error}</div>}
    {notice && <div className="alert alert-success" role="status">{notice}</div>}
    <div className="release-layout">
      <section className="release-list" aria-label="Business type catalogue">
        <div className="release-panel-heading"><h3>Business Types</h3><span>Live policy</span></div>
        <div className="release-list-toolbar">
          <label className="visually-hidden" htmlFor="release-business-search">Filter business types</label>
          <input className="form-control" id="release-business-search" type="search"
            placeholder="Filter by name or code" maxLength={80} value={search}
            onChange={event => setSearch(event.target.value)} />
          <button className="btn btn-outline-secondary btn-sm" type="button"
            disabled={loading} onClick={() => setReload(n => n + 1)} aria-label="Refresh business types">
            <RefreshCw size={16} /></button>
        </div>
        {loading ? <p className="release-empty" role="status">Loading policies…</p> :
          visible.length === 0 ? <p className="release-empty">No matching business types.</p> :
          <div className="release-type-list">{visible.map(type => <button key={type.id}
            type="button" onClick={() => { setSelected(type.id); setNotice(''); setReason('') }}
            className={`release-type-item${type.id === selected ? ' is-selected' : ''}`}
            aria-pressed={type.id === selected}>
            <span className="release-type-symbol"><Building2 size={19} /></span>
            <span><strong>{type.name}</strong><small>{type.code} · {type.releaseState}
              {type.canProvision ? ' · Accepting new businesses' : ' · Provisioning blocked'}</small></span>
            <ChevronRight size={17} aria-hidden="true" />
          </button>)}</div>}
      </section>
      <section className="release-editor" aria-label="Business type release policy">
        <div className="release-panel-heading"><h3>Release Policy</h3><span>Server enforced</span></div>
        {current ? <>
          <div className="release-selected"><strong>{current.name}</strong>
            <small>{current.code} · v{current.releaseRevision}</small></div>
          <label className="form-label" htmlFor="release-type-status">New business availability</label>
          <select id="release-type-status" className="form-select" value={desiredStatus}
            disabled={saving} onChange={event => setDesiredStatus(event.target.value as ReleaseState)}>
            {releaseStates.map(entry => <option key={entry.state} value={entry.state}
              disabled={entry.state === 'Available' && (!current.isSupported || current.code !== 'RETAIL')}>
              {entry.state}</option>)}
          </select>
          <div className="release-preview-card">
            <span className="release-preview-label"><Eye size={14} /> Current: {current.releaseState}</span>
            <StateBadge state={desiredStatus} />
            <p>{releaseStates.find(x => x.state === desiredStatus)?.summary}</p>
            <small>This only affects future provisioning, not existing tenant access.</small>
          </div>
          <label className="form-label mt-3" htmlFor="release-type-reason">Reason for change</label>
          <textarea id="release-type-reason" className="form-control" rows={2}
            maxLength={200} value={reason} onChange={event => setReason(event.target.value)}
            placeholder="Explain the release change for the audit log" />
          <button type="button" className="btn btn-primary mt-2"
            disabled={saving || desiredStatus === current.releaseState || reason.trim().length < 10}
            onClick={() => void save()}>{saving ? 'Saving…' : 'Apply release policy'}</button>
        </> : <p className="release-empty">Choose a business type to manage its availability.</p>}
      </section>
    </div>
  </>
}

function ModuleReleasePreview() {
  const [selected, setSelected] = useState(moduleNames[0])
  const [desiredStatus, setDesiredStatus] = useState<ReleaseState>('Under Construction')
  return <>
    <DesignCaution message="These are UI planning areas, not a fetched or authoritative module registry. Actual modules and entitlement rules must be server-owned before any policy is published." />
    <div className="release-layout">
      <section className="release-list" aria-label="Illustrative module families">
        <div className="release-panel-heading"><h3>Module Families</h3><span>Design reference</span></div>
        <div className="release-type-list">{moduleNames.map(name => <button key={name} type="button"
          className={`release-type-item${selected === name ? ' is-selected' : ''}`}
          aria-pressed={selected === name} onClick={() => { setSelected(name); setDesiredStatus('Under Construction') }}>
          <span className="release-type-symbol"><Package size={18} /></span>
          <span><strong>{name}</strong><small>Live module status: not connected</small></span>
          <ChevronRight size={17} /></button>)}</div>
      </section>
      <section className="release-editor" aria-label="Module release policy design">
        <div className="release-panel-heading"><h3>Module Policy</h3><span>Unsaved prototype</span></div>
        <div className="release-selected"><strong>{selected}</strong><small>Independent module release state</small></div>
        <label className="form-label" htmlFor="release-module-status">Choose a sample module state</label>
        <select className="form-select" id="release-module-status" value={desiredStatus}
          onChange={event => setDesiredStatus(event.target.value as ReleaseState)}>
          {releaseStates.map(entry => <option key={entry.state} value={entry.state}>{entry.state}</option>)}
        </select>
        <div className="release-preview-card"><span className="release-preview-label"><Eye size={14} /> Preview only</span>
          <StateBadge state={desiredStatus} />
          <p>With an authorized policy backend, the module can be marked as {desiredStatus.toLowerCase()}.</p>
          <small>Release status does not replace per-user permissions, plan entitlements or verified business-type compatibility.</small>
        </div>
      </section>
    </div>
  </>
}

function MaintenancePreview() {
  const [scope, setScope] = useState('Entire Platform')
  const [mode, setMode] = useState<MaintenanceMode>('Notice Only')
  const [title, setTitle] = useState('Scheduled platform update')
  const [message, setMessage] = useState('We are improving this service. Please check back shortly.')
  const [starts, setStarts] = useState('')
  const [ends, setEnds] = useState('')
  const invalidRange = starts !== '' && ends !== '' && new Date(ends).getTime() <= new Date(starts).getTime()
  return <>
    <DesignCaution message="This is a schedule and banner composition preview only. No maintenance window is scheduled, no writes are restricted and no site notice is published." />
    <div className="release-layout">
      <section className="release-editor release-editor-wide">
        <div className="release-panel-heading"><h3>Maintenance Window</h3><span>Unsaved prototype</span></div>
        <div className="release-form-grid">
          <div><label className="form-label" htmlFor="maintenance-scope">Target scope</label>
            <select id="maintenance-scope" className="form-select" value={scope}
              onChange={event => setScope(event.target.value)}>
              <option>Entire Platform</option><option>Business Type</option><option>Specific Module</option>
            </select></div>
          <div><label className="form-label" htmlFor="maintenance-mode">Operating mode</label>
            <select id="maintenance-mode" className="form-select" value={mode}
              onChange={event => setMode(event.target.value as MaintenanceMode)}>
              <option>Notice Only</option><option>Read Only</option><option>Unavailable</option>
            </select></div>
          <div><label className="form-label" htmlFor="maintenance-start">Start time (local preview)</label>
            <input id="maintenance-start" className="form-control" type="datetime-local" value={starts}
              onChange={event => setStarts(event.target.value)} /></div>
          <div><label className="form-label" htmlFor="maintenance-end">End time (local preview)</label>
            <input id="maintenance-end" className="form-control" type="datetime-local" value={ends}
              onChange={event => setEnds(event.target.value)} /></div>
          <div className="release-field-wide"><label className="form-label" htmlFor="maintenance-title">Visitor heading</label>
            <input id="maintenance-title" className="form-control" type="text" maxLength={100}
              value={title} onChange={event => setTitle(event.target.value)} /></div>
          <div className="release-field-wide"><label className="form-label" htmlFor="maintenance-message">Visitor message</label>
            <textarea id="maintenance-message" className="form-control" maxLength={300} rows={3}
              value={message} onChange={event => setMessage(event.target.value)} />
            <small className="release-input-hint">{message.length}/300 characters. Public text only; no HTML or secrets.</small></div>
        </div>
        {invalidRange && <p className="release-validation" role="alert">End time must be later than start time.</p>}
        {scope !== 'Entire Platform' && <p className="release-input-hint">Target selection by business-type/module code is deliberately not enabled until the verified registry exists.</p>}
      </section>
      <aside className="release-editor" aria-label="Maintenance visitor message preview">
        <div className="release-panel-heading"><h3>Visitor Preview</h3><span>Not published</span></div>
        <div className="release-preview-card">
          <span className="release-preview-label"><CalendarClock size={15} /> {scope}</span>
          <div className="release-maintenance-sign"><Wrench size={19} />
            <strong>{title.trim() || 'Maintenance notice'}</strong></div>
          <p>{message.trim() || 'Maintenance information will appear here.'}</p>
          <small>Requested mode: {mode}. Real activation requires owner confirmation, a reason, precise UTC scheduling, exceptions and rollback.</small>
        </div>
        <div className="release-policy-info">
          <span><BellRing size={15} /> Notice Only — show a banner, no API blocking</span>
          <span><CirclePause size={15} /> Read Only — block applicable writes on the server</span>
          <span><AlertTriangle size={15} /> Unavailable — return a correct maintenance response</span>
        </div>
      </aside>
    </div>
  </>
}

function AnnouncementsPreview() {
  const [level, setLevel] = useState<NoticeLevel>('Information')
  const [audience, setAudience] = useState('All visitors')
  const [title, setTitle] = useState('A new update is on the way')
  const [message, setMessage] = useState('We are preparing an improved experience. Some features may appear as coming soon.')
  return <>
    <DesignCaution message="This is a local message-composition preview. It does not send email, display a live banner or publish any public information." />
    <div className="release-layout">
      <section className="release-editor release-editor-wide">
        <div className="release-panel-heading"><h3>Notice Composer</h3><span>Unsaved prototype</span></div>
        <div className="release-form-grid">
          <div><label className="form-label" htmlFor="notice-severity">Message level</label>
            <select className="form-select" id="notice-severity" value={level}
              onChange={event => setLevel(event.target.value as NoticeLevel)}>
              <option>Information</option><option>Warning</option><option>Critical</option>
            </select></div>
          <div><label className="form-label" htmlFor="notice-audience">Audience</label>
            <select className="form-select" id="notice-audience" value={audience}
              onChange={event => setAudience(event.target.value)}>
              <option>All visitors</option><option>Business Owner / Manager</option>
              <option>Cashier / POS</option><option>Selected business type</option>
            </select></div>
          <div className="release-field-wide"><label className="form-label" htmlFor="notice-title">Message title</label>
            <input className="form-control" id="notice-title" maxLength={100} value={title}
              onChange={event => setTitle(event.target.value)} /></div>
          <div className="release-field-wide"><label className="form-label" htmlFor="notice-text">Message content</label>
            <textarea className="form-control" id="notice-text" maxLength={300} rows={4}
              value={message} onChange={event => setMessage(event.target.value)} />
            <small className="release-input-hint">{message.length}/300 characters; approved language variants will be required for production.</small></div>
        </div>
      </section>
      <aside className="release-editor">
        <div className="release-panel-heading"><h3>Banner Preview</h3><span>Not published</span></div>
        <div className={`release-public-banner release-level-${level.toLowerCase()}`}>
          {level === 'Critical' ? <AlertTriangle size={21} /> :
            level === 'Warning' ? <Construction size={21} /> : <BellRing size={21} />}
          <div><strong>{title.trim() || 'Announcement'}</strong>
            <p>{message.trim() || 'Enter a message to preview a visitor notice.'}</p></div>
        </div>
        <p className="release-input-hint mt-3">Audience preview: {audience}. Scheduling, acknowledgement, expiry and visibility need the public status API before deployment.</p>
      </aside>
    </div>
  </>
}

export default function PlatformAvailabilityPreview({ view }: { view: AvailabilityView }) {
  const definition = viewDetails[view]
  const Icon = definition.Icon
  return <div className="platform-release-controls">
    <div className="platform-preview-panel-title">
      <span className="platform-preview-panel-icon"><Icon size={20} /></span>
      <div><h2>{definition.title}</h2><p>{definition.description}</p></div>
    </div>
    {view === 'types' && <BusinessTypePreview />}
    {view === 'modules' && <ModuleReleasePreview />}
    {view === 'maintenance' && <MaintenancePreview />}
    {view === 'announcements' && <AnnouncementsPreview />}
    <div className="release-architecture-note"><ShieldCheck size={17} />
      <span>Planned enforcement: one audited, versioned Super Admin release policy; server-side business creation checks;
        scoped maintenance windows; independent messages; safe handling of existing tenants.</span>
    </div>
  </div>
}
