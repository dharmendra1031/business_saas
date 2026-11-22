import { useEffect, useState, type FormEvent } from 'react'
import { api, post, SessionExpiredError } from './api'
import PlatformOverview from './PlatformOverview'
import PlatformCustomers from './PlatformCustomers'
import PlatformAudit from './PlatformAudit'
import PlatformPlans from './PlatformPlans'
import PlatformOperations from './PlatformOperations'
import PlatformCreateBusiness from './PlatformCreateBusiness'
import PlatformModulePreview from './PlatformModulePreview'
import { Building2, Plus, Search } from 'lucide-react'

export type PlatformSection = 'overview' | 'customers' | 'businesses' | 'plans' | 'licenses' | 'settings' | 'audit' | 'operations'

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
  statusBeforeSuspension: string | null
  currencyCode: string
  timeZoneId: string
  createdAtUtc: string
}

function statusLabel(status: string | null, trialEnds: string | null = null) {
  if (status === 'Trial' && trialEnds && new Date(trialEnds).getTime() <= Date.now())
    return 'Trial expired'
  return status ?? 'No subscription'
}

function statusStyle(status: string) {
  if (status === 'Active') return 'is-active'
  if (status === 'Trial') return 'is-trial'
  return 'is-muted'
}

function PlatformBusinesses({ selectedBusinessId, onBilling, startCreate, onCreateHandled, incomingSearch }: {
  selectedBusinessId: number | null
  onBilling: (business: BusinessRow) => void
  startCreate: boolean
  onCreateHandled: () => void
  incomingSearch: string
}) {
  const [search, setSearch] = useState(incomingSearch)
  const [term, setTerm] = useState(incomingSearch)
  const [page, setPage] = useState(1)
  const [reload, setReload] = useState(0)
  const [list, setList] = useState<BusinessPage | null>(null)
  const [details, setDetails] = useState<BusinessDetails | null>(null)
  const [error, setError] = useState('')
  const [loading, setLoading] = useState(true)
  const [detailLoading, setDetailLoading] = useState(false)
  const [extending, setExtending] = useState(false)
  const [extensionDays, setExtensionDays] = useState(7)
  const [extensionReason, setExtensionReason] = useState('')
  const [extensionBusy, setExtensionBusy] = useState(false)
  const [statusAction, setStatusAction] = useState<'Suspend' | 'Reactivate' | null>(null)
  const [statusReason, setStatusReason] = useState('')
  const [statusBusy, setStatusBusy] = useState(false)
  const [notice, setNotice] = useState('')
  const [creating, setCreating] = useState(startCreate)

  useEffect(() => { if (startCreate) setCreating(true) }, [startCreate])
  useEffect(() => {
    setSearch(incomingSearch)
    setTerm(incomingSearch)
    setPage(1)
    setDetails(null)
  }, [incomingSearch])

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
    setNotice('')
    setExtending(false)
    setStatusAction(null)
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

  async function extendTrial(event: FormEvent) {
    event.preventDefault()
    if (!details?.trialEndsAtUtc || details.subscriptionStatus !== 'Trial') return
    setError('')
    setExtensionBusy(true)
    try {
      const result = await post<{ trialEndsAtUtc: string; auditId: number }>(
        `/platform/businesses/${details.id}/extend-trial`, {
          additionalDays: extensionDays,
          expectedTrialEndsAtUtc: details.trialEndsAtUtc,
          reason: extensionReason.trim()
        })
      setDetails(current => current && current.id === details.id
        ? { ...current, trialEndsAtUtc: result.trialEndsAtUtc } : current)
      setExtending(false)
      setExtensionReason('')
      setNotice(`Trial extended successfully. Audit event #${result.auditId} recorded.`)
    } catch (cause) {
      if (!(cause instanceof SessionExpiredError)) {
        setError(cause instanceof Error ? cause.message : 'Unable to extend trial.')
      }
    } finally {
      setExtensionBusy(false)
    }
  }

  async function changeSubscriptionStatus(event: FormEvent) {
    event.preventDefault()
    if (!details || !statusAction || !details.subscriptionStatus) return
    setStatusBusy(true)
    setError('')
    try {
      const result = await post<{ status: string; auditId: number }>(
        `/platform/businesses/${details.id}/subscription-status`, {
          action: statusAction,
          expectedStatus: details.subscriptionStatus,
          reason: statusReason.trim()
        })
      setDetails(current => current && current.id === details.id
        ? {
            ...current,
            subscriptionStatus: result.status,
            statusBeforeSuspension: statusAction === 'Suspend'
              ? current.subscriptionStatus : null
          } : current)
      setStatusAction(null)
      setStatusReason('')
      setExtending(false)
      setNotice(`Subscription ${result.status.toLowerCase()}. Audit event #${result.auditId} recorded.`)
    } catch (cause) {
      if (!(cause instanceof SessionExpiredError))
        setError(cause instanceof Error ? cause.message :
          'Unable to change subscription status. Reload and try again.')
    } finally {
      setStatusBusy(false)
    }
  }

  useEffect(() => {
    if (selectedBusinessId !== null) void openBusiness(selectedBusinessId)
  }, [selectedBusinessId])

  if (creating) return <PlatformCreateBusiness onCancel={() => { setCreating(false); onCreateHandled() }}
    onCreated={id => {
      setCreating(false)
      onCreateHandled()
      setReload(value => value + 1)
      void openBusiness(id)
    }} />

  if (details) {
    return (
      <>
        <div className="page-header">
          <div>
            <button type="button" className="back-link" onClick={() => { setDetails(null); setError('') }}>
              All businesses
            </button>
            <h1>{details.name}</h1>
            <button type="button" className="btn btn-outline-primary" onClick={() => onBilling(details)}>Prepare subscription payment</button>
            <p>Platform subscription and business account controls. Shop operations remain private.</p>
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
            {details.subscriptionStatus === 'Suspended' && details.statusBeforeSuspension &&
              <div><dt>Previous status</dt><dd>{details.statusBeforeSuspension}</dd></div>}
            {details.trialEndsAtUtc && <div><dt>Trial end</dt>
              <dd>{new Date(details.trialEndsAtUtc).toLocaleDateString()}</dd></div>}
          </dl>
        </section>
        {notice && <div className="alert alert-success mt-3" role="status">{notice}</div>}
        {details.subscriptionStatus === 'Trial' && details.trialEndsAtUtc && <>
          <div className="mt-3 mb-2">
            <button type="button" className="btn btn-primary btn-sm"
              disabled={extensionBusy} onClick={() => { setExtending(true); setError('') }}>
              Extend Trial
            </button>
          </div>
          {extending && <section className="content-section mt-3" aria-label="Extend trial">
            <h2>Extend Trial — {details.name}</h2>
            <p className="text-secondary small">
              Current expiry: {new Date(details.trialEndsAtUtc).toLocaleString()}.
              This change affects only this business and is recorded in the platform audit log.
            </p>
            <form onSubmit={event => void extendTrial(event)}>
              <div className="row g-3">
                <div className="col-12 col-md-4">
                  <label className="form-label" htmlFor="trial-extension-days">Additional days</label>
                  <input id="trial-extension-days" className="form-control" type="number"
                    min={1} max={30} required value={extensionDays}
                    onChange={event => setExtensionDays(Number(event.target.value))} />
                </div>
                <div className="col-12">
                  <label className="form-label" htmlFor="trial-extension-reason">
                    Reason for extension
                  </label>
                  <textarea id="trial-extension-reason" className="form-control"
                    rows={3} minLength={10} maxLength={200} required
                    value={extensionReason}
                    onChange={event => setExtensionReason(event.target.value)}
                    placeholder="Explain why this business needs more trial time" />
                </div>
              </div>
              <p className="small text-secondary mt-3">
                Estimated new expiry: {new Date(
                  Math.max(Date.now(), new Date(details.trialEndsAtUtc).getTime()) +
                  extensionDays * 86400000).toLocaleString()}.
                Maximum permitted expiry is 90 days from now.
              </p>
              {error && <div className="alert alert-danger" role="alert">{error}</div>}
              <div className="form-footer">
                <button type="button" className="btn btn-outline-secondary"
                  disabled={extensionBusy} onClick={() => { setExtending(false); setError('') }}>
                  Cancel
                </button>
                <button type="submit" className="btn btn-primary"
                  disabled={extensionBusy || extensionDays < 1 || extensionDays > 30 ||
                    extensionReason.trim().length < 10}>
                  {extensionBusy ? 'Saving…' : 'Confirm Trial Extension'}
                </button>
              </div>
            </form>
          </section>}
        </>}
        {details.subscriptionStatus && ['Trial', 'Active', 'Suspended']
          .includes(details.subscriptionStatus) && <>
          <div className="d-flex gap-2 flex-wrap mt-3">
            <button type="button" className={details.subscriptionStatus === 'Suspended'
              ? 'btn btn-primary btn-sm' : 'btn btn-outline-danger btn-sm'}
              disabled={statusBusy || extensionBusy}
              onClick={() => {
                setStatusAction(details.subscriptionStatus === 'Suspended'
                  ? 'Reactivate' : 'Suspend')
                setStatusReason('')
                setExtending(false)
                setError('')
              }}>
              {details.subscriptionStatus === 'Suspended' ? 'Reactivate Subscription' : 'Suspend Subscription'}
            </button>
            <button type="button" className="btn btn-outline-secondary btn-sm"
              disabled={statusBusy || extensionBusy} onClick={() => void openBusiness(details.id)}>
              Reload Details
            </button>
          </div>
          {statusAction && <section className="content-section mt-3" aria-label="Confirm subscription status change">
            <h2>{statusAction} Subscription — {details.name}</h2>
            <p className="small text-secondary">
              {statusAction === 'Suspend'
                ? 'Suspension blocks business operations without deleting any business data.'
                : `Reactivation restores the previous ${details.statusBeforeSuspension ?? 'subscription'} status. Expired trials remain expired.`}
              {' '}Current status: {details.subscriptionStatus}. The action is audited and cannot be undone without a separate confirmed action.
            </p>
            <form onSubmit={event => void changeSubscriptionStatus(event)}>
              <label className="form-label" htmlFor="subscription-action-reason">
                Reason for {statusAction.toLowerCase()}
              </label>
              <textarea id="subscription-action-reason" className="form-control"
                rows={3} minLength={10} maxLength={200} required
                value={statusReason}
                onChange={event => setStatusReason(event.target.value)}
                placeholder="Explain the reason for this subscription change" />
              {error && <div className="alert alert-danger mt-3" role="alert">{error}</div>}
              <div className="form-footer">
                <button type="button" className="btn btn-outline-secondary"
                  disabled={statusBusy} onClick={() => { setStatusAction(null); setError('') }}>Cancel</button>
                <button type="submit" className={statusAction === 'Suspend'
                  ? 'btn btn-danger' : 'btn btn-primary'}
                  disabled={statusBusy || statusReason.trim().length < 10}>
                  {statusBusy ? 'Saving…' : `Confirm ${statusAction}`}
                </button>
              </div>
            </form>
          </section>}
        </>}
        <p className="text-secondary small mt-3">
          Plan assignment, payment activation and business-type changes are not available yet.
        </p>
      </>
    )
  }

  return (
    <div className="platform-business-directory">
      <div className="page-header">
        <div>
          <span className="platform-kicker">PLATFORM MANAGEMENT</span>
          <h1>Businesses</h1>
          <p>View registered businesses, verified owners, plans and subscription status.</p>
        </div>
        <button type="button" className="btn btn-primary" onClick={() => { setError(''); setCreating(true) }}>
          <Plus size={18} /> Create Business
        </button>
      </div>

      <section className="content-section platform-directory-card" aria-label="Business directory">
        <div className="platform-directory-heading">
          <div><h2>Business Directory</h2>
            <p>{list && !loading ? `${list.total.toLocaleString()} businesses` : 'Authorized platform records'}</p>
          </div>
          <span className="platform-data-tag">Server-paginated</span>
        </div>
        <form className="platform-search platform-directory-search" role="search" onSubmit={applySearch}>
          <div className="platform-directory-search-field">
            <label htmlFor="platform-search" className="visually-hidden">Search business name</label>
            <Search size={18} aria-hidden="true" />
            <input id="platform-search" className="form-control" type="search" maxLength={100}
              placeholder="Search business by name" value={search}
              onChange={event => setSearch(event.target.value)} />
          </div>
          <button type="submit" className="btn btn-primary">Search</button>
          {(term || search) && <button type="button" className="btn btn-outline-secondary"
            onClick={() => { setSearch(''); setTerm(''); setPage(1) }}>Clear</button>}
        </form>

        {error && <div className="alert alert-danger" role="alert">{error}
          <button type="button" className="btn btn-outline-secondary btn-sm ms-2"
            onClick={() => setReload(value => value + 1)}>Retry</button>
        </div>}
        {detailLoading && <p role="status" className="text-secondary small">Opening business details…</p>}
        {loading ? <div className="platform-directory-loading" role="status">
          <span className="spinner-border spinner-border-sm" aria-hidden="true" /> Loading businesses…
        </div> : list?.total === 0 ? <div className="platform-directory-empty" role="status">
          <Building2 size={32} />
          <h3>{term ? 'No matching businesses' : 'No businesses created yet'}</h3>
          <p>{term ? 'Try a different business name or clear the search.' :
            'Create your first business using the Platform Owner workflow.'}</p>
        </div> : list && <>
          <div className="table-responsive">
            <table className="table table-sm platform-table platform-directory-table">
              <thead>
                <tr><th scope="col">Business</th><th scope="col">Owner</th>
                  <th scope="col">Type / Country</th><th scope="col">Plan</th>
                  <th scope="col">Status</th><th scope="col" className="text-end">Action</th></tr>
              </thead>
              <tbody>{list.items.map(item => {
                const status = statusLabel(item.subscriptionStatus, item.trialEndsAtUtc)
                return <tr key={item.id}>
                  <td><span className="platform-business-cell">
                    <span className="platform-business-symbol" aria-hidden="true"><Building2 size={19} /></span>
                    <span><strong>{item.name}</strong><small>Business #{item.id}</small></span>
                  </span></td>
                  <td>{item.ownerEmail}</td>
                  <td>{item.typeName}<small className="d-block text-secondary">{item.countryCode}</small></td>
                  <td><span className="platform-plan-pill">{item.planCode ?? 'Not assigned'}</span></td>
                  <td><span className={`platform-state-pill ${statusStyle(status)}`}>{status}</span></td>
                  <td className="text-end"><button type="button"
                    className="btn btn-outline-secondary btn-sm" disabled={detailLoading}
                    aria-label={`View details for ${item.name}`}
                    onClick={() => void openBusiness(item.id)}>View</button></td>
                </tr>
              })}</tbody>
            </table>
          </div>
          <div className="platform-summary platform-directory-pagination">
            <p>{list.items.length ? `Showing ${(page - 1) * list.pageSize + 1}–${(page - 1) * list.pageSize + list.items.length} of ${list.total}`
              : `No businesses on page ${page}`}</p>
            <div className="platform-directory-pages">
              <button type="button" className="btn btn-outline-secondary btn-sm"
                disabled={page <= 1} onClick={() => setPage(value => value - 1)}>Previous</button>
              <span aria-label={`Current page ${page}`}>Page {page}</span>
              <button type="button" className="btn btn-outline-secondary btn-sm"
                disabled={page * list.pageSize >= list.total}
                onClick={() => setPage(value => value + 1)}>Next</button>
            </div>
          </div>
        </>}
      </section>
      <p className="platform-directory-note">Directory search currently uses the verified business-name API.
        Plan/status filters and creation dates will appear only when backed by accurate platform data.</p>
    </div>
  )
}

