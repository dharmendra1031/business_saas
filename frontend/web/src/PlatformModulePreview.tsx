import { useState } from 'react'
import PlatformAvailabilityPreview from './PlatformAvailabilityPreview'
import {
  Bell, Building2, FileClock, Globe2, KeyRound, Languages,
  LockKeyhole, Mail, Settings2, ShieldCheck, SlidersHorizontal, Users
} from 'lucide-react'

type PlatformPreviewModule = 'licenses' | 'settings'

const settingTabs = [
  { id: 'general', name: 'General', Icon: Settings2 },
  { id: 'types', name: 'Business Types', Icon: Building2 },
  { id: 'modules', name: 'Modules & Access', Icon: ShieldCheck },
  { id: 'maintenance', name: 'Maintenance', Icon: SlidersHorizontal },
  { id: 'announcements', name: 'Announcements', Icon: Bell },
  { id: 'localization', name: 'Languages', Icon: Languages },
  { id: 'notifications', name: 'Notifications', Icon: Bell }
] as const

const licenseTabs = [
  { id: 'directory', name: 'License Directory', Icon: KeyRound },
  { id: 'issue', name: 'Issue & Activate', Icon: LockKeyhole },
  { id: 'lifecycle', name: 'Renew & Transfer', Icon: SlidersHorizontal },
  { id: 'history', name: 'Audit History', Icon: FileClock }
] as const

function PreviewNotice({ area }: { area: string }) {
  return <div className="platform-preview-notice" role="status">
    <LockKeyhole size={17} />
    <span><strong>Frontend design preview:</strong> {area} is not connected to a live write API.
      No settings, licenses or records are saved from this screen.</span>
  </div>
}

function PreviewField({ label, value, hint }: { label: string; value: string; hint?: string }) {
  return <div>
    <label className="form-label">{label}</label>
    <div className="platform-preview-field" aria-label={`${label}: ${value}`}>{value}</div>
    {hint && <p className="field-help">{hint}</p>}
  </div>
}

function PlatformSettingsPreview() {
  const [tab, setTab] = useState<(typeof settingTabs)[number]['id']>('general')
  return <div className="platform-preview-page">
    <div className="page-header">
      <div><span className="platform-kicker">PLATFORM CONFIGURATION</span>
        <h1>Platform Settings</h1>
        <p>Global settings and module policies are separate from every business's operational settings.</p></div>
    </div>
    {(tab === 'general' || tab === 'localization' || tab === 'notifications') &&
      <PreviewNotice area="Selected platform settings" />}
    <div className="platform-preview-tabs" role="tablist" aria-label="Platform settings sections">
      {settingTabs.map(item => <button key={item.id} type="button" role="tab"
        aria-selected={tab === item.id} className={tab === item.id ? 'is-active' : ''}
        onClick={() => setTab(item.id)}>
        <item.Icon size={17} /> {item.name}
      </button>)}
    </div>
    <div className={`platform-preview-columns${tab === 'types' || tab === 'modules' || tab === 'maintenance' || tab === 'announcements' ? ' is-release-management' : ''}`}>
      <section className="content-section platform-preview-main" role="tabpanel">
        {tab === 'general' && <>
          <div className="platform-preview-panel-title">
            <span className="platform-preview-panel-icon"><Settings2 size={20} /></span>
            <div><h2>General Configuration</h2><p>Platform-wide identity and regional defaults</p></div>
          </div>
          <div className="platform-preview-field-grid">
            <PreviewField label="Platform name" value="Business SaaS"
              hint="The existing app identity is shown. Editing requires a verified settings endpoint." />
            <PreviewField label="Default language" value="English"
              hint="Published language packs will determine available options." />
            <PreviewField label="Default market" value="Not configured"
              hint="Country, tax and legal defaults require a reviewed market policy." />
            <PreviewField label="Timezone" value="Not configured"
              hint="Each business retains its own authoritative timezone." />
          </div>
        </>}
        {(tab === 'types' || tab === 'modules' || tab === 'maintenance' || tab === 'announcements') &&
          <PlatformAvailabilityPreview view={tab} />}
        {tab === 'localization' && <>
          <div className="platform-preview-panel-title">
            <span className="platform-preview-panel-icon"><Globe2 size={20} /></span>
            <div><h2>Languages & Localization</h2><p>Translate interface strings; preserve currency and business timezone</p></div>
          </div>
          <div className="platform-preview-feature-list">
            <div><span><strong>Published languages</strong><small>Live language catalogue not connected to this design preview</small></span></div>
            <div><span><strong>Translation validation</strong><small>Preview text, required keys, formatting and RTL behavior before publishing</small></span></div>
            <div><span><strong>Change control</strong><small>Publish audited versions; rollback must be tested</small></span></div>
          </div>
        </>}
        {tab === 'notifications' && <>
          <div className="platform-preview-panel-title">
            <span className="platform-preview-panel-icon"><Mail size={20} /></span>
            <div><h2>Notifications</h2><p>Approved system communications, never fictional delivery status</p></div>
          </div>
          <div className="platform-preview-feature-list">
            <div><span><strong>Owner invitation</strong><small>Signed, expiring invitations and verified email delivery</small></span>
              <span className="platform-preview-small-tag">Pending</span></div>
            <div><span><strong>Subscription reminders</strong><small>Only after real subscription dates and dispatch integration exist</small></span>
              <span className="platform-preview-small-tag">Pending</span></div>
            <div><span><strong>Platform announcements</strong><small>Auditable targeted messages; no sample recipients</small></span>
              <span className="platform-preview-small-tag">Pending</span></div>
          </div>
        </>}
        {(tab === 'general' || tab === 'localization' || tab === 'notifications') &&
          <p className="platform-preview-footnote">This section remains a design preview; no live write API is connected.</p>}
      </section>
      <aside className="content-section platform-preview-side">
        <span className="platform-kicker">PLATFORM SCOPE</span>
        <h2>Keep controls separate</h2>
        <p>Platform configuration applies only to authorized global administration.
          Store prices, tax, stock, POS transactions and receipt settings stay inside their business.</p>
        <div className="platform-preview-side-row"><ShieldCheck size={17} /> Platform permission required</div>
        <div className="platform-preview-side-row"><Users size={17} /> Tenant roles remain isolated</div>
        <div className="platform-preview-side-row"><FileClock size={17} /> Sensitive updates require audit</div>
      </aside>
    </div>
  </div>
}

