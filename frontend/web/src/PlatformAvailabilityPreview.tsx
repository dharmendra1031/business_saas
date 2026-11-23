import { useEffect, useState } from 'react'
import {
  Building2, ChevronRight, Eye, LockKeyhole, Megaphone, Package,
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

type MaintenanceRow = { id: number; mode: string; title: string; message: string;
  startUtc: string; endUtc: string; isCancelled: boolean }
type AnnouncementRow = { id: number; level: string; title: string; message: string;
  startUtc: string; endUtc: string; isArchived: boolean }

function utc(value: string): string { return new Date(value).toISOString() }

function MaintenancePreview() {
  const [rows, setRows] = useState<MaintenanceRow[]>([])
  const [mode, setMode] = useState<'NoticeOnly' | 'ReadOnly' | 'Unavailable'>('NoticeOnly')
  const [title, setTitle] = useState('Scheduled platform update')
  const [message, setMessage] = useState('We are updating this service. Please check back shortly.')
  const [reason, setReason] = useState('')
  const [starts, setStarts] = useState('')
  const [ends, setEnds] = useState('')
  const [error, setError] = useState('')
  const [notice, setNotice] = useState('')
  const [busy, setBusy] = useState(false)
  const [reload, setReload] = useState(0)
  useEffect(() => {
    let active = true
    api<MaintenanceRow[]>('/platform/maintenance').then(data => {
      if (active) setRows(data)
    }).catch(cause => {
      if (active && !(cause instanceof SessionExpiredError))
        setError(cause instanceof Error ? cause.message : 'Unable to read maintenance history.')
    })
    return () => { active = false }
  }, [reload])

  const validRange = starts !== '' && ends !== '' && Date.parse(starts) < Date.parse(ends) &&
    Date.parse(ends) - Date.parse(starts) <= 24 * 60 * 60 * 1000
  async function save() {
    if (busy || !validRange) return
    setBusy(true); setError(''); setNotice('')
    try {
      await post('/platform/maintenance', {
        mode, title: title.trim(), message: message.trim(), reason: reason.trim(),
        startUtc: utc(starts), endUtc: utc(ends)
      })
      setNotice('Maintenance window scheduled and audited. The server will enforce it at the selected time.')
      setReason(''); setReload(x => x + 1)
    } catch (cause) {
      if (!(cause instanceof SessionExpiredError))
        setError(cause instanceof Error ? cause.message : 'Unable to schedule maintenance.')
    } finally { setBusy(false) }
  }
  async function cancel(id: number) {
    if (busy || reason.trim().length < 10) return
    setBusy(true); setError(''); setNotice('')
    try {
      await post(`/platform/maintenance/${id}/cancel`, { reason: reason.trim() })
      setNotice('Maintenance cancelled. The policy no longer blocks tenant operations.')
      setReason(''); setReload(x => x + 1)
    } catch (cause) {
      if (!(cause instanceof SessionExpiredError))
        setError(cause instanceof Error ? cause.message : 'Unable to cancel maintenance.')
    } finally { setBusy(false) }
  }

  return <>
    <div className="alert alert-info" role="status">
      Live global maintenance. <strong>Notice Only</strong> displays a banner;
      <strong> Read Only</strong> blocks tenant writes; <strong>Unavailable</strong> blocks
      tenant operations. Platform recovery, sign-in and health endpoints stay accessible.
      Scope-specific maintenance is not enabled yet.
    </div>
    {error && <div role="alert" className="alert alert-danger">{error}</div>}
    {notice && <div role="status" className="alert alert-success">{notice}</div>}
    <div className="release-layout">
      <section className="release-editor release-editor-wide">
        <div className="release-panel-heading"><h3>Schedule Global Maintenance</h3><span>Audited</span></div>
        <div className="release-form-grid">
          <div><label className="form-label" htmlFor="maintenance-mode">Mode</label>
            <select id="maintenance-mode" className="form-select" value={mode}
              onChange={event => setMode(event.target.value as typeof mode)}>
              <option value="NoticeOnly">Notice Only</option>
              <option value="ReadOnly">Read Only</option>
              <option value="Unavailable">Unavailable</option>
            </select></div>
          <div><label className="form-label" htmlFor="maintenance-start">Start (local time)</label>
            <input type="datetime-local" id="maintenance-start" className="form-control"
              value={starts} onChange={event => setStarts(event.target.value)} /></div>
          <div><label className="form-label" htmlFor="maintenance-end">End (local time)</label>
            <input type="datetime-local" id="maintenance-end" className="form-control"
              value={ends} onChange={event => setEnds(event.target.value)} /></div>
          <div className="release-field-wide"><label className="form-label" htmlFor="maintenance-title">Public heading</label>
            <input id="maintenance-title" className="form-control" maxLength={100}
              value={title} onChange={event => setTitle(event.target.value)} /></div>
          <div className="release-field-wide"><label className="form-label" htmlFor="maintenance-message">Public message</label>
            <textarea id="maintenance-message" rows={3} className="form-control" maxLength={300}
              value={message} onChange={event => setMessage(event.target.value)} /></div>
          <div className="release-field-wide"><label className="form-label" htmlFor="maintenance-reason">Operator reason (required for schedule and cancellation)</label>
            <textarea id="maintenance-reason" rows={2} className="form-control" maxLength={200}
              value={reason} onChange={event => setReason(event.target.value)} /></div>
        </div>
        <p className="release-input-hint">Windows must begin within 30 days and last no longer than 24 hours. UTC is calculated from the browser's local time.</p>
        <button type="button" className="btn btn-primary"
          disabled={busy || !validRange || title.trim().length < 3 ||
            message.trim().length < 10 || reason.trim().length < 10}
          onClick={() => void save()}>{busy ? 'Saving…' : 'Schedule maintenance'}</button>
      </section>
      <aside className="release-editor" aria-label="Maintenance window history">
        <div className="release-panel-heading"><h3>Maintenance History</h3><span>Latest 30</span></div>
        {rows.length === 0 ? <p>No maintenance windows scheduled.</p> : rows.map(row =>
          <div className="release-preview-card" key={row.id}>
            <strong>{row.title}</strong>
            <p>{row.mode} · {row.isCancelled ? 'Cancelled' : 'Scheduled / time-limited'}</p>
            <small>{new Date(row.startUtc).toLocaleString()} — {new Date(row.endUtc).toLocaleString()}</small>
            {!row.isCancelled && <button type="button" className="btn btn-outline-danger btn-sm mt-2"
              disabled={busy || reason.trim().length < 10}
              onClick={() => void cancel(row.id)}>Cancel window</button>}
          </div>)}
      </aside>
    </div>
  </>
}

function AnnouncementsPreview() {
  const [rows, setRows] = useState<AnnouncementRow[]>([])
  const [level, setLevel] = useState<NoticeLevel>('Information')
  const [title, setTitle] = useState('Platform update')
  const [message, setMessage] = useState('We have an update to share.')
  const [reason, setReason] = useState('')
  const [starts, setStarts] = useState('')
  const [ends, setEnds] = useState('')
  const [error, setError] = useState('')
  const [notice, setNotice] = useState('')
  const [busy, setBusy] = useState(false)
  const [reload, setReload] = useState(0)
  useEffect(() => {
    let active = true
    api<AnnouncementRow[]>('/platform/announcements').then(data => {
      if (active) setRows(data)
    }).catch(cause => {
      if (active && !(cause instanceof SessionExpiredError))
        setError(cause instanceof Error ? cause.message : 'Unable to load announcements.')
    })
    return () => { active = false }
  }, [reload])
  const validRange = starts !== '' && ends !== '' && Date.parse(starts) < Date.parse(ends) &&
    Date.parse(ends) - Date.parse(starts) <= 24 * 60 * 60 * 1000
  async function publish() {
    if (!validRange || busy) return
    setBusy(true); setError(''); setNotice('')
    try {
      await post('/platform/announcements', {
        level, title: title.trim(), message: message.trim(), reason: reason.trim(),
        startUtc: utc(starts), endUtc: utc(ends)
      })
      setNotice('Announcement published with its scheduled window. Visitor banners show only while active.')
      setReason(''); setReload(x => x + 1)
    } catch (cause) {
      if (!(cause instanceof SessionExpiredError))
        setError(cause instanceof Error ? cause.message : 'Unable to publish the announcement.')
    } finally { setBusy(false) }
  }
  async function archive(id: number) {
    if (busy || reason.trim().length < 10) return
    setBusy(true); setError(''); setNotice('')
    try {
      await post(`/platform/announcements/${id}/archive`, { reason: reason.trim() })
      setNotice('Announcement archived and removed from the live status feed.')
      setReason(''); setReload(x => x + 1)
    } catch (cause) {
      if (!(cause instanceof SessionExpiredError))
        setError(cause instanceof Error ? cause.message : 'Unable to archive announcement.')
    } finally { setBusy(false) }
  }

  return <>
    <div className="alert alert-info" role="status">
      Real public in-app announcements, scheduled using UTC. This version is for
      <strong> all visitors</strong>; targeted audiences and email delivery are not yet enabled.
    </div>
    {error && <div role="alert" className="alert alert-danger">{error}</div>}
    {notice && <div role="status" className="alert alert-success">{notice}</div>}
    <div className="release-layout">
      <section className="release-editor release-editor-wide">
        <div className="release-panel-heading"><h3>Publish Public Notice</h3><span>Audited</span></div>
        <div className="release-form-grid">
          <div><label className="form-label" htmlFor="notice-severity">Severity</label>
            <select id="notice-severity" className="form-select" value={level}
              onChange={event => setLevel(event.target.value as NoticeLevel)}>
              <option>Information</option><option>Warning</option><option>Critical</option>
            </select></div>
          <div><label className="form-label" htmlFor="notice-start">Start (local time)</label>
            <input type="datetime-local" id="notice-start" className="form-control"
              value={starts} onChange={event => setStarts(event.target.value)} /></div>
          <div><label className="form-label" htmlFor="notice-end">End (local time)</label>
            <input type="datetime-local" id="notice-end" className="form-control"
              value={ends} onChange={event => setEnds(event.target.value)} /></div>
          <div className="release-field-wide"><label className="form-label" htmlFor="notice-title">Title</label>
            <input id="notice-title" className="form-control" maxLength={100}
              value={title} onChange={event => setTitle(event.target.value)} /></div>
          <div className="release-field-wide"><label className="form-label" htmlFor="notice-text">Message</label>
            <textarea id="notice-text" className="form-control" rows={3} maxLength={300}
              value={message} onChange={event => setMessage(event.target.value)} /></div>
          <div className="release-field-wide"><label className="form-label" htmlFor="notice-reason">Operator reason (required)</label>
            <textarea id="notice-reason" className="form-control" rows={2} maxLength={200}
              value={reason} onChange={event => setReason(event.target.value)} /></div>
        </div>
        <button type="button" className="btn btn-primary mt-2"
          disabled={busy || !validRange || title.trim().length < 3 ||
            message.trim().length < 10 || reason.trim().length < 10}
          onClick={() => void publish()}>{busy ? 'Publishing…' : 'Publish announcement'}</button>
      </section>
      <aside className="release-editor" aria-label="Announcement history">
        <div className="release-panel-heading"><h3>Announcements</h3><span>Latest 30</span></div>
        {rows.length === 0 ? <p>No published notices.</p> : rows.map(row =>
          <div className="release-preview-card" key={row.id}>
            <strong>{row.title}</strong><p>{row.message}</p>
            <small>{row.level} · {row.isArchived ? 'Archived' : 'Scheduled / published'} · {new Date(row.startUtc).toLocaleString()}</small>
            {!row.isArchived && <button type="button" className="btn btn-outline-secondary btn-sm mt-2"
              disabled={busy || reason.trim().length < 10}
              onClick={() => void archive(row.id)}>Archive</button>}
          </div>)}
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
