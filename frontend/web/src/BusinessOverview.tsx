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
  const stockAlerts = dashboard.stockAlerts
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
          {isOwner && stockAlerts && <div className="business-kpi-card">
            <small>Stock Alerts</small>
            <strong>{stockAlerts.count.toLocaleString()}</strong>
            <span>{stockAlerts.outOfStockCount > 0
              ? stockAlerts.outOfStockCount + ' out of stock · ' +
                (stockAlerts.count - stockAlerts.outOfStockCount) + ' low stock'
              : 'Posted stock records only'}</span>
          </div>}
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
          : !isOwner ? <p>No assigned actions to show yet.</p>
            : stockAlerts && stockAlerts.count === 0 ? <p>No alerts for monitored stock records.</p> : null}
        {isOwner && stockAlerts && stockAlerts.count > 0 && <div className="business-stock-alerts">
          <button type="button" className="business-setup-alert-link"
            onClick={() => onNavigate('stock')}>
            Review {stockAlerts.count} stock {stockAlerts.count === 1 ? 'alert' : 'alerts'} in Inventory
            <ArrowRight size={15} aria-hidden="true" />
          </button>
          <ul aria-label="Products needing stock attention">
            {stockAlerts.items.map(item => <li key={item.locationId + '-' + item.productId}>
              <span><strong>{item.productName}</strong>
                <small>{item.locationName} · {item.sku}</small></span>
              <span>{item.quantity === 0 ? 'Out of stock' : 'Low stock'}
                <small>{item.quantity} remaining · Alert at {item.reorderLevel}</small></span>
            </li>)}
          </ul>
          <small>Only configured products with recorded stock are monitored.</small>
        </div>}
        {isOwner && !stockAlerts && <button type="button" className="business-setup-alert-link"
          onClick={() => onNavigate('products')}>
          Configure reorder levels in Products to enable stock alerts
          <ArrowRight size={15} aria-hidden="true" />
        </button>}
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
