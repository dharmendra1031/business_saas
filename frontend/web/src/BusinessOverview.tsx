import {
  ArrowRight, Boxes, Building2, CheckCircle2, ClipboardCheck, Clock3,
  CreditCard, Layers3, Package, ShoppingCart, Store, Tags, Truck, Users
} from 'lucide-react'
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
    { key: 'products', label: 'Products', description: 'Manage your retail catalog and prices', Icon: Package },
    { key: 'purchases', label: 'Purchases', description: 'Purchase orders and goods receiving', Icon: ShoppingCart },
    { key: 'stock', label: 'Inventory', description: 'Track stock by store location', Icon: Boxes },
    { key: 'masters', label: 'Categories & Units', description: 'Set up product classifications', Icon: Tags },
    { key: 'suppliers', label: 'Suppliers', description: 'Manage purchasing contacts', Icon: Truck },
    { key: 'customers', label: 'Customers', description: 'Manage store customer records', Icon: Users }
  ] as const

  const statusLabel = canOperate ? accessStatus === 'Trial' ? 'Active trial' : 'Subscription active'
    : accessStatus === 'TrialExpired' ? 'Trial expired' : 'Subscription inactive'

  return <div className="business-overview">
    <section className="business-welcome" aria-labelledby="business-overview-heading">
      <div className="business-welcome-copy">
        <span className="business-eyebrow">RETAIL MANAGEMENT WORKSPACE</span>
        <h2 id="business-overview-heading">Welcome to {business.name}</h2>
        <p>Your business operations in one place. Choose a module to continue with products,
          purchasing, inventory or your team.</p>
        <div className="business-welcome-meta">
          <span><Store size={15} /> {locations.length} {locations.length === 1 ? 'location' : 'locations'}</span>
          <span><ShieldRoleIcon /> {isOwner ? 'Business Owner' : 'Store Manager'}</span>
          <span className={canOperate ? 'is-ready' : 'is-restricted'}>
            {canOperate ? <CheckCircle2 size={15} /> : <Clock3 size={15} />} {statusLabel}
          </span>
        </div>
      </div>
      {dashboard.cashSaleEnabled && <button type="button" className="btn business-welcome-pos"
        onClick={() => onNavigate('cash')} disabled={!canOperate}>
        <ShoppingCart size={18} /> Open POS Checkout <ArrowRight size={17} />
      </button>}
    </section>

    <div className="business-dashboard-facts" aria-label="Verified business summary">
      <section className="business-fact-card">
        <span className="business-fact-icon"><Building2 size={22} /></span>
        <div><small>Store locations</small><strong>{locations.length.toLocaleString()}</strong>
          <span>Assigned to your workspace</span></div>
      </section>
      <section className="business-fact-card">
        <span className="business-fact-icon is-catalog"><Package size={22} /></span>
        <div><small>Product catalog</small>
          <strong className="business-fact-word">{hasProducts ? 'Configured' : 'Not started'}</strong>
          <span>Based on existing product records</span></div>
      </section>
      <section className="business-fact-card">
        <span className="business-fact-icon is-approval"><ClipboardCheck size={22} /></span>
        <div><small>Draft purchase orders</small>
          <strong>{isOwner ? pendingApprovals.toLocaleString() : '—'}</strong>
          <span>{isOwner ? 'Pending Owner review' : 'Owner approvals only'}</span></div>
      </section>
      <section className="business-fact-card">
        <span className="business-fact-icon is-plan"><CreditCard size={22} /></span>
        <div><small>Subscription plan</small>
          <strong className="business-fact-word">{subscription?.planCode ?? 'Not assigned'}</strong>
          <span>{statusLabel}</span></div>
      </section>
    </div>

    <div className="business-dashboard-columns">
      <section className="content-section business-actions-panel" aria-labelledby="business-actions-title">
        <div className="business-panel-heading">
          <div><span className="business-eyebrow">DAILY OPERATIONS</span>
            <h2 id="business-actions-title">Your workspace</h2>
            <p>Every card opens a real module in this business, without switching tenant context.</p></div>
        </div>
        <div className="business-action-grid">
          {actions.map(({ key, label, description, Icon }) =>
            <button type="button" key={key} className="business-action-card"
              onClick={() => onNavigate(key)}>
              <span className="business-action-icon"><Icon size={20} /></span>
              <span className="business-action-text">
                <strong>{label}</strong><small>{description}</small>
              </span>
              <ArrowRight size={17} className="business-action-arrow" />
            </button>)}
        </div>
        {isOwner && <div className="business-owner-link">
          <span><Users size={18} /> Need to manage user access?</span>
          <button type="button" onClick={() => onNavigate('team')}>Staff & Access <ArrowRight size={15} /></button>
        </div>}
      </section>

      <div className="business-dashboard-side">
        <section className="content-section business-attention-panel" aria-labelledby="business-next-title">
          <div className="business-panel-heading">
            <div><span className="business-eyebrow">START HERE</span>
              <h2 id="business-next-title">{hasProducts ? 'Business checklist' : 'Prepare your store'}</h2></div>
          </div>
          <ol className="business-setup-steps">
            <li><span className="business-setup-step"><Layers3 size={17} /></span>
              <div><strong>Organize your catalog</strong>
                <p>Set up the categories and units used by your products.</p>
                <button type="button" onClick={() => onNavigate('masters')}>Categories & Units <ArrowRight size={14} /></button>
              </div></li>
            <li><span className="business-setup-step"><Package size={17} /></span>
              <div><strong>{hasProducts ? 'Review your products' : 'Add your first product'}</strong>
                <p>{hasProducts ? 'Products exist; review prices and product details.' :
                  'Add an actual product before recording its opening stock.'}</p>
                <button type="button" onClick={() => onNavigate('products')}>Open Products <ArrowRight size={14} /></button>
              </div></li>
            <li><span className="business-setup-step"><Boxes size={17} /></span>
              <div><strong>Verify inventory</strong>
                <p>Review opening stock and location-wise movements.</p>
                <button type="button" onClick={() => onNavigate('stock')}>Open Inventory <ArrowRight size={14} /></button>
              </div></li>
          </ol>
          {isOwner && pendingApprovals > 0 && <button type="button"
            className="business-approval-cta" onClick={onReviewApprovals}>
            <ClipboardCheck size={17} /> Review {pendingApprovals} draft purchase
            {pendingApprovals === 1 ? ' order' : ' orders'} <ArrowRight size={16} />
          </button>}
        </section>
        <section className="content-section business-locations-panel" aria-labelledby="business-locations-title">
          <div className="business-panel-heading">
            <div><span className="business-eyebrow">ASSIGNED LOCATIONS</span>
              <h2 id="business-locations-title">Store locations</h2></div>
          </div>
          {locations.length ? <div className="business-location-list">
            {locations.map(location => <div className="business-location-item" key={location.id}>
              <span><Store size={17} /></span>
              <div><strong>{location.name}</strong><small>Code: {location.code}</small></div>
            </div>)}
          </div> : <p className="business-no-locations">No locations assigned. Ask your business owner to review access.</p>}
          <p className="business-location-footnote">Your visible locations are determined by your server-side role.</p>
        </section>
      </div>
    </div>
    <p className="business-dashboard-disclaimer">Stock quantities, sales totals and revenue are not shown without a verified reporting API.
      Nothing on this dashboard is a fabricated financial KPI.</p>
  </div>
}

function ShieldRoleIcon() {
  return <CheckCircle2 size={15} aria-hidden="true" />
}
