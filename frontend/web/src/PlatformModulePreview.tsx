import { useState } from 'react'
import { Bell, Building2, ShieldCheck, SlidersHorizontal } from 'lucide-react'
import PlatformAvailabilityPreview from './PlatformAvailabilityPreview'

const tabs = [
  { id: 'types', name: 'Business Types', Icon: Building2 },
  { id: 'modules', name: 'Modules & Access', Icon: ShieldCheck },
  { id: 'maintenance', name: 'Maintenance', Icon: SlidersHorizontal },
  { id: 'announcements', name: 'Announcements', Icon: Bell }
] as const

export default function PlatformModulePreview() {
  const [tab, setTab] = useState<(typeof tabs)[number]['id']>('types')
  return <div className="platform-preview-page">
    <div className="page-header"><h1>Platform Settings</h1></div>
    <div className="platform-preview-tabs" role="tablist" aria-label="Platform settings sections">
      {tabs.map(item => <button key={item.id} type="button" role="tab"
        aria-selected={tab === item.id} className={tab === item.id ? 'is-active' : ''}
        onClick={() => setTab(item.id)}>
        <item.Icon size={17} /> {item.name}
      </button>)}
    </div>
    <section className="content-section platform-preview-main" role="tabpanel">
      <PlatformAvailabilityPreview view={tab} />
    </section>
  </div>
}
