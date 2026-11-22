import { ArrowRight, Boxes, ClipboardCheck, CreditCard, Package, ShoppingCart, Store, Tags, Truck, Users } from 'lucide-react'
import type { RetailDashboard } from './api'

type BusinessAction = 'products' | 'purchases' | 'stock' | 'masters' | 'suppliers' |
  'customers' | 'team' | 'cash'

export default function BusinessOverview({ dashboard, onNavigate, onReviewApprovals }: {
  dashboard: RetailDashboard
  onNavigate: (page: BusinessAction) => void
  onReviewApprovals: () => void
}) {
  const { business, locations, roleCode, subscription, accessStatus, canOperate, hasProducts, pendingApprovals } = dashboard
  const isOwner = roleCode === 'Owner'
  const actions = [
    { key: 'products', label: 'Products', Icon: Package },
    { key: 'purchases', label: 'Purchases', Icon: ShoppingCart },
    { key: 'stock', label: 'Inventory', Icon: Boxes },
    { key: 'masters', label: 'Categories & Units', Icon: Tags },
    { key: 'suppliers', label: 'Suppliers', Icon: Truck },
    { key: 'customers', label: 'Customers', Icon: Users }
  ] as const
  const status = accessStatus === 'Trial' ? 'Trial active'
    : accessStatus === 'TrialExpired' ? 'Trial expired'
      : canOperate ? 'Plan active' : 'Subscription inactive'
  const trialEnd = subscription?.status === 'Trial' &&
    new Date(subscription.trialEndsAtUtc).toLocaleDateString(undefined, {
      dateStyle: 'medium', timeZone: business.timeZoneId
    })
  const paidEnd = subscription?.paidEndsAtUtc &&
    new Date(subscription.paidEndsAtUtc).toLocaleDateString(undefined, {
      dateStyle: 'medium', timeZone: business.timeZoneId
    })

  return <div className="business-overview">
    <div className="business-dashboard-summary" aria-label="Verified business status">
      <span><CreditCard size={16} aria-hidden="true" />
        <strong>{subscription?.planCode ?? 'No plan'}</strong> · {status}
        {trialEnd && ` · ${accessStatus === 'TrialExpired' ? 'Ended' : 'Ends'} ${trialEnd}`}
        {paidEnd && ` · Paid through ${paidEnd}`}
      </span>
      <span><Package size={16} aria-hidden="true" /> {hasProducts ? 'Products added' : 'No products yet'}</span>
      <span><Store size={16} aria-hidden="true" /> {locations.length} {locations.length === 1 ? 'location' : 'locations'}</span>
    </div>

    {isOwner && pendingApprovals > 0 && <button type="button" className="business-approval-cta"
      onClick={onReviewApprovals}>
      <ClipboardCheck size={17} /> Review {pendingApprovals} draft purchase {pendingApprovals === 1 ? 'order' : 'orders'}
      <ArrowRight size={16} />
    </button>}

    <section className="content-section business-actions-panel" aria-labelledby="business-actions-title">
      <div className="business-dashboard-toolbar">
        <h2 id="business-actions-title">Quick access</h2>
        {dashboard.cashSaleEnabled && <button type="button" className="btn btn-primary btn-sm"
          disabled={!canOperate} onClick={() => onNavigate('cash')}>
          <ShoppingCart size={16} /> Open POS
        </button>}
      </div>
      <div className="business-action-grid">
        {actions.map(({ key, label, Icon }) => <button type="button" key={key}
          className="business-action-card" onClick={() => onNavigate(key)}>
          <span className="business-action-icon"><Icon size={19} aria-hidden="true" /></span>
          <strong>{label}</strong><ArrowRight size={16} className="business-action-arrow" aria-hidden="true" />
        </button>)}
      </div>
      {isOwner && <button type="button" className="business-team-link" onClick={() => onNavigate('team')}>
        <Users size={16} aria-hidden="true" /> Staff & Access <ArrowRight size={15} aria-hidden="true" />
      </button>}
    </section>

    {locations.length === 0 && <p className="business-location-warning" role="status">
      No location assigned. Ask your administrator to review access.
    </p>}
    {locations.length > 1 && <details className="business-location-details">
      <summary>View assigned locations ({locations.length})</summary>
      <ul>{locations.map(location => <li key={location.id}>{location.name} · {location.code}</li>)}</ul>
    </details>}
  </div>
}