function PlatformLicensesPreview() {
  const [tab, setTab] = useState<(typeof licenseTabs)[number]['id']>('directory')
  return <div className="platform-preview-page">
    <div className="page-header">
      <div><span className="platform-kicker">ENTERPRISE OPERATIONS</span><h1>Licenses</h1>
        <p>Windows offline and on-premise licenses. Hosted SaaS plans remain under Plans & Billing.</p></div>
    </div>
    <PreviewNotice area="License management" />
    <div className="platform-preview-tabs" role="tablist" aria-label="License management sections">
      {licenseTabs.map(item => <button key={item.id} type="button" role="tab"
        aria-selected={tab === item.id} className={tab === item.id ? 'is-active' : ''}
        onClick={() => setTab(item.id)}>
        <item.Icon size={17} /> {item.name}
      </button>)}
    </div>
    <section className="content-section platform-preview-main" role="tabpanel">
      {tab === 'directory' && <>
        <div className="platform-preview-panel-title">
          <span className="platform-preview-panel-icon"><KeyRound size={20} /></span>
          <div><h2>License Directory</h2><p>Signed issued license records will appear here after integration</p></div>
        </div>
        <div className="table-responsive"><table className="table table-sm platform-table">
          <thead><tr><th scope="col">License</th><th scope="col">Customer / Business</th>
            <th scope="col">Edition</th><th scope="col">Expiry</th><th scope="col">Status</th></tr></thead>
          <tbody><tr><td colSpan={5}><div className="platform-preview-empty">
            <KeyRound size={28} /><strong>No license records connected</strong>
            <p>Once issuance and authorization APIs are available, real licenses and statuses will appear here.</p>
          </div></td></tr></tbody>
        </table></div>
      </>}
      {tab === 'issue' && <>
        <div className="platform-preview-panel-title">
          <span className="platform-preview-panel-icon"><LockKeyhole size={20} /></span>
          <div><h2>Issue & Activate</h2><p>Reserved layout for verified license issuance</p></div>
        </div>
        <div className="platform-preview-field-grid">
          <PreviewField label="Contracted business" value="Select a verified enterprise customer" />
          <PreviewField label="Edition" value="Requires approved enterprise contract" />
          <PreviewField label="Activation request" value="Signed import / device validation pending" />
          <PreviewField label="License duration" value="Contract and expiry policy pending" />
        </div>
        <p className="platform-preview-footnote">License signing, import and activation actions
          remain unavailable. The platform private signing key must never be exposed in the browser.</p>
      </>}
      {tab === 'lifecycle' && <>
        <div className="platform-preview-panel-title"><span className="platform-preview-panel-icon">
          <SlidersHorizontal size={20} /></span><div><h2>Renew, Transfer & Revoke</h2>
          <p>Future audited license lifecycle actions</p></div></div>
        <div className="platform-preview-feature-list">
          <div><span><strong>Renew</strong><small>Issue an approved versioned extension</small></span></div>
          <div><span><strong>Transfer / Reissue</strong><small>Verify the replacement device and contractual scope</small></span></div>
          <div><span><strong>Revoke</strong><small>Explicit reason, historical audit and offline behavior policy</small></span></div>
        </div>
      </>}
      {tab === 'history' && <>
        <div className="platform-preview-panel-title"><span className="platform-preview-panel-icon">
          <FileClock size={20} /></span><div><h2>License Audit History</h2>
          <p>Issued, activated, renewed and revoked events</p></div></div>
        <div className="platform-preview-empty"><FileClock size={28} />
          <strong>No license audit API connected</strong>
          <p>History will use authenticated, tamper-evident records, not demo activity.</p></div>
      </>}
    </section>
  </div>
}

export default function PlatformModulePreview({ module }: { module: PlatformPreviewModule }) {
  return module === 'settings' ? <PlatformSettingsPreview /> : <PlatformLicensesPreview />
}
