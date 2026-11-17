import { useEffect, useState, type FormEvent } from 'react'
import { api, SessionExpiredError } from './api'

interface Customer {
  id: number
  email: string
  emailConfirmed: boolean
  ownedBusinessCount: number
}

interface CustomerPage {
  items: Customer[]
  total: number
  page: number
  pageSize: number
}

interface OwnedBusiness {
  id: number
  name: string
  typeName: string
  planCode: string | null
  subscriptionStatus: string | null
}

interface CustomerDetails {
  id: number
  email: string
  emailConfirmed: boolean
  businesses: OwnedBusiness[]
}

export default function PlatformCustomers() {
  const [search, setSearch] = useState('')
  const [term, setTerm] = useState('')
  const [page, setPage] = useState(1)
  const [reload, setReload] = useState(0)
  const [list, setList] = useState<CustomerPage | null>(null)
  const [details, setDetails] = useState<CustomerDetails | null>(null)
  const [error, setError] = useState('')
  const [loading, setLoading] = useState(true)
  const [detailLoading, setDetailLoading] = useState(false)

  useEffect(() => {
    let active = true
    setLoading(true)
    setError('')
    api<CustomerPage>(`/platform/customers?page=${page}&search=${encodeURIComponent(term)}`)
      .then(result => { if (active) setList(result) })
      .catch(cause => {
        if (!active || cause instanceof SessionExpiredError) return
        setList(null)
        setError('Unable to load customer accounts.')
      })
      .finally(() => { if (active) setLoading(false) })
    return () => { active = false }
  }, [term, page, reload])

  async function openCustomer(id: number) {
    setError('')
    setDetailLoading(true)
    try {
      setDetails(await api<CustomerDetails>(`/platform/customers/${id}`))
    } catch (cause) {
      if (!(cause instanceof SessionExpiredError))
        setError('Unable to open customer account.')
    } finally {
      setDetailLoading(false)
    }
  }

  function applySearch(event: FormEvent) {
    event.preventDefault()
    setPage(1)
    setTerm(search.trim())
    setDetails(null)
  }

  if (details) return <>
    <div className="page-header">
      <div>
        <button type="button" className="back-link" onClick={() => { setDetails(null); setError('') }}>
          All customers
        </button>
        <h1>{details.email}</h1>
        <p>Customer #{details.id} · Read-only account summary</p>
      </div>
    </div>
    <section className="content-section">
      <dl className="detail-list">
        <div><dt>Customer ID</dt><dd>{details.id}</dd></div>
        <div><dt>Email</dt><dd>{details.email}</dd></div>
        <div><dt>Email verified</dt><dd>{details.emailConfirmed ? 'Yes' : 'No'}</dd></div>
        <div><dt>Owned Businesses</dt><dd>{details.businesses.length}</dd></div>
      </dl>
    </section>
    <section className="content-section mt-3">
      <h2>Businesses created by this customer</h2>
      {details.businesses.length === 0 ? <p className="text-secondary">No businesses created yet.</p>
        : <div className="table-responsive"><table className="table table-sm">
          <thead><tr><th>Business</th><th>Type</th><th>Plan</th><th>Status</th></tr></thead>
          <tbody>{details.businesses.map(business => <tr key={business.id}>
            <td>{business.name} <span className="small text-secondary">#{business.id}</span></td>
            <td>{business.typeName}</td>
            <td>{business.planCode ?? 'Not assigned'}</td>
            <td>{business.subscriptionStatus ?? 'No subscription'}</td>
          </tr>)}</tbody>
        </table></div>}
    </section>
    <p className="small text-secondary mt-3">
      Only account and subscription metadata is visible here. Shop records remain private.
    </p>
  </>

  return <>
    <div className="page-header">
      <div>
        <h1>Customer Accounts</h1>
        <p>Search registered accounts and view businesses they own. This page is read-only.</p>
      </div>
    </div>
    <section className="content-section">
      <form className="platform-search mb-3" onSubmit={applySearch}>
        <div>
          <label className="form-label" htmlFor="customer-search">Customer email</label>
          <input className="form-control" id="customer-search" type="search"
            maxLength={100} placeholder="Search by email" value={search}
            onChange={event => setSearch(event.target.value)} />
        </div>
        <button className="btn btn-primary" type="submit">Search</button>
        {term && <button className="btn btn-outline-secondary" type="button"
          onClick={() => { setSearch(''); setTerm(''); setPage(1) }}>Clear</button>}
      </form>
      {error && <div className="alert alert-danger" role="alert">{error}
        <button type="button" className="btn btn-outline-secondary btn-sm ms-2"
          onClick={() => setReload(value => value + 1)}>Retry</button>
      </div>}
      {detailLoading && <p role="status" className="text-secondary">Opening customer…</p>}
      {loading ? <p role="status">Loading customers…</p>
        : list?.total === 0 ? <p role="status" className="text-secondary">
          {term ? 'No customers match this search.' : 'No customer accounts registered yet.'}
        </p> : list && <>
          <div className="table-responsive"><table className="table table-sm platform-table">
            <thead><tr><th>Email</th><th>Verified</th><th>Owned Businesses</th>
              <th className="text-end">Action</th></tr></thead>
            <tbody>{list.items.map(customer => <tr key={customer.id}>
              <td>{customer.email}<small className="d-block text-secondary">#{customer.id}</small></td>
              <td>{customer.emailConfirmed ? 'Yes' : 'No'}</td>
              <td>{customer.ownedBusinessCount}</td>
              <td className="text-end">
                <button type="button" className="btn btn-outline-secondary btn-sm"
                  disabled={detailLoading} onClick={() => void openCustomer(customer.id)}>
                  View
                </button>
              </td>
            </tr>)}</tbody>
          </table></div>
          <div className="platform-summary">
            <p className="text-secondary small">
              {list.items.length
                ? `Showing ${(page - 1) * list.pageSize + 1}–${(page - 1) * list.pageSize + list.items.length} of ${list.total}`
                : `No customers on page ${page}`}
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
}
