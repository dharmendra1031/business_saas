import { useState } from 'react'
import {
  BadgeCheck, Banknote, BarChart3, Building2, ClipboardList, CreditCard, FileText,
  Globe2, History, LockKeyhole, ReceiptText, RotateCcw, Settings2, ShieldCheck,
  Store, Wallet
} from 'lucide-react'
import type { RetailDashboard } from './api'

export type BusinessPreviewModule = 'sales' | 'register' | 'reports' | 'settings'

const modules = {
  sales: {
    kicker: 'RETAIL SALES',
    title: 'Sales & Returns',
    subtitle: 'Review posted receipts, returns and exchanges in this business.',
    Icon: ReceiptText,
    tabs: [
      { name: 'Invoices', Icon: ReceiptText, title: 'Posted Sales', description: 'Locate immutable posted invoices, line details and receipts.' },
      { name: 'Returns', Icon: RotateCcw, title: 'Returns & Refunds', description: 'Verify original sale quantities, eligibility and processed refunds.' },
      { name: 'Exchanges', Icon: History, title: 'Exchanges', description: 'Track a linked return and replacement sale without rewriting history.' }
    ]
  },
  register: {
    kicker: 'CASH OPERATIONS',
    title: 'Cash Register',
    subtitle: 'Track location sessions, counted cash and safe day-closing workflows.',
    Icon: Wallet,
    tabs: [
      { name: 'Sessions', Icon: Wallet, title: 'Register Sessions', description: 'Opening float, assigned shift and location-specific session history.' },
      { name: 'Day Closing', Icon: ReceiptText, title: 'Day Closing', description: 'Cash counted versus expected, discrepancy approval and close confirmation.' },
      { name: 'Cash Movements', Icon: Banknote, title: 'Cash Movements', description: 'View posted cash in/out history with actor, reason and reconciliation.' }
    ]
  },
  reports: {
    kicker: 'BUSINESS INSIGHTS',
    title: 'Reports',
    subtitle: 'Verified operational reports scoped to the current business and authorized locations.',
    Icon: BarChart3,
    tabs: [
      { name: 'Sales', Icon: ReceiptText, title: 'Daily Sales Report', description: 'Gross sales, discounts, taxes, returns and payment reconciliation.' },
      { name: 'Inventory', Icon: ClipboardList, title: 'Inventory Report', description: 'Location-wise available stock and actual stock movements.' },
      { name: 'Purchasing', Icon: Building2, title: 'Purchase Report', description: 'Ordered, received and outstanding purchase quantities.' },
      { name: 'Cash', Icon: Wallet, title: 'Register Report', description: 'Real cash movement totals and approved closing variance by shift.' }
    ]
  },
  settings: {
    kicker: 'BUSINESS CONFIGURATION',
    title: 'Business Settings',
    subtitle: 'Business identity, receipts and market rules belong to this tenant, not the platform.',
    Icon: Settings2,
    tabs: [
      { name: 'General', Icon: Store, title: 'Business Identity', description: 'Review the business identity and the provisioned market.' },
      { name: 'Receipts', Icon: FileText, title: 'Receipt Preferences', description: 'Plan receipt details, 80 mm printing and store identity controls.' },
      { name: 'Tax & Payments', Icon: CreditCard, title: 'Tax & Payment Policy', description: 'Market-specific tax rules and verified tender integrations only.' },
      { name: 'Language', Icon: Globe2, title: 'Language & Locale', description: 'Approved locale pack, currency display, timezone and RTL behavior.' }
    ]
  }
} as const

export default function BusinessPreview({ module, dashboard }: {
  module: BusinessPreviewModule
  dashboard: RetailDashboard
}) {
  const item = modules[module]
  const [tab, setTab] = useState<number>(0)
  const selected = item.tabs[tab]
  const Icon = item.Icon
  const SelectedIcon = selected.Icon
  const isGeneral = module === 'settings' && tab === 0

  return <div className="business-preview">
    <section className="business-preview-intro" aria-label={item.title}>
      <span className="business-preview-symbol"><Icon size={26} /></span>
      <div><span className="business-eyebrow">{item.kicker}</span>
        <h2>{item.title}</h2>
        <p>{item.subtitle}</p></div>
    </section>

    <div className="business-preview-disclosure" role="status">
      <LockKeyhole size={17} />
      <span><strong>Frontend design preview</strong> — this module is not connected to a reporting or write API.
        There are no simulated totals, transactions, saves, payments or receipts.</span>
    </div>

    <div className="business-preview-tabs" role="tablist" aria-label={`${item.title} sections`}>
      {item.tabs.map((entry, index) => {
        const TabIcon = entry.Icon
        return <button type="button" role="tab" aria-selected={index === tab}
          className={index === tab ? 'is-active' : ''} key={entry.name}
          onClick={() => setTab(index)}>
          <TabIcon size={17} /> {entry.name}
        </button>
      })}
    </div>

    <div className="business-preview-layout">
      <section className="content-section business-preview-content" role="tabpanel">
        <div className="business-preview-panel-heading">
          <span><SelectedIcon size={22} /></span>
          <div><h3>{selected.title}</h3><p>{selected.description}</p></div>
        </div>

        {isGeneral ? <dl className="business-preview-identity">
          <div><dt>Business name</dt><dd>{dashboard.business.name}</dd></div>
          <div><dt>Business type</dt><dd>{dashboard.business.typeName}</dd></div>
          <div><dt>Country</dt><dd>{dashboard.business.countryCode}</dd></div>
          <div><dt>Currency</dt><dd>{dashboard.business.currencyCode}</dd></div>
          <div><dt>Timezone</dt><dd>{dashboard.business.timeZoneId}</dd></div>
          <div><dt>Authorized locations</dt><dd>{dashboard.locations.length.toLocaleString()}</dd></div>
        </dl> : <div className="business-preview-empty">
          <span><FileText size={29} /></span>
          <h4>{module === 'reports' ? 'Report data not connected' :
            module === 'sales' ? 'Sales history API not connected' :
              module === 'register' ? 'Register ledger not connected' : 'Configuration not connected'}</h4>
          <p>The layout for this workflow is ready for review. No results can be displayed until a real
            tenant-secured API and necessary permissions are integrated.</p>
        </div>}

        <div className="business-preview-footer"><BadgeCheck size={16} />
          {isGeneral ? 'Identity values are read from the current authorized business.' :
            'This is a visual flow only. No business data was created or changed.'}
        </div>
      </section>
      <aside className="content-section business-preview-guidance">
        <span className="business-eyebrow">IMPLEMENTATION GUARDRAILS</span>
        <h3>Business-safe by design</h3>
        <p>Future actions must enforce business ID, location assignment, role, subscription access,
          audit history and safe retries before they become available.</p>
        <div><ShieldCheck size={17} /> Scoped tenant permissions</div>
        <div><LockKeyhole size={17} /> No unverified transactions</div>
        <div><BarChart3 size={17} /> No fabricated business totals</div>
      </aside>
    </div>
  </div>
}