export default function PlatformAdmin({ section, selectedBusinessId, onViewBusiness, onBilling,
  billingBusiness, onChooseBusiness, onSeeBusinesses, onStartCreate, startCreate, onCreateHandled,
  businessSearch, onViewAudit }: {
  billingBusiness: { id: number; name: string } | null
  onBilling: (business: BusinessRow) => void
  onChooseBusiness: () => void
  section: PlatformSection
  selectedBusinessId: number | null
  onViewBusiness: (id: number) => void
  onSeeBusinesses: () => void
  onStartCreate: () => void
  startCreate: boolean
  onCreateHandled: () => void
  businessSearch: string
  onViewAudit: () => void
}) {
  if (section === 'overview') return <PlatformOverview onViewBusiness={onViewBusiness}
    onSeeBusinesses={onSeeBusinesses} onCreateBusiness={onStartCreate} />
  if (section === 'customers') return <PlatformCustomers onViewBusiness={onViewBusiness} />
  if (section === 'plans') return <PlatformPlans business={billingBusiness} onChooseBusiness={onChooseBusiness} />
  if (section === 'audit') return <PlatformAudit />
  if (section === 'operations') return <PlatformOperations onViewAudit={onViewAudit} />
  if (section === 'licenses' || section === 'settings') return <PlatformModulePreview module={section} />
  return <PlatformBusinesses onBilling={onBilling} selectedBusinessId={selectedBusinessId}
    startCreate={startCreate} onCreateHandled={onCreateHandled} incomingSearch={businessSearch} />
}
