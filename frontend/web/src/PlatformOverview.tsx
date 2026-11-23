import { useEffect, useState } from 'react'
import {
  Activity, AlertTriangle, ArrowRight, BadgeCheck, Building2, Clock3, Plus, RefreshCw,
  TrendingUp, Users, Wallet
} from 'lucide-react'
import { api, SessionExpiredError } from './api'

interface GrowthMonth {
  year: number
  month: number
  businessesCreated: number
}

interface PlanCount {
  planCode: string
  businessCount: number
}

interface ExpiringTrial {
  businessId: number
  businessName: string
  ownerEmail: string | null
  businessType: string
  trialEndsAtUtc: string
}

interface PlatformActivity {
  id: number
  actorUserId: number
  action: string
  targetType: string
  targetId: number | null
  createdAtUtc: string
}

interface Overview {
  totalCustomers: number
  totalBusinesses: number
  activeSubscriptions: number
  activeTrials: number
  expiredTrials: number
  businessGrowth: GrowthMonth[]
  businessesByPlan: PlanCount[]
  unassignedBusinesses: number
  expiringTrialCount: number
  expiringTrials: ExpiringTrial[]
  recentActivity: PlatformActivity[]
}

const planColors = ['#5947de', '#9c91f3', '#245cc9', '#e7b85f', '#48a68b', '#8796aa']

function monthLabel(item: GrowthMonth) {
  return new Date(Date.UTC(item.year, item.month - 1, 1))
    .toLocaleDateString('en-IN', { month: 'short', year: '2-digit', timeZone: 'UTC' })
}

function GrowthChart({ data }: { data: GrowthMonth[] }) {
  const total = data.reduce((sum, item) => sum + item.businessesCreated, 0)
  if (total === 0)
    return <p className="platform-chart-empty">No businesses were created during this period.</p>

  const maximum = Math.max(1, ...data.map(item => item.businessesCreated))
  const coordinates = data.map((item, index) => ({
    x: 44 + index * 550 / Math.max(1, data.length - 1),
    y: 176 - item.businessesCreated * 140 / maximum
  }))
  const line = coordinates.map((point, index) =>
    `${index === 0 ? 'M' : 'L'} ${point.x.toFixed(1)} ${point.y.toFixed(1)}`).join(' ')
  const area = `${line} L ${coordinates.at(-1)!.x.toFixed(1)} 176 L 44 176 Z`

  return <svg className="platform-growth-chart" viewBox="0 0 640 224"
    role="img" aria-label="Monthly businesses created">
    <title>Business Growth</title>
    <desc>{data.map(item => `${monthLabel(item)}: ${item.businessesCreated}`).join('; ')}</desc>
    {[0, 0.5, 1].map(ratio => <g key={ratio}>
      <line x1="44" y1={176 - ratio * 140} x2="600" y2={176 - ratio * 140}
        stroke="#e5eaf1" strokeDasharray={ratio ? '4 5' : undefined} />
      <text x="35" y={181 - ratio * 140} textAnchor="end" fill="#718198" fontSize="12">
        {Math.round(maximum * ratio)}
      </text>
    </g>)}
    <path d={area} fill="#5947de" fillOpacity=".09" />
    <path d={line} fill="none" stroke="#5947de" strokeWidth="3"
      strokeLinecap="round" strokeLinejoin="round" />
    {coordinates.map((point, index) => <g key={index}>
      <circle cx={point.x} cy={point.y} r="4" fill="#fff" stroke="#5947de" strokeWidth="2">
        <title>{monthLabel(data[index])}: {data[index].businessesCreated} new businesses</title>
      </circle>
      {(data.length <= 6 || index % 2 === 0 || index === data.length - 1) &&
        <text x={point.x} y="207" textAnchor="middle" fill="#687b90" fontSize="11">
          {monthLabel(data[index])}
        </text>}
    </g>)}
  </svg>
}

