import { ArrowRight, ClipboardCheck } from 'lucide-react'
import type { RetailDashboard } from './api'

type SetupAction = 'masters' | 'products' | 'stock'

export default function BusinessOverview({ dashboard, onNavigate, onReviewApprovals }: {
  dashboard: RetailDashboard
  onNavigate: (page: SetupAction) => void
  onReviewApprovals: () => void
}) {
  const { roleCode, locations, subscription, accessStatus, hasProducts, pendingApprovals } = dashboard
  const isOwner = roleCode === 'Owner'
  const status = accessStatus === 'Trial' ? 'Trial active' :
    accessStatus === 'TrialExpired' ? 'Trial expired' :
    dashboard.canOperate ? 'Plan active' : 'Subscription inactive'

  return <div className="business-overview">
    <h1 className="visually-hidden">Business Dashboard</h1>
    <div className="business-dashboard-summary" aria-label="Business status">
      <span>{subscription?.planCode ?? 'No plan'} · {status}
        {subscription?.status === 'Trial' && <>
          {' · '}{accessStatus === 'TrialExpired' ? 'Ended' : 'Ends'}{' '}
          {new Date(subscription.trialEndsAtUtc).toLocaleDateString(undefined, {
            dateStyle: 'medium', timeZone: dashboard.business.timeZoneId
          })}
        </>}
      </span>
      <span>Locations: {locations.length}</span>
      <span>Products: {hasProducts ? 'Added' : 'Not started'}</span>
    </div>
    {isOwner && pendingApprovals > 0 && <button type="button" className="business-approval-cta"
      onClick={onReviewApprovals}>
      <ClipboardCheck size={17} aria-hidden="true" />
      Review {pendingApprovals} draft purchase {pendingApprovals === 1 ? 'order' : 'orders'}
      <ArrowRight size={16} aria-hidden="true" />
    </button>}
    {!hasProducts && <section className="content-section business-onboarding" aria-labelledby="business-onboarding-heading">
      <h2 id="business-onboarding-heading">Set up your store</h2>
      <ol className="business-onboarding-steps">
        <li><span>1</span><button type="button" onClick={() => onNavigate('masters')}>Categories &amp; units <ArrowRight size={15} /></button></li>
        <li><span>2</span><button type="button" onClick={() => onNavigate('products')}>Add products <ArrowRight size={15} /></button></li>
        <li><span>3</span><button type="button" onClick={() => onNavigate('stock')}>Review stock <ArrowRight size={15} /></button></li>
      </ol>
    </section>}
    {locations.length === 0 && <p className="business-location-warning" role="status">
      No location assigned. Ask your administrator to review access.
    </p>}
    {locations.length > 1 && <details className="business-location-details">
      <summary>View assigned locations ({locations.length})</summary>
      <ul>{locations.map(location => <li key={location.id}>{location.name} · {location.code}</li>)}</ul>
    </details>}
  </div>
}
