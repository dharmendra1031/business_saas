import { useEffect, useState } from 'react'
import {
  Activity, AlertTriangle, BadgeCheck, Building2, Clock3, RefreshCw,
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

const planColors = ['#165ed4', '#14a3a5', '#8356c8', '#f5a524', '#dc5c65', '#8796aa']

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
    <path d={area} fill="#165ed4" fillOpacity=".10" />
    <path d={line} fill="none" stroke="#165ed4" strokeWidth="3"
      strokeLinecap="round" strokeLinejoin="round" />
    {coordinates.map((point, index) => <g key={index}>
      <circle cx={point.x} cy={point.y} r="4" fill="#fff" stroke="#165ed4" strokeWidth="2">
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

export default function PlatformOverview({ onViewBusiness }: { onViewBusiness: (id: number) => void }) {
  const [months, setMonths] = useState<6 | 12>(6)
  const [summary, setSummary] = useState<Overview | null>(null)
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
        setError('Unable to load the platform overview. Please retry.')
      })
      .finally(() => { if (active) setLoading(false) })
    return () => { active = false }
  }, [months, reload])

  const metrics = summary ? [
    { label: 'Total Customers', value: summary.totalCustomers, Icon: Users },
    { label: 'Total Businesses', value: summary.totalBusinesses, Icon: Building2 },
    { label: 'Active Subscriptions', value: summary.activeSubscriptions, Icon: BadgeCheck },
    { label: 'Active Trials', value: summary.activeTrials, Icon: Clock3 },
    { label: 'Expired Trials', value: summary.expiredTrials, Icon: AlertTriangle }
  ] : []

  return <div className="platform-dashboard">
    <div className="page-header">
      <div>
        <h1>Platform Overview</h1>
        <p>Live overview of customer accounts, subscriptions and business activity.</p>
      </div>
      <button type="button" className="btn btn-outline-secondary btn-sm"
        disabled={loading} onClick={() => setReload(value => value + 1)}>
        <RefreshCw size={15} /> Refresh
      </button>
    </div>

    {error && <div className="alert alert-danger" role="alert">{error}
      <button type="button" className="btn btn-outline-secondary btn-sm ms-2"
        onClick={() => setReload(value => value + 1)}>Retry</button>
    </div>}
    {loading ? <p role="status" className="text-secondary">Loading platform overview…</p>
      : summary && <>
        <div className="platform-metric-grid">
          {metrics.map(({ label, value, Icon }) =>
            <section className="platform-metric" key={label}>
              <div className="platform-metric-top">
                <span>{label}</span><Icon size={19} aria-hidden="true" />
              </div>
              <strong>{value.toLocaleString()}</strong>
              <small>Current platform total</small>
            </section>)}
          <section className="platform-metric">
            <div className="platform-metric-top">
              <span>Monthly Revenue</span><Wallet size={19} aria-hidden="true" />
            </div>
            <strong className="platform-unavailable">Not available</strong>
            <small>Subscription payment ledger pending</small>
          </section>
        </div>

        <div className="platform-analytics-grid">
          <section className="content-section">
            <div className="platform-panel-heading">
              <div><h2><TrendingUp size={19} /> Business Growth</h2>
                <p>New businesses created each month</p>
              </div>
              <select className="form-select form-select-sm" aria-label="Business growth period"
                value={months} onChange={event => setMonths(Number(event.target.value) as 6 | 12)}>
                <option value={6}>Last 6 months</option>
                <option value={12}>Last 12 months</option>
              </select>
            </div>
            <GrowthChart data={summary.businessGrowth} />
          </section>
          <section className="content-section">
            <div className="platform-panel-heading">
              <div><h2><BadgeCheck size={19} /> Businesses by Plan</h2>
                <p>Current plan assignments (all subscription statuses)</p>
              </div>
            </div>
            <PlanBreakdown items={summary.businessesByPlan}
              unassigned={summary.unassignedBusinesses} />
          </section>
        </div>

        <div className="platform-bottom-grid">
          <section className="content-section">
            <div className="platform-panel-heading">
              <div><h2><Clock3 size={19} /> Trials Expiring Soon</h2>
                <p>Trials ending in the next 7 days · {summary.expiringTrialCount} total</p>
              </div>
            </div>
            {summary.expiringTrials.length === 0 ?
              <p className="platform-chart-empty">No trials are expiring in the next 7 days.</p> :
              <div className="table-responsive">
                <table className="table table-sm platform-table">
                  <thead><tr><th>Business</th><th>Customer</th><th>Trial Ends</th>
                    <th>Status</th><th className="text-end">Action</th></tr></thead>
                  <tbody>{summary.expiringTrials.map(trial => <tr key={trial.businessId}>
                    <td><strong>{trial.businessName}</strong>
                      <small className="d-block text-secondary">#{trial.businessId} · {trial.businessType}</small></td>
                    <td>{trial.ownerEmail ?? 'Not available'}</td>
                    <td>{new Date(trial.trialEndsAtUtc).toLocaleDateString()}</td>
                    <td><span className="platform-status-warning">Expiring</span></td>
                    <td className="text-end">
                      <button className="btn btn-outline-primary btn-sm" type="button"
                        aria-label={`View details for ${trial.businessName}`}
                        onClick={() => onViewBusiness(trial.businessId)}>View</button>
                    </td>
                  </tr>)}</tbody>
                </table>
                {summary.expiringTrialCount > summary.expiringTrials.length &&
                  <p className="small text-secondary mt-2 mb-0">
                    Showing the {summary.expiringTrials.length} soonest expirations.
                  </p>}
              </div>}
          </section>

          <section className="content-section">
            <div className="platform-panel-heading">
              <div><h2><Activity size={19} /> Recent Platform Activity</h2>
                <p>Recorded security and platform events</p>
              </div>
            </div>
            {summary.recentActivity.length === 0 ?
              <p className="platform-chart-empty">No platform events have been recorded yet.</p> :
              <div className="platform-activity-list">
                {summary.recentActivity.map(item => <div className="platform-activity" key={item.id}>
                  <div className="platform-activity-dot" aria-hidden="true" />
                  <div><strong>{activityLabel(item.action)}</strong>
                    <span>Actor #{item.actorUserId}
                      {item.targetId !== null ? ` · ${item.targetType} #${item.targetId}` : ''}
                    </span>
                    <time dateTime={item.createdAtUtc}>
                      {new Date(item.createdAtUtc).toLocaleString()}
                    </time>
                  </div>
                </div>)}
              </div>}
          </section>
        </div>
        <p className="platform-overview-note">
          Account and subscription counts are platform-wide. Monthly growth reflects business creation only;
          customer registration timestamps are not stored yet. Revenue remains unavailable until billing is implemented.
        </p>
      </>}
  </div>
}