function PlanBreakdown({ items, unassigned }: { items: PlanCount[]; unassigned: number }) {
  const plans = [
    ...items.map(item => ({ name: item.planCode, count: item.businessCount })),
    ...(unassigned ? [{ name: 'Not assigned', count: unassigned }] : [])
  ]
  const total = plans.reduce((sum, item) => sum + item.count, 0)
  if (total === 0)
    return <p className="platform-chart-empty">No businesses or subscriptions to show yet.</p>

  let progress = 0
  const segments = plans.map((plan, index) => {
    const from = progress
    progress += plan.count / total * 100
    return `${planColors[index % planColors.length]} ${from.toFixed(2)}% ${progress.toFixed(2)}%`
  })
  return <div className="platform-plan-chart">
    <div className="platform-donut" role="img"
      aria-label={plans.map(p => `${p.name}: ${p.count}`).join(', ')}
      style={{ background: `conic-gradient(${segments.join(', ')})` }}>
      <div className="platform-donut-center" aria-hidden="true">
        <strong>{total.toLocaleString()}</strong><span>Businesses</span>
      </div>
    </div>
    <div className="platform-plan-legend">
      {plans.map((plan, index) => <div className="platform-plan-row" key={plan.name}>
        <span className="platform-plan-dot" style={{ background: planColors[index % planColors.length] }} />
        <span>{plan.name}</span>
        <strong>{plan.count}</strong>
        <span className="text-secondary">{Math.round(plan.count * 100 / total)}%</span>
      </div>)}
    </div>
  </div>
}

function activityLabel(action: string) {
  if (action === 'PlatformOwner.SignIn') return 'Super Admin signed in'
  if (action === 'PlatformOwner.MfaEnabled') return 'Authenticator enabled'
  return action
}


interface RecentBusiness {
  id: number
  name: string
  countryCode: string
  typeName: string
  ownerEmail: string
  planCode: string | null
  subscriptionStatus: string | null
  trialEndsAtUtc: string | null
}

interface RecentBusinessPage {
  items: RecentBusiness[]
  total: number
}

function currentStatus(item: RecentBusiness) {
  if (item.subscriptionStatus === 'Trial' && item.trialEndsAtUtc &&
    new Date(item.trialEndsAtUtc).getTime() <= Date.now()) return 'Expired Trial'
  return item.subscriptionStatus ?? 'Unassigned'
}

function statusClass(status: string) {
  if (status === 'Active') return 'is-active'
  if (status === 'Trial') return 'is-trial'
  return 'is-muted'
}

export type OverviewSubscriptionFilter = 'Active' | 'Trial' | 'TrialExpired'

