import { useEffect, useState, type FormEvent } from 'react'
import { api, post, SessionExpiredError } from './api'

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

interface Plan {
  code: string
  name: string
  isPublic: boolean
  assignedBusinesses: number
  pricingConfigured: boolean
  draft: PlanDraft | null
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

function numberOrNull(value: string): number | null {
  return value.trim() === '' ? null : Number(value)
}

export default function PlatformPlans() {
  const [data, setData] = useState<PlanResponse | null>(null)
  const [loading, setLoading] = useState(true)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  const [notice, setNotice] = useState('')
  const [reload, setReload] = useState(0)
  const [editing, setEditing] = useState<Plan | null>(null)
  const [form, setForm] = useState<DraftForm>(formFromDraft(null))

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
    setEditing(plan)
    setForm(formFromDraft(plan.draft))
    setError('')
    setNotice('')
  }

  function update(field: keyof DraftForm, value: string) {
    setForm(current => ({ ...current, [field]: value }))
  }

  async function saveDraft(event: FormEvent) {
    event.preventDefault()
    if (!editing) return
    setBusy(true)
    setError('')
    try {
      const result = await post<{ revision: number; auditId: number }>(
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
        })
      setEditing(null)
      setNotice(`${editing.name} draft v${result.revision} saved. Audit #${result.auditId} recorded.`)
      setReload(value => value + 1)
    } catch (cause) {
      if (!(cause instanceof SessionExpiredError))
        setError(cause instanceof Error ? cause.message : 'Unable to save plan draft.')
    } finally {
      setBusy(false)
    }
  }

  return <>
    <div className="page-header">
      <div><h1>Subscription Plans</h1>
        <p>Manage draft commercial terms without changing any active business subscriptions.</p>
      </div>
      <button type="button" className="btn btn-outline-secondary btn-sm"
        disabled={loading || busy} onClick={() => setReload(value => value + 1)}>Refresh</button>
    </div>
    {notice && <div className="alert alert-success" role="status">{notice}</div>}
    {error && <div className="alert alert-danger" role="alert">{error}
      {!editing && <button type="button" className="btn btn-outline-secondary btn-sm ms-2"
        onClick={() => setReload(value => value + 1)}>Retry</button>}
    </div>}
    <section className="content-section">
      <h2>Plan Directory</h2>
      {loading ? <p role="status">Loading plans…</p> : data && <>
        <div className="table-responsive">
          <table className="table platform-table align-middle">
            <thead><tr><th>Plan</th><th>Visibility</th><th>Assigned Businesses</th>
              <th>Draft</th><th>Live Pricing</th><th className="text-end">Action</th></tr></thead>
            <tbody>{data.items.map(plan => <tr key={plan.code}>
              <td><strong>{plan.name}</strong>
                <small className="d-block text-secondary">{plan.code}</small></td>
              <td>{plan.isPublic ? 'Public plan' : 'Custom / Contact Sales'}</td>
              <td>{plan.assignedBusinesses.toLocaleString()}</td>
              <td>{plan.draft ? `Draft v${plan.draft.revision}` : 'Not created'}</td>
              <td>{plan.pricingConfigured ? 'Configured' : 'Not published'}</td>
              <td className="text-end">
                <button className="btn btn-outline-primary btn-sm" type="button"
                  onClick={() => openDraft(plan)}>Edit Draft</button>
              </td>
            </tr>)}</tbody>
          </table>
        </div>
        {data.unknownPlanAssignments > 0 && <div className="alert alert-warning">
          {data.unknownPlanAssignments} businesses have plan codes outside this catalog.
        </div>}
      </>}
    </section>

    {editing && <section className="content-section mt-3" aria-label="Edit plan draft">
      <h2>{editing.name} — Draft Configuration</h2>
      <p className="small text-secondary">
        {editing.draft ? `Editing draft v${editing.draft.revision}.` : 'New draft.'}
        {' '}All values are internal drafts only; they do not publish prices or enforce limits.
      </p>
      <form onSubmit={event => void saveDraft(event)}>
        <div className="form-grid">
          <div><label className="form-label" htmlFor="draft-currency">Currency (ISO code)</label>
            <input id="draft-currency" className="form-control" maxLength={3}
              value={form.currencyCode} onChange={event =>
                update('currencyCode', event.target.value.toUpperCase())}
              placeholder="INR" /></div>
          {([
            ['monthlyPrice', 'Monthly price'],
            ['quarterlyPrice', 'Quarterly price'],
            ['yearlyPrice', 'Yearly price']
          ] as const).map(([field, label]) => <div key={field}>
            <label className="form-label" htmlFor={field}>{label}</label>
            <input id={field} className="form-control" type="number" min={0}
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
            <input id={field} className="form-control" type="number" min={1}
              max={field === 'maxLocations' ? 1000 : field === 'maxUsers' ? 10000 : 1000000}
              step={1} value={form[field]}
              onChange={event => update(field, event.target.value)}
              placeholder="Not set" />
          </div>)}
          <div className="form-wide">
            <label className="form-label" htmlFor="draft-reason">Reason for change</label>
            <textarea id="draft-reason" className="form-control" rows={3}
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
          <button type="submit" className="btn btn-primary"
            disabled={busy || form.reason.trim().length < 10}>
            {busy ? 'Saving…' : 'Save Draft'}
          </button>
        </div>
      </form>
    </section>}
  </>
}
