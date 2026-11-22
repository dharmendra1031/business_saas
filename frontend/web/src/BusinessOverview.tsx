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
  const cash = dashboard.cashSalesToday
  const money = new Intl.NumberFormat(undefined, {
    style: 'currency', currency: dashboard.business.currencyCode
  })
  const receiptDate = new Intl.DateTimeFormat(undefined, {
    dateStyle: 'medium', timeStyle: 'short', timeZone: dashboard.business.timeZoneId
  })
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
          {isOwner && cash && <>
            <div className="business-kpi-card"><small>Today's Cash Sales</small>
              <strong>{money.format(cash.total)}</strong>
              <span>Posted cash receipts only</span>
            </div>
            <div className="business-kpi-card"><small>Cash Bills Today</small>
              <strong>{cash.billCount.toLocaleString()}</strong>
              <span>Posted cash bills only</span>
            </div>
          </>}
          {isOwner && <div className="business-kpi-card"><small>Draft Purchase Orders</small>
            <strong>{pendingApprovals.toLocaleString()}</strong><span>Owner review</span></div>}
        </div>
      </section>

      <section className="content-section business-dashboard-panel" aria-labelledby="business-attention-title">
        <h2 id="business-attention-title">Attention Needed</h2>
        {isOwner && pendingApprovals > 0
          ? <button type="button" className="business-attention-action" onClick={onReviewApprovals}>
              <ClipboardCheck size={17} aria-hidden="true" />
              <span>Review {pendingApprovals} draft purchase {pendingApprovals === 1 ? 'order' : 'orders'}</span>
              <ArrowRight size={16} aria-hidden="true" />
            </button>
          : <p>{isOwner ? 'No draft purchase orders awaiting review.' : 'No assigned actions to show yet.'}</p>}
        {locations.length === 0 && <p role="status">No location assigned. Contact your business administrator.</p>}
      </section>

      {cash && <section className="content-section business-dashboard-panel" aria-labelledby="business-activity-title">
        <h2 id="business-activity-title">Recent Activity</h2>
        {cash.recentReceipts.length ? (
          <ul className="business-receipt-activity" aria-label="Recent recorded cash sales">
            {cash.recentReceipts.map(receipt => (
              <li key={receipt.id}>
                <span>Cash receipt #{receipt.id}
                  <small><time dateTime={receipt.postedAtUtc}>
                    {receiptDate.format(new Date(receipt.postedAtUtc))}
                  </time></small>
                </span>
                <strong>{money.format(receipt.total)}</strong>
              </li>
            ))}
          </ul>
        ) : <p>No recorded cash sales yet.</p>}
      </section>}
    </>}
    {locations.length === 0 && !hasProducts && <p className="business-location-warning" role="status">
      No location assigned. Ask your administrator to review access.
    </p>}
  </div>
}