export default function PlatformOverview({
  onViewBusiness, onSeeBusinesses, onCreateBusiness
}: {
  onViewBusiness: (id: number) => void
  onSeeBusinesses: (status?: OverviewSubscriptionFilter) => void
  onCreateBusiness: () => void
}) {
  const [months, setMonths] = useState<6 | 12>(6)
  const [summary, setSummary] = useState<Overview | null>(null)
  const [recent, setRecent] = useState<RecentBusiness[]>([])
  const [recentError, setRecentError] = useState(false)
  const [error, setError] = useState('')
  const [loading, setLoading] = useState(true)
  const [reload, setReload] = useState(0)

  useEffect(() => {
    let active = true
    setLoading(true)
    setError('')
    api<Overview>(`/platform/overview?months=${months}`)
      .then(result => { if (active) setSummary(result) })
      .catch(cause => {
        if (!active || cause instanceof SessionExpiredError) return
        setSummary(null)
        setError('Unable to load the platform dashboard. Please retry.')
      })
      .finally(() => { if (active) setLoading(false) })
    return () => { active = false }
  }, [months, reload])

  useEffect(() => {
    let active = true
    setRecentError(false)
    api<RecentBusinessPage>('/platform/businesses?page=1&pageSize=5')
      .then(result => { if (active) setRecent(result.items) })
      .catch(cause => {
        if (!active || cause instanceof SessionExpiredError) return
        setRecent([])
        setRecentError(true)
      })
    return () => { active = false }
  }, [reload])

  const metrics = summary ? [
    { label: 'Total Businesses', value: summary.totalBusinesses, Icon: Building2, kind: 'blue' },
    { label: 'Total Customers', value: summary.totalCustomers, Icon: Users, kind: 'violet' },
    { label: 'Active Subscriptions', value: summary.activeSubscriptions, Icon: BadgeCheck, kind: 'green', status: 'Active' as const },
    { label: 'Active Trials', value: summary.activeTrials, Icon: Clock3, kind: 'amber', status: 'Trial' as const },
    { label: 'Expired Trials', value: summary.expiredTrials, Icon: AlertTriangle, kind: 'rose', status: 'TrialExpired' as const }
  ] : []

  return <div className="platform-dashboard">
    <div className="page-header platform-dashboard-hero">
      <div className="platform-heading-group">
        <span className="platform-heading-icon"><Building2 size={25} /></span>
        <div>
          <span className="platform-kicker">SUPER ADMIN</span>
          <h1>Platform Dashboard</h1>
          <p>Manage businesses, subscriptions, customer accounts and platform operations.</p>
        </div>
      </div>
      <div className="platform-hero-actions">
        <label className="visually-hidden" htmlFor="platform-dashboard-period">Business growth period</label>
        <select className="form-select form-select-sm" id="platform-dashboard-period"
          value={months} disabled={loading}
          onChange={event => setMonths(Number(event.target.value) as 6 | 12)}>
          <option value={6}>Last 6 months</option>
          <option value={12}>Last 12 months</option>
        </select>
        <button type="button" className="btn btn-outline-secondary btn-sm" disabled={loading}
          onClick={() => setReload(value => value + 1)}><RefreshCw size={15} /> Refresh</button>
        <button type="button" className="btn btn-primary btn-sm" onClick={onCreateBusiness}>
          <Plus size={17} /> Create Business
        </button>
      </div>
    </div>

    {error && <div className="alert alert-danger" role="alert">{error}
      <button className="btn btn-outline-secondary btn-sm ms-2" type="button"
        onClick={() => setReload(value => value + 1)}>Retry</button>
    </div>}

    {loading ? <div className="platform-overview-loading" role="status" aria-live="polite">
      <span className="spinner-border spinner-border-sm" aria-hidden="true" />
      Loading actual platform totals and activity…
    </div> : summary && <>
      <div className="platform-metric-grid">
        {metrics.map(({ label, value, Icon, kind, status }) =>
          <section className="platform-metric" key={label}>
            <div className="platform-metric-badge">
              <span className={`platform-metric-icon is-${kind}`}><Icon size={23} aria-hidden="true" /></span>
              <div className="platform-metric-content">
                <span>{label}</span><strong>{value.toLocaleString()}</strong>
                {status && <button type="button" className="platform-link-action"
                  onClick={() => onSeeBusinesses(status)}>View {label.toLowerCase()}</button>}
              </div>
            </div>
          </section>)}
      </div>

      <div className="platform-analytics-grid">
        <section className="content-section platform-analytics-card">
          <div className="platform-panel-heading">
            <div><h2><TrendingUp size={19} /> Business Growth</h2>
              <p>Verified new businesses · Last {months} months</p></div>
            <span className="platform-data-tag">Live records</span>
          </div>
          <GrowthChart data={summary.businessGrowth} />
        </section>
        <section className="content-section platform-analytics-card">
          <div className="platform-panel-heading">
            <div><h2><BadgeCheck size={19} /> Subscription by Plan</h2>
              <p>Current plan assignments across all statuses</p></div>
          </div>
          <PlanBreakdown items={summary.businessesByPlan} unassigned={summary.unassignedBusinesses} />
        </section>
      </div>

      <div className="platform-bottom-grid">
        <section className="content-section platform-recent-card">
          <div className="platform-panel-heading">
            <div><h2><Building2 size={19} /> Recent Businesses</h2>
              <p>Latest registered business workspaces</p></div>
            <button className="platform-link-action" type="button" onClick={() => onSeeBusinesses()}>
              View all <ArrowRight size={15} />
            </button>
          </div>
          {recentError ? <p className="platform-chart-empty" role="status">
            Recent businesses are temporarily unavailable. Use Refresh to retry.
          </p> : recent.length === 0 ? <p className="platform-chart-empty">
            No businesses to show yet. Create the first business to get started.
          </p> : <div className="table-responsive">
            <table className="table table-sm platform-table platform-recent-table">
              <thead><tr><th scope="col">Business</th><th scope="col">Plan</th>
                <th scope="col">Status</th><th scope="col" className="text-end">Action</th></tr></thead>
              <tbody>{recent.map(item => {
                const status = currentStatus(item)
                return <tr key={item.id}>
                  <td><strong>{item.name}</strong><small className="d-block text-secondary">
                    #{item.id} · {item.countryCode} · {item.typeName}</small></td>
                  <td>{item.planCode ?? 'Not assigned'}</td>
                  <td><span className={`platform-state-pill ${statusClass(status)}`}>{status}</span></td>
                  <td className="text-end"><button type="button" className="btn btn-outline-secondary btn-sm"
                    onClick={() => onViewBusiness(item.id)} aria-label={`View business ${item.name}`}>
                    View</button></td>
                </tr>
              })}</tbody>
            </table>
          </div>}
        </section>
        <section className="content-section platform-activity-card">
          <div className="platform-panel-heading">
            <div><h2><Activity size={19} /> Platform Activity</h2>
              <p>Last recorded platform actions</p></div>
          </div>
          {summary.recentActivity.length === 0 ? <p className="platform-chart-empty">
            No platform events recorded yet.
          </p> : <div className="platform-activity-list">
            {summary.recentActivity.slice(0, 5).map(item => <div className="platform-activity" key={item.id}>
              <span className="platform-activity-icon"><Activity size={15} /></span>
              <div><strong>{activityLabel(item.action)}</strong>
                <span>Actor #{item.actorUserId}{item.targetId !== null
                  ? ` · ${item.targetType} #${item.targetId}` : ''}</span>
                <time dateTime={item.createdAtUtc}>{new Date(item.createdAtUtc).toLocaleString()}</time>
              </div>
            </div>)}
          </div>}
        </section>
        <section className="content-section platform-quick-card">
          <div className="platform-panel-heading">
            <div><h2><Wallet size={19} /> Quick Stats</h2><p>Current platform signals</p></div>
          </div>
          <dl className="platform-quick-stat-list">
            <div><dt>Active trials</dt><dd>{summary.activeTrials.toLocaleString()}</dd></div>
            <div><dt>Expired trials</dt><dd>{summary.expiredTrials.toLocaleString()}</dd></div>
            <div><dt>Expiring soon</dt><dd>{summary.expiringTrialCount.toLocaleString()}</dd></div>
            <div><dt>Monthly revenue</dt><dd className="platform-stat-unavailable">Unavailable</dd></div>
          </dl>
          <p className="platform-quick-note">Revenue needs a verified subscription payment ledger.</p>
        </section>
      </div>

      <section className="content-section platform-expiring-card">
        <div className="platform-panel-heading">
          <div><h2><Clock3 size={19} /> Trials Expiring Soon</h2>
            <p>Next seven days · {summary.expiringTrialCount.toLocaleString()} trials</p></div>
          <button type="button" className="platform-link-action" onClick={() => onSeeBusinesses()}>
            Businesses <ArrowRight size={15} />
          </button>
        </div>
        {summary.expiringTrials.length === 0 ?
          <p className="platform-empty-inline">No business trials are expiring in the next seven days.</p> :
          <div className="table-responsive">
            <table className="table table-sm platform-table platform-expiring-table">
              <thead><tr><th scope="col">Business</th><th scope="col">Owner</th>
                <th scope="col">Trial ends</th><th scope="col">Status</th>
                <th scope="col" className="text-end">Action</th></tr></thead>
              <tbody>{summary.expiringTrials.map(trial => <tr key={trial.businessId}>
                <td><strong>{trial.businessName}</strong>
                  <small className="d-block text-secondary">#{trial.businessId} · {trial.businessType}</small></td>
                <td>{trial.ownerEmail ?? 'Not available'}</td>
                <td>{new Date(trial.trialEndsAtUtc).toLocaleDateString()}</td>
                <td><span className="platform-state-pill is-trial">Expiring</span></td>
                <td className="text-end"><button type="button" className="btn btn-outline-primary btn-sm"
                  onClick={() => onViewBusiness(trial.businessId)}>View</button></td>
              </tr>)}</tbody>
            </table>
          </div>}
        {summary.expiringTrialCount > summary.expiringTrials.length && <p className="small text-secondary mb-0 mt-2">
          Showing the nearest {summary.expiringTrials.length} trial expirations.
        </p>}
      </section>
      <p className="platform-overview-note">
        Figures are sourced from authorized platform APIs. No demo transactions, assumed revenue or unverified trends are shown.
      </p>
    </>}
  </div>
}
