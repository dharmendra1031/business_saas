import { useState } from 'react'
import {
  Banknote, Building2, ClipboardList, CreditCard, FileText, Globe2, History,
  ReceiptText, RotateCcw, Store, Wallet
} from 'lucide-react'
import type { RetailDashboard } from './api'
import BusinessPostedSales from './BusinessPostedSales'
import BusinessDailySales from './BusinessDailySales'

export type BusinessPreviewModule = 'sales' | 'register' | 'reports' | 'settings'

const modules = {
  sales: {
    title: 'Sales & Returns',
    tabs: [
      { name: 'Invoices', Icon: ReceiptText, title: 'Posted Sales', description: 'Posted sales receipts and invoices.' },
      { name: 'Returns', Icon: RotateCcw, title: 'Returns & Refunds', description: 'Original sale quantities and eligible refunds.' },
      { name: 'Exchanges', Icon: History, title: 'Exchanges', description: 'Linked return and replacement sales.' }
    ]
  },
  register: {
    title: 'Cash Register',
    tabs: [
      { name: 'Sessions', Icon: Wallet, title: 'Register Sessions', description: 'Opening cash and cashier shifts.' },
      { name: 'Day Closing', Icon: ReceiptText, title: 'Day Closing', description: 'Expected and counted cash with discrepancies.' },
      { name: 'Cash Movements', Icon: Banknote, title: 'Cash Movements', description: 'Recorded cash in and cash out.' }
    ]
  },
  reports: {
    title: 'Reports',
    tabs: [
      { name: 'Sales', Icon: ReceiptText, title: 'Daily Sales Report', description: 'Sales, returns and reconciled payments.' },
      { name: 'Inventory', Icon: ClipboardList, title: 'Inventory Report', description: 'Stock and actual movements by location.' },
      { name: 'Purchasing', Icon: Building2, title: 'Purchase Report', description: 'Ordered and received purchase quantities.' },
      { name: 'Cash', Icon: Wallet, title: 'Register Report', description: 'Shift cash totals and closing differences.' }
    ]
  },
  settings: {
    title: 'Business Settings',
    tabs: [
      { name: 'General', Icon: Store, title: 'Business Identity', description: 'Registered business and market information.' },
      { name: 'Receipts', Icon: FileText, title: 'Receipt Preferences', description: 'Store information and receipt printing preferences.' },
      { name: 'Tax & Payments', Icon: CreditCard, title: 'Tax & Payments', description: 'Tax rules and verified tender integrations.' },
      { name: 'Language', Icon: Globe2, title: 'Language & Locale', description: 'Approved locale and language settings.' }
    ]
  }
} as const

export default function BusinessPreview({ module, dashboard }: {
  module: BusinessPreviewModule
  dashboard: RetailDashboard
}) {
  const item = modules[module]
  const [tab, setTab] = useState(0)
  const selected = item.tabs[tab]
  const generalSettings = module === 'settings' && tab === 0
  const postedSales = module === 'sales' && tab === 0 && dashboard.roleCode === 'Owner'
  const dailySales = module === 'reports' && tab === 0 && dashboard.roleCode === 'Owner'

  return <div className="business-preview">
    <div className="business-preview-heading">
      <h2>{item.title}</h2>
      {!postedSales && !dailySales && <span>Under construction</span>}
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

    <section className="content-section business-preview-content" role="tabpanel">
      <h3>{selected.title}</h3>
      <p className="business-preview-description">{dailySales ? 'Totals from posted cash receipts.' : selected.description}</p>
      {postedSales ? <BusinessPostedSales dashboard={dashboard} /> :
        dailySales ? <BusinessDailySales dashboard={dashboard} /> : generalSettings ? <dl className="business-preview-identity">
        <div><dt>Business name</dt><dd>{dashboard.business.name}</dd></div>
        <div><dt>Business type</dt><dd>{dashboard.business.typeName}</dd></div>
        <div><dt>Country</dt><dd>{dashboard.business.countryCode}</dd></div>
        <div><dt>Currency</dt><dd>{dashboard.business.currencyCode}</dd></div>
        <div><dt>Timezone</dt><dd>{dashboard.business.timeZoneId}</dd></div>
        <div><dt>Authorized locations</dt><dd>{dashboard.locations.length.toLocaleString()}</dd></div>
      </dl> : <p className="business-preview-unavailable" role="status">
        This feature is not available yet. No records or transactions are simulated.
      </p>}
    </section>
  </div>
}
