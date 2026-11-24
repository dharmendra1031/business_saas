import { useEffect, useRef, useState, type FormEvent } from 'react'
import { api, post, SessionExpiredError } from './api'
import PlatformBilling from './PlatformBilling'
import { ActionButton, useActionFeedback } from './ActionFeedback'
import { BadgeCheck, Building2, CheckCircle2, CreditCard, RefreshCw, Store, Users } from 'lucide-react'

interface PlanDraft {
  id: number
  revision: number
  currencyCode: string | null
  monthlyPrice: number | null
  quarterlyPrice: number | null
  yearlyPrice: number | null
  maxLocations: number | null
  maxUsers: number | null
  maxProducts: number | null
  updatedAtUtc: string
}

interface PublishedVersion {
  id: number
  version: number
  sourceDraftRevision: number
  currencyCode: string
  monthlyPrice: number
  quarterlyPrice: number
  yearlyPrice: number
  maxLocations: number | null
  maxUsers: number | null
  maxProducts: number | null
  publishedAtUtc: string
}

interface Plan {
  code: string
  name: string
  isPublic: boolean
  assignedBusinesses: number
  pricingConfigured: boolean
  draft: PlanDraft | null
  published: PublishedVersion | null
}

interface PlanResponse {
  items: Plan[]
  unknownPlanAssignments: number
}

interface DraftForm {
  currencyCode: string
  monthlyPrice: string
  quarterlyPrice: string
  yearlyPrice: string
  maxLocations: string
  maxUsers: string
  maxProducts: string
  reason: string
}

function formFromDraft(draft: PlanDraft | null): DraftForm {
  return {
    currencyCode: draft?.currencyCode ?? '',
    monthlyPrice: draft?.monthlyPrice?.toString() ?? '',
    quarterlyPrice: draft?.quarterlyPrice?.toString() ?? '',
    yearlyPrice: draft?.yearlyPrice?.toString() ?? '',
    maxLocations: draft?.maxLocations?.toString() ?? '',
    maxUsers: draft?.maxUsers?.toString() ?? '',
    maxProducts: draft?.maxProducts?.toString() ?? '',
    reason: ''
  }
}

function planMoney(amount: number | null, currency: string | null): string {
  if (amount === null || !currency) return 'Not configured'
  try { return new Intl.NumberFormat('en-IN', { style: 'currency', currency, maximumFractionDigits: 2 }).format(amount) }
  catch { return `${currency} ${amount.toFixed(2)}` }
}

function numberOrNull(value: string): number | null {
  return value.trim() === '' ? null : Number(value)
}

