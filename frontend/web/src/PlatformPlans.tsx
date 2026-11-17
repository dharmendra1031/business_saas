import { useEffect, useState } from 'react'
import { api, SessionExpiredError } from './api'

interface Plan {
  code: string
  name: string
  isPublic: boolean
  assignedBusinesses: number
  pricingConfigured: boolean
}

interface PlanResponse {
  items: Plan[]
  unknownPlanAssignments: number
}

export default function PlatformPlans() {
  const [data, setData] = useState<PlanResponse | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')
  const [reload, setReload] = useState(0)

  useEffect(() => {
    let active = true
    setLoading(true)
    setError('')
    api<PlanResponse>('/platform/plans')
      .then(result => { if (active) setData(result) })
      .catch(cause => {
        if (!active || cause instanceof SessionExpiredError) return
        setData(null)
        setError('Unable to load subscription plans.')
      })
      .finally(() => { if (active) setLoading(false) })
    return () => { active = false }
  }, [reload])

  return <>
    <div className="page-header">
      <div><h1>Subscription Plans</h1>
        <p>Approved plan catalog and live business assignments. Configuration is not available yet.</p>
      </div>
      <button type="button" className="btn btn-outline-secondary btn-sm"
        disabled={loading} onClick={() => setReload(value => value + 1)}>Refresh</button>
    </div>
    <section className="content-section">
      <h2>Plan Directory</h2>
      {error && <div className="alert alert-danger" role="alert">{error}
        <button type="button" className="btn btn-outline-secondary btn-sm ms-2"
          onClick={() => setReload(value => value + 1)}>Retry</button>
      </div>}
      {loading ? <p role="status">Loading plans…</p> : data && <>
        <div className="table-responsive">
          <table className="table platform-table align-middle">
            <thead><tr><th>Plan</th><th>Visibility</th><th>Assigned Businesses</th>
              <th>Pricing</th><th>Configuration</th></tr></thead>
            <tbody>{data.items.map(plan => <tr key={plan.code}>
              <td><strong>{plan.name}</strong>
                <small className="d-block text-secondary">{plan.code}</small></td>
              <td>{plan.isPublic ? 'Public plan' : 'Custom / Contact Sales'}</td>
              <td>{plan.assignedBusinesses.toLocaleString()}</td>
              <td>{plan.pricingConfigured ? 'Configured' : 'Not configured'}</td>
              <td><span className="text-secondary">Not available yet</span></td>
            </tr>)}</tbody>
          </table>
        </div>
        {data.unknownPlanAssignments > 0 && <div className="alert alert-warning">
          {data.unknownPlanAssignments} businesses have a plan code not present in this catalog.
        </div>}
      </>}
    </section>
    <p className="small text-secondary mt-3">
      These plan codes are approved, but prices, billing cycles, features and limits still need
      configuration and approval. No pricing or entitlement changes can be made here.
    </p>
  </>
}
