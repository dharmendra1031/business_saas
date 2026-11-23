import { useEffect, useRef, useState, type FormEvent } from 'react'
import { api, post, SessionExpiredError, type BusinessType } from './api'
import PlatformOverview, { type OverviewSubscriptionFilter } from './PlatformOverview'
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
  directoryStatus?: string
  createdAtUtc?: string
  trialEndsAtUtc: string | null
}

interface BusinessPage {
  items: BusinessRow[]
  total: number
  page: number
  pageSize: number
}

interface BusinessDetails extends BusinessRow {
  accessStatus: string
  paidStartsAtUtc: string | null
  paidEndsAtUtc: string | null
  paidPlanVersionId: number | null
  paidPlanVersion: number | null
  locationCount: number
  primaryLocationName: string | null
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

const directoryDate = new Intl.DateTimeFormat(undefined, { dateStyle: 'medium', timeZone: 'UTC' })

function PlatformBusinesses({ selectedBusinessId, onBilling, startCreate, onCreateHandled, incomingSearch, incomingStatus, onViewAudit }: {
  selectedBusinessId: number | null
  onBilling: (business: BusinessRow) => void
  startCreate: boolean
  onCreateHandled: () => void
  incomingSearch: string
  incomingStatus: string
  onViewAudit: (id: number) => void
}) {
  const [search, setSearch] = useState(incomingSearch)
  const [term, setTerm] = useState(incomingSearch)
  const [ownerEmail, setOwnerEmail] = useState('')
  const [ownerTerm, setOwnerTerm] = useState('')
  const [typeId, setTypeId] = useState('')
  const [types, setTypes] = useState<BusinessType[]>([])
  const [typeLoadError, setTypeLoadError] = useState(false)
  const [plan, setPlan] = useState('')
  const [statusFilter, setStatusFilter] = useState(incomingStatus)
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
  const [notice, setNotice] = useState<{ message: string; auditId: number } | null>(null)
  const [creating, setCreating] = useState(startCreate)
  // Ignore responses from superseded detail requests (search, back, or another business).
  const detailRequestId = useRef(0)

  useEffect(() => {
    if (startCreate) {
      detailRequestId.current += 1
      setDetailLoading(false)
      setCreating(true)
    }
  }, [startCreate])
  useEffect(() => {
    detailRequestId.current += 1
    setDetailLoading(false)
    setSearch(incomingSearch)
    setTerm(incomingSearch)
    setOwnerEmail('')
    setOwnerTerm('')
    setTypeId('')
    setPlan('')
    setStatusFilter(incomingStatus)
    setPage(1)
    setDetails(null)
  }, [incomingSearch, incomingStatus])

  useEffect(() => {
    let active = true
    setLoading(true)
    setError('')

    const params = new URLSearchParams({
      page: String(page), search: term, ownerEmail: ownerTerm, planCode: plan, status: statusFilter
    })
    if (typeId) params.set('businessTypeId', typeId)
    api<BusinessPage>(`/platform/businesses?${params}`)
      .then(result => { if (active) setList(result) })
      .catch(cause => {
        if (!active || cause instanceof SessionExpiredError) return
        setList(null)
        setError('Unable to load businesses. Check your access or retry.')
      })
      .finally(() => { if (active) setLoading(false) })

    return () => { active = false }
  }, [term, ownerTerm, typeId, page, plan, statusFilter, reload])

  useEffect(() => {
    let active = true
    setTypeLoadError(false)
    api<BusinessType[]>('/platform/business-type-controls')
      .then(result => { if (active) setTypes(result) })
      .catch(cause => {
        if (active && !(cause instanceof SessionExpiredError)) setTypeLoadError(true)
      })
    return () => { active = false }
  }, [reload])

  function applySearch(event: FormEvent) {
    event.preventDefault()
    detailRequestId.current += 1
    setDetailLoading(false)
    setPage(1)
    setTerm(search.trim())
    setOwnerTerm(ownerEmail.trim())
    setDetails(null)
  }

  async function openBusiness(id: number) {
    const requestId = ++detailRequestId.current
    setError('')
    setNotice(null)
    setExtending(false)
    setStatusAction(null)
    setDetailLoading(true)
    try {
      const result = await api<BusinessDetails>(`/platform/businesses/${id}`)
      if (requestId === detailRequestId.current) setDetails(result)
    } catch (cause) {
      if (requestId === detailRequestId.current && !(cause instanceof SessionExpiredError))
        setError('Unable to open business details. Please retry.')
    } finally {
      if (requestId === detailRequestId.current) setDetailLoading(false)
    }
  }

  const validExtensionDays = Number.isInteger(extensionDays) && extensionDays >= 1 && extensionDays <= 30
  const trimmedExtensionReason = extensionReason.trim()
  const canExtendTrial = validExtensionDays && trimmedExtensionReason.length >= 10 &&
    trimmedExtensionReason.length <= 200
  const trimmedStatusReason = statusReason.trim()
  const validStatusReason = trimmedStatusReason.length >= 10 && trimmedStatusReason.length <= 200

  async function extendTrial(event: FormEvent) {
    event.preventDefault()
    if (extensionBusy || !canExtendTrial || !details?.trialEndsAtUtc || details.subscriptionStatus !== 'Trial') return
    setError('')
    setExtensionBusy(true)
    try {
      const result = await post<{ trialEndsAtUtc: string; auditId: number }>(
        `/platform/businesses/${details.id}/extend-trial`, {
          additionalDays: extensionDays,
          expectedTrialEndsAtUtc: details.trialEndsAtUtc,
          reason: trimmedExtensionReason
        })
      setDetails(current => current && current.id === details.id
        ? { ...current, trialEndsAtUtc: result.trialEndsAtUtc, accessStatus: 'Trial' } : current)
      setExtending(false)
      setExtensionReason('')
      setNotice({ message: 'Trial extended successfully.', auditId: result.auditId })
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
    if (statusBusy || !validStatusReason || !details || !statusAction || !details.subscriptionStatus) return
    setStatusBusy(true)
    setError('')
    try {
      const result = await post<{ status: string; accessStatus: string; auditId: number }>(
        `/platform/businesses/${details.id}/subscription-status`, {
          action: statusAction,
          expectedStatus: details.subscriptionStatus,
          reason: trimmedStatusReason
        })
      setDetails(current => current && current.id === details.id
        ? {
            ...current,
            subscriptionStatus: result.status,
            accessStatus: result.accessStatus,
            statusBeforeSuspension: statusAction === 'Suspend'
              ? current.subscriptionStatus : null
          } : current)
      setStatusAction(null)
      setStatusReason('')
      setExtending(false)
      const outcome = result.status === 'Suspended' ? 'Subscription suspended.' :
        result.accessStatus === 'Active' || result.accessStatus === 'Trial'
          ? 'Subscription reactivated. Business access is restored.'
          : 'Suspension lifted. Business access remains unavailable outside the valid subscription period.'
      setNotice({ message: outcome, auditId: result.auditId })
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
    const subscriptionDate = new Intl.DateTimeFormat(undefined, {
      dateStyle: 'medium', timeStyle: 'short', timeZone: details.timeZoneId
    })
    return (
      <>
        <div className="page-header">
          <div>
            <button type="button" className="back-link" onClick={() => {
              detailRequestId.current += 1
              setDetailLoading(false)
              setDetails(null)
              setError('')
            }}>
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
            <div><dt>Locations</dt><dd>{details.locationCount.toLocaleString()}</dd></div>
            <div><dt>Primary location</dt><dd>{details.primaryLocationName ?? 'Not assigned'}</dd></div>
            <div><dt>Plan</dt><dd>{details.planCode ?? 'Not assigned'}</dd></div>
            {details.paidPlanVersionId != null && <div><dt>Paid plan version</dt>
              <dd>{details.paidPlanVersion ?? `#${details.paidPlanVersionId}`}</dd></div>}
            <div><dt>Subscription</dt><dd>{details.subscriptionStatus == null ? 'No subscription' :
              details.accessStatus === 'TrialExpired' ? 'Trial expired' :
              (details.accessStatus ?? statusLabel(details.subscriptionStatus, details.trialEndsAtUtc))}</dd></div>
            {details.subscriptionStatus === 'Suspended' && details.statusBeforeSuspension &&
              <div><dt>Previous status</dt><dd>{details.statusBeforeSuspension}</dd></div>}
            {details.trialEndsAtUtc && <div><dt>Trial end</dt>
              <dd>{subscriptionDate.format(new Date(details.trialEndsAtUtc))}</dd></div>}
            {details.paidStartsAtUtc && <div><dt>Paid period starts</dt>
              <dd>{subscriptionDate.format(new Date(details.paidStartsAtUtc))}</dd></div>}
            {details.paidEndsAtUtc && <div><dt>Paid period ends</dt>
              <dd>{subscriptionDate.format(new Date(details.paidEndsAtUtc))}</dd></div>}
          </dl>
        </section>
        {notice && <div className="alert alert-success mt-3">
          <span role="status">{notice.message} Audit event #{notice.auditId} recorded.</span>{' '}
          <button type="button" className="btn btn-link p-0" onClick={() => onViewAudit(notice.auditId)}>
            View audit event
          </button>
        </div>}
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
              Current expiry is {subscriptionDate.format(new Date(details.trialEndsAtUtc))} ({details.timeZoneId}).
              The extension is recorded in the platform audit log.
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
                {validExtensionDays &&
                  <>Estimated new expiry is {subscriptionDate.format(new Date(
                  Math.max(Date.now(), new Date(details.trialEndsAtUtc).getTime()) +
                  extensionDays * 86400000))} ({details.timeZoneId}).{' '}</>}
                Maximum permitted expiry is 90 days from now.
              </p>
              {error && <div className="alert alert-danger" role="alert">{error}</div>}
              <div className="form-footer">
                <button type="button" className="btn btn-outline-secondary"
                  disabled={extensionBusy} onClick={() => { setExtending(false); setError('') }}>
                  Cancel
                </button>
                <button type="submit" className="btn btn-primary"
                  disabled={extensionBusy || !canExtendTrial}>
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
                : 'Reactivation lifts the suspension. Business access requires a current trial or paid subscription period.'}
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
                  disabled={statusBusy || !validStatusReason}>
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

  const filtered = !!(term || ownerTerm || typeId || plan || statusFilter)
  return (
    <div className="platform-business-directory">
      <div className="page-header">
        <div>
          <span className="platform-kicker">PLATFORM MANAGEMENT</span>
          <h1>Businesses</h1>
          <p>View registered businesses, verified owners, plans and subscription status.</p>
        </div>
        <button type="button" className="btn btn-primary" onClick={() => {
          detailRequestId.current += 1
          setDetailLoading(false)
          setError('')
          setCreating(true)
        }}>
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
          <input className="form-control platform-directory-filter" type="search" aria-label="Filter by owner email"
            placeholder="Owner email" maxLength={254} value={ownerEmail}
            onChange={event => setOwnerEmail(event.target.value)} />
          <select className="form-select platform-directory-filter" aria-label="Filter by business type" value={typeId}
            disabled={types.length === 0}
            onChange={event => { setTypeId(event.target.value); setPage(1) }}>
            <option value="">All business types</option>
            {types.map(type => <option key={type.id} value={type.id}>{type.name}</option>)}
          </select>
          <select className="form-select platform-directory-filter" aria-label="Filter by plan" value={plan}
            onChange={event => { setPlan(event.target.value); setPage(1) }}>
            <option value="">All plans</option>
            <option value="STARTER">Starter</option>
            <option value="GROWTH">Growth</option>
            <option value="PROFESSIONAL">Professional</option>
            <option value="BUSINESS">Business</option>
            <option value="ENTERPRISE">Enterprise</option>
          </select>
          <select className="form-select platform-directory-filter" aria-label="Filter by subscription status" value={statusFilter}
            onChange={event => { setStatusFilter(event.target.value); setPage(1) }}>
            <option value="">All statuses</option>
            <option value="Trial">Trial</option>
            <option value="TrialExpired">Trial expired</option>
            <option value="Active">Active</option>
            <option value="Inactive">Inactive</option>
            <option value="Suspended">Suspended</option>
            <option value="None">No subscription</option>
          </select>
          <button type="submit" className="btn btn-primary">Search</button>
          {(filtered || search || ownerEmail) && <button type="button" className="btn btn-outline-secondary"
            onClick={() => {
              setSearch(''); setTerm(''); setOwnerEmail(''); setOwnerTerm(''); setTypeId('')
              setPlan(''); setStatusFilter(''); setPage(1)
            }}>Clear</button>}
        </form>

        {typeLoadError && <div className="alert alert-warning" role="alert">Unable to load business types.
          <button type="button" className="btn btn-outline-secondary btn-sm ms-2"
            onClick={() => setReload(value => value + 1)}>Retry business types</button>
        </div>}
        {error && <div className="alert alert-danger" role="alert">{error}
          <button type="button" className="btn btn-outline-secondary btn-sm ms-2"
            onClick={() => setReload(value => value + 1)}>Retry</button>
        </div>}
        {detailLoading && <p role="status" className="text-secondary small">Opening business details…</p>}
        {loading ? <div className="platform-directory-loading" role="status">
          <span className="spinner-border spinner-border-sm" aria-hidden="true" /> Loading businesses…
        </div> : list?.total === 0 ? <div className="platform-directory-empty" role="status">
          <Building2 size={32} />
          <h3>{filtered ? 'No matching businesses' : 'No businesses created yet'}</h3>
          <p>{filtered ? 'Try a different search or clear the filters.' :
            'Create your first business using the Platform Owner workflow.'}</p>
        </div> : list && <>
          <div className="table-responsive">
            <table className="table table-sm platform-table platform-directory-table">
              <thead>
                <tr><th scope="col">Business</th><th scope="col">Owner</th>
                  <th scope="col">Type / Country</th><th scope="col">Plan</th>
                  <th scope="col">Status</th><th scope="col">Created (UTC)</th>
                  <th scope="col" className="text-end">Action</th></tr>
              </thead>
              <tbody>{list.items.map(item => {
                const status = item.directoryStatus ?? statusLabel(item.subscriptionStatus, item.trialEndsAtUtc)
                return <tr key={item.id}>
                  <td><span className="platform-business-cell">
                    <span className="platform-business-symbol" aria-hidden="true"><Building2 size={19} /></span>
                    <span><strong>{item.name}</strong><small>Business #{item.id}</small></span>
                  </span></td>
                  <td>{item.ownerEmail}</td>
                  <td>{item.typeName}<small className="d-block text-secondary">{item.countryCode}</small></td>
                  <td><span className="platform-plan-pill">{item.planCode ?? 'Not assigned'}</span></td>
                  <td><span className={`platform-state-pill ${statusStyle(status)}`}>{status}</span></td>
                  <td>{item.createdAtUtc && Number.isFinite(Date.parse(item.createdAtUtc))
                    ? <time dateTime={item.createdAtUtc}>{directoryDate.format(new Date(item.createdAtUtc))}</time>
                    : '—'}</td>
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
    </div>
  )
}

export default function PlatformAdmin({ section, selectedBusinessId, onViewBusiness, onBilling,
  billingBusiness, onChooseBusiness, onSeeBusinesses, onStartCreate, startCreate, onCreateHandled,
  businessSearch, businessStatus, auditEventId, onViewAudit }: {
  billingBusiness: { id: number; name: string } | null
  onBilling: (business: BusinessRow) => void
  onChooseBusiness: () => void
  section: PlatformSection
  selectedBusinessId: number | null
  onViewBusiness: (id: number) => void
  onSeeBusinesses: (status?: OverviewSubscriptionFilter) => void
  onStartCreate: () => void
  startCreate: boolean
  onCreateHandled: () => void
  businessSearch: string
  businessStatus: string
  auditEventId: number | null
  onViewAudit: (id?: number) => void
}) {
  if (section === 'overview') return <PlatformOverview onViewBusiness={onViewBusiness}
    onSeeBusinesses={onSeeBusinesses} onCreateBusiness={onStartCreate} />
  if (section === 'customers') return <PlatformCustomers onViewBusiness={onViewBusiness} />
  if (section === 'plans') return <PlatformPlans business={billingBusiness} onChooseBusiness={onChooseBusiness} />
  if (section === 'audit') return <PlatformAudit key={auditEventId ?? 'all'}
    eventId={auditEventId} onFindEvent={onViewAudit} onClearEvent={() => onViewAudit()} />
  if (section === 'operations') return <PlatformOperations onViewAudit={onViewAudit} />
  if (section === 'licenses' || section === 'settings') return <PlatformModulePreview module={section} />
  return <PlatformBusinesses onBilling={onBilling} selectedBusinessId={selectedBusinessId}
    startCreate={startCreate} onCreateHandled={onCreateHandled}
    incomingSearch={businessSearch} incomingStatus={businessStatus} onViewAudit={onViewAudit} />
}
