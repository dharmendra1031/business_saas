import { useEffect, useState } from 'react'
import { api, SessionExpiredError } from './api'

interface Overview {
  totalCustomers: number
  totalBusinesses: number
  activeSubscriptions: number
  activeTrials: number
  expiredTrials: number
}

export default function PlatformOverview() {
  const [summary, setSummary] = useState<Overview | null>(null)
  const [error, setError] = useState('')
  const [loading, setLoading] = useState(true)
  const [reload, setReload] = useState(0)

  useEffect(() => {
    let active = true
    setLoading(true)
    setError('')
    api<Overview>('/platform/overview')
      .then(result => { if (active) setSummary(result) })
      .catch(cause => {
        if (!active || cause instanceof SessionExpiredError) return
        setSummary(null)
        setError('Unable to load platform overview.')
      })
      .finally(() => { if (active) setLoading(false) })
    return () => { active = false }
  }, [reload])

  const metrics = summary ? [
    ['Customer Accounts', summary.totalCustomers],
    ['Total Businesses', summary.totalBusinesses],
    ['Active Subscriptions', summary.activeSubscriptions],
    ['Active Trials', summary.activeTrials],
    ['Expired Trials', summary.expiredTrials]
  ] as const : []

  return <>
    <div className="page-header">
      <div>
        <h1>Platform Overview</h1>
        <p>Live account, business and subscription counts across the platform.</p>
      </div>
      <button className="btn btn-outline-secondary btn-sm" type="button"
        onClick={() => setReload(value => value + 1)} disabled={loading}>Refresh</button>
    </div>
    {error && <div className="alert alert-danger" role="alert">{error}
      <button type="button" className="btn btn-outline-secondary btn-sm ms-2"
        onClick={() => setReload(value => value + 1)}>Retry</button>
    </div>}
    {loading ? <p role="status">Loading platform summary…</p> : summary && <>
      <div className="row g-3">
        {metrics.map(([label, value]) => <div className="col-12 col-sm-6 col-xl-4" key={label}>
          <section className="content-section h-100">
            <p className="small text-secondary mb-2">{label}</p>
            <h2 className="mb-0">{value.toLocaleString()}</h2>
          </section>
        </div>)}
        <div className="col-12 col-sm-6 col-xl-4">
          <section className="content-section h-100">
            <p className="small text-secondary mb-2">Monthly Revenue</p>
            <h2 className="mb-0">Not available</h2>
            <p className="small text-secondary mb-0">Subscription payment records are not implemented yet.</p>
          </section>
        </div>
      </div>
      <p className="small text-secondary mt-3">
        Customer Accounts excludes the dedicated Super Admin. Trial counts use the current expiry date.
        Revenue is not calculated until subscription billing is implemented.
      </p>
    </>}
  </>
}
