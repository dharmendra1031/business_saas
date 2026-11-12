import { useEffect, useState, type FormEvent } from 'react'
import { api, SessionExpiredError } from './api'

interface BusinessRow {
  id: number
  name: string
  typeName: string
  ownerEmail: string
  countryCode: string
  planCode: string | null
  subscriptionStatus: string | null
  trialEndsAtUtc: string | null
}

interface BusinessPage {
  items: BusinessRow[]
  total: number
  page: number
  pageSize: number
}

interface BusinessDetails extends BusinessRow {
  currencyCode: string
  timeZoneId: string
  createdAtUtc: string
}

function statusLabel(status: string | null, trialEnds: string | null = null) {
  if (status === 'Trial' && trialEnds && new Date(trialEnds).getTime() <= Date.now())
    return 'Trial expired'
  return status ?? 'No subscription'
}

export default function PlatformAdmin() {
  const [search, setSearch] = useState('')
  const [term, setTerm] = useState('')
  const [page, setPage] = useState(1)
  const [reload, setReload] = useState(0)
  const [list, setList] = useState<BusinessPage | null>(null)
  const [details, setDetails] = useState<BusinessDetails | null>(null)
  const [error, setError] = useState('')
  const [loading, setLoading] = useState(true)
  const [detailLoading, setDetailLoading] = useState(false)

  useEffect(() => {
    let active = true
    setLoading(true)
    setError('')

    api<BusinessPage>(`/platform/businesses?page=${page}&search=${encodeURIComponent(term)}`)
      .then(result => { if (active) setList(result) })
      .catch(cause => {
        if (!active || cause instanceof SessionExpiredError) return
        setList(null)
        setError('Unable to load businesses. Check your access or retry.')
      })
      .finally(() => { if (active) setLoading(false) })

    return () => { active = false }
  }, [term, page, reload])

  function applySearch(event: FormEvent) {
    event.preventDefault()
    setPage(1)
    setTerm(search.trim())
    setDetails(null)
  }

  async function openBusiness(id: number) {
    setError('')
    setDetailLoading(true)
    try {
      const result = await api<BusinessDetails>(`/platform/businesses/${id}`)
      setDetails(result)
    } catch (cause) {
      if (!(cause instanceof SessionExpiredError))
        setError('Unable to open business details. Please retry.')
    } finally {
      setDetailLoading(false)
    }
  }

  if (details) {
    return (
      <>
        <div className="page-header">
          <div>
            <button type="button" className="back-link" onClick={() => { setDetails(null); setError('') }}>
              All businesses
            </button>
            <h1>{details.name}</h1>
            <p>Read-only platform business summary</p>
          </div>
        </div>
        <section className="content-section" aria-label="Business details">
          <dl className="detail-list">
            <div><dt>Business ID</dt><dd>{details.id}</dd></div>
            <div><dt>Owner</dt><dd>{details.ownerEmail}</dd></div>
            <div><dt>Business type</dt><dd>{details.typeName}</dd></div>
            <div><dt>Country</dt><dd>{details.countryCode}</dd></div>
            <div><dt>Currency</dt><dd>{details.currencyCode}</dd></div>
            <div><dt>Timezone</dt><dd>{details.timeZoneId}</dd></div>
            <div><dt>Plan</dt><dd>{details.planCode ?? 'Not assigned'}</dd></div>
            <div><dt>Subscription</dt><dd>{statusLabel(details.subscriptionStatus, details.trialEndsAtUtc)}</dd></div>
            {details.trialEndsAtUtc && <div><dt>Trial end</dt>
              <dd>{new Date(details.trialEndsAtUtc).toLocaleDateString()}</dd></div>}
          </dl>
        </section>
        <p className="text-secondary small mt-3">
          Plan editing, trial extensions and business-type changes are not available yet.
        </p>
      </>
    )
  }

  return (
    <>
      <div className="page-header">
        <div>
          <h1>Platform Businesses</h1>
          <p>Find businesses and review their current subscription. No changes can be made here.</p>
        </div>
      </div>
      <section className="content-section">
        <form className="platform-search mb-3" onSubmit={applySearch}>
          <div>
            <label className="form-label" htmlFor="platform-search">Business name</label>
            <input id="platform-search" className="form-control" type="search"
              maxLength={100} placeholder="Search businesses" value={search}
              onChange={event => setSearch(event.target.value)} />
          </div>
          <button type="submit" className="btn btn-primary">Search</button>
          {term && <button type="button" className="btn btn-outline-secondary"
            onClick={() => { setSearch(''); setTerm(''); setPage(1) }}>Clear</button>}
        </form>
        {error && <div className="alert alert-danger" role="alert">{error}
          <button type="button" className="btn btn-outline-secondary btn-sm ms-2"
            onClick={() => setReload(value => value + 1)}>Retry</button>
        </div>}
        {detailLoading && <p role="status" className="text-secondary">Opening business details…</p>}
        {loading ? <p role="status" className="text-secondary">Loading businesses…</p>
          : list?.total === 0 ? <p role="status" className="text-secondary">
            {term ? 'No businesses match this search.' : 'No businesses have been created yet.'}
          </p>
          : list && <>
            <div className="table-responsive">
              <table className="table table-sm platform-table">
                <thead>
                  <tr><th scope="col">Business</th><th scope="col">Owner</th>
                    <th scope="col">Plan</th><th scope="col">Status</th>
                    <th scope="col" className="text-end">Action</th></tr>
                </thead>
                <tbody>
                  {list.items.map(item => (
                    <tr key={item.id}>
                      <td><strong>{item.name}</strong><small className="d-block text-secondary">
                        {item.typeName} · {item.countryCode}</small></td>
                      <td>{item.ownerEmail}</td>
                      <td>{item.planCode ?? 'Not assigned'}</td>
                      <td>{statusLabel(item.subscriptionStatus, item.trialEndsAtUtc)}</td>
                      <td className="text-end"><button type="button"
                        className="btn btn-outline-secondary btn-sm" disabled={detailLoading}
                        onClick={() => void openBusiness(item.id)}>View</button></td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
            <div className="platform-summary">
              <p className="text-secondary small">
                {list.items.length ? `Showing ${(page - 1) * list.pageSize + 1}–${(page - 1) * list.pageSize + list.items.length} of ${list.total}`
                  : `No businesses on page ${page}`}
              </p>
              <div className="d-flex align-items-center gap-2">
                <button type="button" className="btn btn-outline-secondary btn-sm"
                  disabled={page <= 1} onClick={() => setPage(value => value - 1)}>Previous</button>
                <span className="small">Page {page}</span>
                <button type="button" className="btn btn-outline-secondary btn-sm"
                  disabled={page * list.pageSize >= list.total}
                  onClick={() => setPage(value => value + 1)}>Next</button>
              </div>
            </div>
          </>}
      </section>
    </>
  )
}