export default function PlatformPlans({ business, onChooseBusiness }: { business: { id: number; name: string } | null; onChooseBusiness: () => void }) {
  const { run } = useActionFeedback()
  const [activeTab, setActiveTab] = useState<'catalog' | 'billing'>(business ? 'billing' : 'catalog')
  const [data, setData] = useState<PlanResponse | null>(null)
  const [loading, setLoading] = useState(true)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  const [reload, setReload] = useState(0)
  const [editing, setEditing] = useState<Plan | null>(null)
  const [publishing, setPublishing] = useState<Plan | null>(null)
  const [publishReason, setPublishReason] = useState('')
  const [form, setForm] = useState<DraftForm>(formFromDraft(null))
  const actionSection = useRef<HTMLElement | null>(null)

  useEffect(() => {
    if (editing || publishing) actionSection.current?.scrollIntoView({ behavior: 'smooth', block: 'start' })
  }, [editing, publishing])

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

  function openDraft(plan: Plan) {
    setPublishing(null)
    setEditing(plan)
    setForm(formFromDraft(plan.draft))
    setError('')
  }

  function openPublish(plan: Plan) {
    setEditing(null)
    setPublishing(plan)
    setPublishReason('')
    setError('')
  }

  async function publishPlan(event: FormEvent) {
    event.preventDefault()
    if (!publishing?.draft || !publishing.isPublic || busy) return
    setBusy(true)
    setError('')
    try {
      await run(`Publishing ${publishing.name} plan…`,
        (result: { version: number; auditId: number }) =>
          `${publishing.name} catalog version ${result.version} published. Audit #${result.auditId} recorded.`,
        () => post<{ version: number; auditId: number }>(
          `/platform/plans/${publishing.code}/publish`, {
            expectedDraftRevision: publishing.draft!.revision,
            expectedPublishedVersion: publishing.published?.version ?? 0,
            reason: publishReason.trim()
          }))
      setPublishing(null)
      setPublishReason('')
      setReload(value => value + 1)
    } catch {
      // Shared popup reports the server result; retain publication form.
    } finally {
      setBusy(false)
    }
  }

  function isPublishable(plan: Plan) {
    const draft = plan.draft
    return plan.isPublic && !!draft && !!draft.currencyCode &&
      draft.monthlyPrice !== null && draft.monthlyPrice > 0 &&
      draft.quarterlyPrice !== null && draft.quarterlyPrice > 0 &&
      draft.yearlyPrice !== null && draft.yearlyPrice > 0 &&
      draft.maxLocations !== null && draft.maxUsers !== null &&
      draft.maxProducts !== null &&
      draft.revision !== plan.published?.sourceDraftRevision
  }

  function update(field: keyof DraftForm, value: string) {
    setForm(current => ({ ...current, [field]: value }))
  }

  async function saveDraft(event: FormEvent) {
    event.preventDefault()
    if (!editing || busy) return
    setBusy(true)
    setError('')
    try {
      await run(`Saving ${editing.name} draft…`,
        (result: { revision: number; auditId: number }) =>
          `${editing.name} draft v${result.revision} saved. Audit #${result.auditId} recorded.`,
        () => post<{ revision: number; auditId: number }>(
          `/platform/plans/${editing.code}/draft`, {
            expectedRevision: editing.draft?.revision ?? 0,
            currencyCode: form.currencyCode.trim().toUpperCase() || null,
            monthlyPrice: numberOrNull(form.monthlyPrice),
            quarterlyPrice: numberOrNull(form.quarterlyPrice),
            yearlyPrice: numberOrNull(form.yearlyPrice),
            maxLocations: numberOrNull(form.maxLocations),
            maxUsers: numberOrNull(form.maxUsers),
            maxProducts: numberOrNull(form.maxProducts),
            reason: form.reason.trim()
          }))
      setEditing(null)
      setReload(value => value + 1)
    } catch {
      // Shared popup reports the server result; retain the unsaved draft.
    } finally {
      setBusy(false)
    }
  }

  return <>
    <div className="page-header">
      <div><span className="platform-kicker">PLATFORM MANAGEMENT</span><h1>Plans & Billing</h1>
        <p>Review real plan prices, publish audited versions and prepare internal billing drafts.</p>
      </div>
      <button type="button" className="btn btn-outline-secondary btn-sm"
        disabled={loading || busy} onClick={() => setReload(value => value + 1)}><RefreshCw size={15} /> Refresh</button>
    </div>
    {error && <div className="alert alert-danger" role="alert">{error}
      {!editing && !publishing && <button type="button" className="btn btn-outline-secondary btn-sm ms-2"
        onClick={() => setReload(value => value + 1)}>Retry</button>}
    </div>}
    <div className="platform-preview-tabs platform-plan-tabs" role="tablist" aria-label="Plans and billing">
      <button type="button" role="tab" disabled={busy} aria-selected={activeTab === 'catalog'}
        className={activeTab === 'catalog' ? 'is-active' : ''}
        onClick={() => { setEditing(null); setPublishing(null); setActiveTab('catalog') }}><BadgeCheck size={17} /> Plan Catalog</button>
      <button type="button" role="tab" disabled={busy} aria-selected={activeTab === 'billing'}
        className={activeTab === 'billing' ? 'is-active' : ''}
        onClick={() => { setEditing(null); setPublishing(null); setActiveTab('billing') }}>
        <CreditCard size={17} /> Billing Drafts
      </button>
    </div>
    {activeTab === 'billing' ? <PlatformBilling business={business} onChooseBusiness={onChooseBusiness} /> : <>
    <section className="content-section platform-plans-section">
      <div className="platform-plans-intro">
        <div><h2>Subscription Plans</h2>
          <p>Published prices, actual assigned-business counts and version history.</p></div>
        {data && !loading && <span className="platform-data-tag">{data.items.length} plan types</span>}
      </div>
      {loading ? <div className="platform-directory-loading" role="status">
        <span className="spinner-border spinner-border-sm" aria-hidden="true" /> Loading subscription plans…
      </div> : data && <>
        <div className="platform-plan-cards" aria-label="Subscription plan summary">
          {data.items.map(plan => {
            const published = plan.published
            return <article className="platform-plan-card" key={plan.code}>
              <div className="platform-plan-card-top">
                <span className="platform-plan-card-icon"><BadgeCheck size={20} /></span>
                <span className={`platform-state-pill ${published ? 'is-active' : 'is-muted'}`}>
                  {published ? `Published v${published.version}` : 'Not published'}
                </span>
              </div>
              <h3>{plan.name}</h3>
              <p>{plan.isPublic ? 'Public subscription plan' : 'Custom / contracted offering'}</p>
              <div className="platform-plan-card-price">
                <strong>{planMoney(published?.monthlyPrice ?? null, published?.currencyCode ?? null)}</strong>
                {published && <small>/ month</small>}
              </div>
              <div className="platform-plan-card-details">
                <div><Building2 size={15} /><span>Businesses</span><strong>{plan.assignedBusinesses.toLocaleString()}</strong></div>
                <div><Store size={15} /><span>Locations</span><strong>{published?.maxLocations ?? 'Not set'}</strong></div>
                <div><Users size={15} /><span>Users</span><strong>{published?.maxUsers ?? 'Not set'}</strong></div>
                <div><CheckCircle2 size={15} /><span>Draft</span><strong>{plan.draft ? `v${plan.draft.revision}` : 'None'}</strong></div>
              </div>
              <button type="button" className="btn btn-outline-secondary btn-sm w-100"
                disabled={busy} onClick={() => openDraft(plan)}>Configure Draft</button>
            </article>
          })}
        </div>
        <div className="platform-plan-table-heading">
          <h3>Plan Catalog & Versions</h3>
          <p>Draft edits and publishing use existing audited platform operations.</p>
        </div>
        <div className="table-responsive">
          <table className="table platform-table align-middle">
            <thead><tr><th>Plan</th><th>Visibility</th><th>Assigned Businesses</th>
              <th>Draft</th><th>Published Catalog</th><th className="text-end">Actions</th></tr></thead>
            <tbody>{data.items.map(plan => <tr key={plan.code}>
              <td><strong>{plan.name}</strong>
                <small className="d-block text-secondary">{plan.code}</small></td>
              <td>{plan.isPublic ? 'Public plan' : 'Custom / Contact Sales'}</td>
              <td>{plan.assignedBusinesses.toLocaleString()}</td>
              <td>{plan.draft ? `Draft v${plan.draft.revision}` : 'Not created'}</td>
              <td>{plan.published ? `Version ${plan.published.version}` : 'Not published'}</td>
              <td className="text-end">
                <div className="d-flex justify-content-end gap-2 flex-wrap">
                  <button className="btn btn-outline-primary btn-sm" type="button"
                    disabled={busy} onClick={() => openDraft(plan)}>Edit Draft</button>
                  {plan.isPublic && <button className="btn btn-primary btn-sm" type="button"
                    disabled={busy || !isPublishable(plan)}
                    title={isPublishable(plan) ? 'Review publication terms'
                      : 'Complete draft prices/limits or create a new revision first'}
                    onClick={() => openPublish(plan)}>Publish</button>}
                </div>
              </td>
            </tr>)}</tbody>
          </table>
        </div>
        {data.unknownPlanAssignments > 0 && <div className="alert alert-warning">
          {data.unknownPlanAssignments} businesses have plan codes outside this catalog.
        </div>}
      </>}
    </section>

    {publishing?.draft && <section ref={actionSection} className="content-section mt-3 platform-plan-action-panel" aria-label="Publish plan">
      <h2>Publish {publishing.name} — Version {(publishing.published?.version ?? 0) + 1}</h2>
      <p className="small text-secondary">
        Review draft v{publishing.draft.revision}. Publication freezes these terms as a new
        catalog version. Existing customers, entitlement rules and subscription charges are not changed.
      </p>
      <dl className="detail-list">
        <div><dt>Currency</dt><dd>{publishing.draft.currencyCode}</dd></div>
        <div><dt>Monthly</dt><dd>{publishing.draft.currencyCode} {publishing.draft.monthlyPrice}</dd></div>
        <div><dt>Quarterly</dt><dd>{publishing.draft.currencyCode} {publishing.draft.quarterlyPrice}</dd></div>
        <div><dt>Yearly</dt><dd>{publishing.draft.currencyCode} {publishing.draft.yearlyPrice}</dd></div>
        <div><dt>Max locations</dt><dd>{publishing.draft.maxLocations}</dd></div>
        <div><dt>Max users</dt><dd>{publishing.draft.maxUsers}</dd></div>
        <div><dt>Max products</dt><dd>{publishing.draft.maxProducts}</dd></div>
        <div><dt>Previous catalog version</dt>
          <dd>{publishing.published?.version ?? 'None'}</dd></div>
      </dl>
      <form onSubmit={event => void publishPlan(event)} className="mt-3">
        <label className="form-label" htmlFor="publish-reason">Approval reason</label>
        <textarea id="publish-reason" className="form-control" rows={3} disabled={busy}
          minLength={10} maxLength={200} required value={publishReason}
          onChange={event => setPublishReason(event.target.value)}
          placeholder="Confirm the commercial approval for these published terms" />
        <p className="small text-secondary mt-2">
          Publishing is recorded in Security &amp; Audit. No checkout, payment capture,
          paid activation or module entitlement is enabled by this action.
        </p>
        <div className="form-footer">
          <button type="button" className="btn btn-outline-secondary" disabled={busy}
            onClick={() => { setPublishing(null); setError('') }}>Cancel</button>
          <ActionButton busy={busy} busyLabel="Publishing…" disabled={publishReason.trim().length < 10}>
            Confirm Catalog Publication
          </ActionButton>
        </div>
      </form>
    </section>}

    <section className="content-section mt-3" aria-label="Subscription billing">
      <h2>Subscription Billing</h2>
      <p className="text-secondary mb-0">
        Captured gateway payments, processed refunds and subscription access changes are tracked
        in the billing reconciliation workflow. Invoice and credit-note drafts are internal
        documents only; statutory invoice issuance is not enabled. Publishing a plan
        alone does not charge a customer or activate access.
      </p>
    </section>

    {editing && <section ref={actionSection} className="content-section mt-3 platform-plan-action-panel" aria-label="Edit plan draft">
      <h2>{editing.name} — Draft Configuration</h2>
      <p className="small text-secondary">
        {editing.draft ? `Editing draft v${editing.draft.revision}.` : 'New draft.'}
        {' '}All values are internal drafts only; they do not publish prices or enforce limits.
      </p>
      <form onSubmit={event => void saveDraft(event)}>
        <div className="form-grid">
          <div><label className="form-label" htmlFor="draft-currency">Currency (ISO code)</label>
            <input id="draft-currency" className="form-control" maxLength={3} disabled={busy}
              value={form.currencyCode} onChange={event =>
                update('currencyCode', event.target.value.toUpperCase())}
              placeholder="INR" /></div>
          {([
            ['monthlyPrice', 'Monthly price'],
            ['quarterlyPrice', 'Quarterly price'],
            ['yearlyPrice', 'Yearly price']
          ] as const).map(([field, label]) => <div key={field}>
            <label className="form-label" htmlFor={field}>{label}</label>
            <input id={field} className="form-control" type="number" min={0} disabled={busy}
              max={99999999} step="0.01" value={form[field]}
              onChange={event => update(field, event.target.value)}
              placeholder="Not set" />
          </div>)}
          {([
            ['maxLocations', 'Max locations'],
            ['maxUsers', 'Max users'],
            ['maxProducts', 'Max products']
          ] as const).map(([field, label]) => <div key={field}>
            <label className="form-label" htmlFor={field}>{label}</label>
            <input id={field} className="form-control" type="number" min={1} disabled={busy}
              max={field === 'maxLocations' ? 1000 : field === 'maxUsers' ? 10000 : 1000000}
              step={1} value={form[field]}
              onChange={event => update(field, event.target.value)}
              placeholder="Not set" />
          </div>)}
          <div className="form-wide">
            <label className="form-label" htmlFor="draft-reason">Reason for change</label>
            <textarea id="draft-reason" className="form-control" rows={3} disabled={busy}
              minLength={10} maxLength={200} required value={form.reason}
              onChange={event => update('reason', event.target.value)}
              placeholder="Explain the reason for this draft revision" />
          </div>
        </div>
        <p className="small text-secondary mt-3">
          Blank limits are not enforced. Saving creates a new draft revision and audit record,
          not a live plan change.
        </p>
        <div className="form-footer">
          <button type="button" className="btn btn-outline-secondary"
            disabled={busy} onClick={() => { setEditing(null); setError('') }}>Cancel</button>
          <ActionButton busy={busy} disabled={form.reason.trim().length < 10}>
            Save Draft
          </ActionButton>
        </div>
      </form>
    </section>}
    </>}
  </>
}
