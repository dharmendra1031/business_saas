import { useEffect, useState, type FormEvent } from 'react'
import { ArrowLeft, ArrowRight, Building2, CheckCircle2, Mail, RefreshCw, Search, ShieldCheck, UserRound, Users, XCircle } from 'lucide-react'
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

function VerificationBadge({ confirmed }: { confirmed: boolean }) {
  return <span className={`platform-state-pill ${confirmed ? 'is-active' : 'is-muted'}`}>
    {confirmed ? <CheckCircle2 size={13} aria-hidden="true" /> : <XCircle size={13} aria-hidden="true" />}
    {confirmed ? 'Verified' : 'Not verified'}
  </span>
}

export default function PlatformCustomers({ onViewBusiness }: { onViewBusiness: (businessId: number) => void }) {
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
        setError('Unable to load customer accounts. Check your access and retry.')
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
        setError('Unable to open customer details. Please retry.')
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

  function closeDetails() {
    setDetails(null)
    setError('')
  }

  if (details) return <div className="platform-customer-page">
    <div className="page-header">
      <div>
        <button type="button" className="back-link" onClick={closeDetails}>
          <ArrowLeft size={16} /> All Customers
        </button>
        <span className="platform-kicker">CUSTOMER ACCOUNTS</span>
        <h1>Customer Details</h1>
        <p>Account #{details.id} · Platform identity and associated businesses</p>
      </div>
    </div>
    {error && <div className="alert alert-danger" role="alert">{error}</div>}
    <section className="content-section platform-customer-profile" aria-label="Customer identity">
      <span className="platform-customer-avatar is-large" aria-hidden="true"><UserRound size={29} /></span>
      <div className="platform-customer-profile-info">
        <h2>{details.email}</h2>
        <p>Customer ID #{details.id}</p>
        <VerificationBadge confirmed={details.emailConfirmed} />
      </div>
      <span className="platform-customer-account-state"><ShieldCheck size={16} /> Read-only access</span>
    </section>
    <div className="platform-customer-summary">
      <section className="content-section">
        <span className="platform-kicker">REGISTERED ACCOUNT</span>
        <div className="platform-customer-summary-value"><Mail size={21} /> Verified email</div>
        <strong>{details.emailConfirmed ? 'Yes' : 'No'}</strong>
        <small>Server-reported contact verification</small>
      </section>
      <section className="content-section">
        <span className="platform-kicker">BUSINESS OWNERSHIP</span>
        <div className="platform-customer-summary-value"><Building2 size={21} /> Linked businesses</div>
        <strong>{details.businesses.length.toLocaleString()}</strong>
        <small>Businesses linked to this account by the platform</small>
      </section>
    </div>
    <section className="content-section platform-directory-card" aria-label="Owned businesses">
      <div className="platform-directory-heading">
        <div><h2>Associated Businesses</h2><p>Subscription and ownership metadata only</p></div>
        <span className="platform-data-tag">Read only</span>
      </div>
      {details.businesses.length === 0 ? <div className="platform-directory-empty">
        <Building2 size={30} />
        <h3>No businesses assigned</h3>
        <p>A Super Admin must provision a business and assign this verified account as Owner.</p>
      </div> : <div className="table-responsive">
        <table className="table table-sm platform-table platform-directory-table">
          <thead><tr><th scope="col">Business</th><th scope="col">Type</th>
            <th scope="col">Plan</th><th scope="col">Subscription</th>
            <th scope="col" className="text-end">Action</th></tr></thead>
          <tbody>{details.businesses.map(business => <tr key={business.id}>
            <td><strong>{business.name}</strong><small className="d-block text-secondary">#{business.id}</small></td>
            <td>{business.typeName}</td>
            <td><span className="platform-plan-pill">{business.planCode ?? 'Not assigned'}</span></td>
            <td><span className={`platform-state-pill ${business.subscriptionStatus === 'Active' ? 'is-active' :
              business.subscriptionStatus === 'Trial' ? 'is-trial' : 'is-muted'}`}>
              {business.subscriptionStatus ?? 'No subscription'}</span></td>
            <td className="text-end"><button type="button" className="btn btn-outline-secondary btn-sm"
              onClick={() => onViewBusiness(business.id)}>View Business <ArrowRight size={14} /></button></td>
          </tr>)}</tbody>
        </table>
      </div>}
    </section>
    <p className="platform-directory-note">This page does not expose shop sales, stock, customer orders or POS transactions.</p>
  </div>

  return <div className="platform-customer-page">
    <div className="page-header">
      <div>
        <span className="platform-kicker">PLATFORM MANAGEMENT</span>
        <h1>Customers</h1>
        <p>Manage visibility of SaaS customer accounts and their businesses. No shop records are accessible here.</p>
      </div>
      <button className="btn btn-outline-secondary btn-sm" type="button" disabled={loading}
        onClick={() => setReload(value => value + 1)}><RefreshCw size={15} /> Refresh</button>
    </div>
    <section className="content-section platform-directory-card" aria-label="Customer account directory">
      <div className="platform-directory-heading">
        <div><h2>Customer Directory</h2><p>{list && !loading ?
          `${list.total.toLocaleString()} accounts matching the current search` : 'Verified platform account records'}</p></div>
        <span className="platform-data-tag">Account registry</span>
      </div>
      <form className="platform-search platform-directory-search" role="search" onSubmit={applySearch}>
        <div className="platform-directory-search-field">
          <label className="visually-hidden" htmlFor="customer-search">Customer email search</label>
          <Search size={18} aria-hidden="true" />
          <input id="customer-search" className="form-control" type="search"
            placeholder="Search by customer email" maxLength={100} value={search}
            onChange={event => setSearch(event.target.value)} />
        </div>
        <button className="btn btn-primary" type="submit">Search</button>
        {(term || search) && <button className="btn btn-outline-secondary" type="button"
          onClick={() => { setSearch(''); setTerm(''); setPage(1) }}>Clear</button>}
      </form>
      {error && <div className="alert alert-danger" role="alert">{error}
        <button type="button" className="btn btn-outline-secondary btn-sm ms-2"
          onClick={() => setReload(value => value + 1)}>Retry</button>
      </div>}
      {detailLoading && <p role="status" className="text-secondary small px-3">Opening customer details…</p>}
      {loading ? <div className="platform-directory-loading" role="status">
        <span className="spinner-border spinner-border-sm" aria-hidden="true" /> Loading customer accounts…
      </div> : list?.total === 0 ? <div className="platform-directory-empty" role="status">
        <Users size={32} />
        <h3>{term ? 'No matching accounts' : 'No registered customer accounts'}</h3>
        <p>{term ? 'Try another email address or clear the search.' :
          'New customer accounts will appear after secure registration and provisioning.'}</p>
      </div> : list && <>
        <div className="table-responsive">
          <table className="table table-sm platform-table platform-directory-table">
            <thead><tr><th scope="col">Customer</th><th scope="col">Email Verification</th>
              <th scope="col">Owned Businesses</th><th scope="col" className="text-end">Action</th></tr></thead>
            <tbody>{list.items.map(customer => <tr key={customer.id}>
              <td><span className="platform-business-cell">
                <span className="platform-customer-avatar" aria-hidden="true"><UserRound size={18} /></span>
                <span><strong>{customer.email}</strong><small>Customer #{customer.id}</small></span>
              </span></td>
              <td><VerificationBadge confirmed={customer.emailConfirmed} /></td>
              <td>{customer.ownedBusinessCount.toLocaleString()}</td>
              <td className="text-end"><button className="btn btn-outline-secondary btn-sm" type="button"
                aria-label={`View customer ${customer.email}`} disabled={detailLoading}
                onClick={() => void openCustomer(customer.id)}>View <ArrowRight size={14} /></button></td>
            </tr>)}</tbody>
          </table>
        </div>
        <div className="platform-summary platform-directory-pagination">
          <p>{list.items.length ?
            `Showing ${(page - 1) * list.pageSize + 1}–${(page - 1) * list.pageSize + list.items.length} of ${list.total}` :
            `No customer accounts on page ${page}`}</p>
          <div className="platform-directory-pages">
            <button type="button" className="btn btn-outline-secondary btn-sm"
              disabled={page <= 1} onClick={() => setPage(value => value - 1)}>Previous</button>
            <span>Page {page}</span>
            <button type="button" className="btn btn-outline-secondary btn-sm"
              disabled={page * list.pageSize >= list.total} onClick={() => setPage(value => value + 1)}>Next</button>
          </div>
        </div>
      </>}
    </section>
    <p className="platform-directory-note">Account restrictions and new-customer invitation actions require approved backend workflows and audit before they can be enabled.</p>
  </div>
}
