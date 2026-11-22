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
    accessStatus === 'Suspended' ? 'Suspended' :
    dashboard.canOperate ? 'Plan active' : 'Subscription inactive'

  return <div className="business-overview">
    <h1 className="visually-hidden">Business Dashboard</h1>
    <div className="business-dashboard-summary" aria-label="Subscription status">
      <span>{subscription?.planCode ?? 'No plan'} · {status}
        {subscription?.status === 'Trial' && <>
          {' · '}{accessStatus === 'TrialExpired' ? 'Ended' : 'Ends'}{' '}
          {new Date(subscription.trialEndsAtUtc).toLocaleDateString(undefined, {
            dateStyle: 'medium', timeZone: dashboard.business.timeZoneId
          })}
        </>}
      </span>
      {locations.length > 1 && <span>{locations.length} locations</span>}
    </div>

    {!hasProducts ? <section className="content-section business-setup-panel" aria-labelledby="business-setup-title">
      <h2 id="business-setup-title">Complete store setup</h2>
      <p>Prepare your product catalog before recording stock.</p>
      <ol className="business-setup-list">
        <li><span>1</span><button type="button" onClick={() => onNavigate('masters')}>
          Review categories &amp; units <ArrowRight size={15} /></button></li>
        <li><span>2</span><button type="button" onClick={() => onNavigate('products')}>
          Add your first product <ArrowRight size={15} /></button></li>
        <li><span>3</span><span>Record opening stock <small>After adding products</small></span></li>
        <li><span>4</span><span>Receipt settings <small>Not available yet</small></span></li>
      </ol>
    </section> : <>
      <section className="business-operational-section" aria-labelledby="business-overview-title">
        <h2 id="business-overview-title">Business Overview</h2>
        <div className="business-kpi-grid">
          {isOwner && <>
            <div className="business-kpi-card"><small>Today's Sales</small><strong>—</strong><span>Reporting not connected</span></div>
            <div className="business-kpi-card"><small>Total Bills</small><strong>—</strong><span>Reporting not connected</span></div>
            <div className="business-kpi-card"><small>Draft Purchase Orders</small><strong>{pendingApprovals.toLocaleString()}</strong><span>Owner review</span></div>
          </>}
          <div className="business-kpi-card"><small>Stock Alerts</small><strong>—</strong><span>Reorder limits not configured</span></div>
        </div>
      </section>

      <section className="content-section business-dashboard-panel" aria-labelledby="business-attention-title">
        <h2 id="business-attention-title">Attention Needed</h2>
        {isOwner && pendingApprovals > 0
          ? <button type="button" className="business-attention-action" onClick={onReviewApprovals}>
              <ClipboardCheck size={17} aria-hidden="true" />
              Review {pendingApprovals} draft purchase {pendingApprovals === 1 ? 'order' : 'orders'}
              <ArrowRight size={16} aria-hidden="true" />
            </button>
          : <p>{isOwner ? 'No draft purchase orders awaiting review.' : 'No assigned actions to show yet.'}</p>}
        {locations.length === 0 && <p role="status">No location assigned. Contact your business administrator.</p>}
      </section>

      <section className="content-section business-dashboard-panel" aria-labelledby="business-activity-title">
        <h2 id="business-activity-title">Recent Activity</h2>
        <p>Activity summary is not connected yet. Detailed records remain in their modules.</p>
      </section>
    </>}
    {locations.length === 0 && !hasProducts && <p className="business-location-warning" role="status">
      No location assigned. Ask your administrator to review access.
    </p>}
  </div>
}
