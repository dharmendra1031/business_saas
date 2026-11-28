import { useState } from 'react'
import {
  CreditCard, FileText, ReceiptText, RotateCcw, Store
} from 'lucide-react'
import type { RetailDashboard } from './api'
import BusinessPostedSales from './BusinessPostedSales'
import BusinessReturns from './BusinessReturns'
import BusinessDailySales from './BusinessDailySales'
import BusinessOutstanding from './BusinessOutstanding'
import BusinessRegisterClosing from './BusinessRegisterClosing'
import BusinessBillingSettings from './BusinessBillingSettings'
import BusinessReceiptSettings from './BusinessReceiptSettings'
import SubscriptionCheckout from './SubscriptionCheckout'

export type BusinessPreviewModule = 'sales' | 'reports' | 'settings'

const modules = {
  sales: {
    title: 'Sales & Returns',
    tabs: [
      { name: 'Invoices', Icon: ReceiptText, title: 'Posted Sales', description: 'Posted sales receipts and invoices.' },
      { name: 'Returns', Icon: RotateCcw, title: 'Returns & Refunds', description: 'Original sale quantities and eligible refunds.' }
    ]
  },
  reports: {
    title: 'Reports',
    tabs: [
      { name: 'Sales', Icon: ReceiptText, title: 'Daily Sales Report', description: 'Sales, returns and reconciled payments.' },
      { name: 'Outstanding', Icon: CreditCard, title: 'Customer Outstanding', description: 'Unpaid credit invoices and overdue aging by business date.' },
      { name: 'Day Closing', Icon: ReceiptText, title: 'Cash Register Closing', description: 'Review and close individual cash shifts.' }
    ]
  },
  settings: {
    title: 'Business Settings',
    tabs: [
      { name: 'General', Icon: Store, title: 'Business Identity', description: 'Registered business and market information.' },
      { name: 'Invoice & Print', Icon: FileText, title: 'Invoice & Print', description: '80mm/A4 templates and visible receipt fields.' },
      { name: 'Billing & Payments', Icon: CreditCard, title: 'Billing & Payments', description: 'Choose which released payment methods are available for your store.' },
      { name: 'Subscription & billing', Icon: CreditCard, title: 'Subscription & billing', description: 'Manage your business subscription.' }
    ]
  }
} as const

export default function BusinessPreview({ module, dashboard, userId, onBillingUpdated }: {
  module: BusinessPreviewModule
  dashboard: RetailDashboard
  userId: number
  onBillingUpdated?: () => void
}) {
  const item = modules[module]
  const [tab, setTab] = useState(0)
  const selected = item.tabs[tab]
  const generalSettings = module === 'settings' && tab === 0
  const subscriptionSettings = module === 'settings' && tab === 3 && dashboard.roleCode === 'Owner'
  const billingSettings = module === 'settings' && tab === 2 && dashboard.roleCode === 'Owner'
  const receiptSettings = module === 'settings' && tab === 1 && dashboard.roleCode === 'Owner'
  const postedSales = module === 'sales' && tab === 0 && dashboard.roleCode === 'Owner'
  const postedReturns = module === 'sales' && tab === 1 && dashboard.roleCode === 'Owner'
  const dailySales = module === 'reports' && tab === 0 && dashboard.roleCode === 'Owner'
  const outstanding = module === 'reports' && tab === 1 && dashboard.roleCode === 'Owner'

  return <div className="business-preview">
    <div className="business-preview-heading">
      <h2>{item.title}</h2>
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
      {!subscriptionSettings && <h3>{selected.title}</h3>}
      {!subscriptionSettings && !postedSales && !postedReturns && <p className="business-preview-description">{dailySales ? 'Gross cash sales, refunds and net cash sales.' : selected.description}</p>}
      {subscriptionSettings ? <SubscriptionCheckout key={dashboard.business.id} businessId={dashboard.business.id} /> :
        postedSales ? <BusinessPostedSales dashboard={dashboard} userId={userId} /> :
        postedReturns ? <BusinessReturns dashboard={dashboard} /> :
        dailySales ? <BusinessDailySales dashboard={dashboard} /> :
        module === 'reports' && tab === 2 && dashboard.roleCode === 'Owner' ? <BusinessRegisterClosing key={dashboard.business.id + ':' + userId} dashboard={dashboard} /> :
        outstanding ? <BusinessOutstanding businessId={dashboard.business.id} /> : receiptSettings ?
          <BusinessReceiptSettings businessId={dashboard.business.id} canOperate={dashboard.canOperate}
            onSaved={() => onBillingUpdated?.()} /> :
          billingSettings ? <BusinessBillingSettings
        businessId={dashboard.business.id} canOperate={dashboard.canOperate}
        onSaved={() => onBillingUpdated?.()} /> : generalSettings ? <dl className="business-preview-identity">
        <div><dt>Business name</dt><dd>{dashboard.business.name}</dd></div>
        <div><dt>Business type</dt><dd>{dashboard.business.typeName}</dd></div>
        <div><dt>Country</dt><dd>{dashboard.business.countryCode}</dd></div>
        <div><dt>Currency</dt><dd>{dashboard.business.currencyCode}</dd></div>
        <div><dt>Timezone</dt><dd>{dashboard.business.timeZoneId}</dd></div>
        <div><dt>Authorized locations</dt><dd>{dashboard.locations.length.toLocaleString()}</dd></div>
      </dl> : null}
    </section>
  </div>
}
