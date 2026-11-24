import { useEffect, useState, type FormEvent } from 'react'
import { api, ApiResponseError, UncertainWriteError } from './api'
import { ActionButton, useActionFeedback } from './ActionFeedback'

interface BillingSettings {
  cashSalesEnabled: boolean
  creditSalesEnabled: boolean
  creditAvailable: boolean
  revision: number
  salesPaused: boolean
}

export default function BusinessBillingSettings({ businessId, canOperate, onSaved }: {
  businessId: number; canOperate: boolean; onSaved: () => void
}) {
  const { run } = useActionFeedback()
  const [settings, setSettings] = useState<BillingSettings | null>(null)
  const [cashEnabled, setCashEnabled] = useState(true)
  const [reason, setReason] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  const [reload, setReload] = useState(0)
  const [needsRefresh, setNeedsRefresh] = useState(false)
  const changed = !!settings && cashEnabled !== settings.cashSalesEnabled

  useEffect(() => {
    let active = true
    setSettings(null)
    setError('')
    setNeedsRefresh(false)
    api<BillingSettings>(`/businesses/${businessId}/billing-settings`)
      .then(result => {
        if (!active) return
        setSettings(result)
        setCashEnabled(result.cashSalesEnabled)
      })
      .catch(cause => { if (active) setError(cause instanceof Error ? cause.message : 'Unable to load settings.') })
    return () => { active = false }
  }, [businessId, reload])

  useEffect(() => { setReason('') }, [businessId])

  async function save(event: FormEvent) {
    event.preventDefault()
    if (!settings || !changed || busy || needsRefresh || !canOperate || reason.trim().length < 10) return
    setBusy(true)
    setError('')
    try {
      const result = await run('Saving payment settings…',
        'Billing preferences saved. New sales now follow this setting.',
        () => api<{ settings: BillingSettings }>(`/businesses/${businessId}/billing-settings`, {
          method: 'PUT',
          body: JSON.stringify({
            cashSalesEnabled: cashEnabled,
            creditSalesEnabled: false,
            expectedRevision: settings.revision,
            reason: reason.trim()
          })
        }))
      setSettings(result.settings)
      setCashEnabled(result.settings.cashSalesEnabled)
      setReason('')
      onSaved()
    } catch (cause) {
      // A stale revision or a lost write response must be reconciled before retry.
      // The shared popup reports the error; do not duplicate it in this form.
      if (cause instanceof UncertainWriteError ||
          (cause instanceof ApiResponseError && cause.status === 409))
        setNeedsRefresh(true)
    } finally {
      setBusy(false)
    }
  }

  return <form onSubmit={event => void save(event)} aria-label="Billing & Payments settings">
    {error && <div role="alert" className="alert alert-danger">
      {error}{' '}<button type="button" className="btn btn-link p-0"
        onClick={() => setReload(x => x + 1)}>Reload settings</button>
    </div>}
    {!settings ? !error && <p role="status">Loading saved payment preferences…</p> : <>
      {needsRefresh && <div role="status" className="alert alert-warning">
        The saved setting may have changed. Review the latest value before saving again.
        <button type="button" className="btn btn-outline-secondary btn-sm ms-2"
          onClick={() => setReload(value => value + 1)}>
          Reload settings (reset selection)
        </button>
      </div>}
      <div className="form-check form-switch mb-3">
        <input id="billing-cash-toggle" className="form-check-input" type="checkbox"
          checked={cashEnabled} disabled={!canOperate || busy}
          onChange={event => setCashEnabled(event.target.checked)} />
        <label htmlFor="billing-cash-toggle" className="form-check-label">
          <strong>Cash sales</strong>
          <span className="d-block small text-body-secondary">Allow new cash checkout at the POS. Existing receipts remain accessible.</span>
        </label>
      </div>
      <div className="form-check form-switch mb-3">
        <input id="billing-credit-toggle" className="form-check-input" type="checkbox"
          checked={settings.creditSalesEnabled} disabled
          aria-describedby="billing-credit-help" readOnly />
        <label htmlFor="billing-credit-toggle" className="form-check-label">
          <strong>Customer Credit / Udhar</strong>
        </label>
        <p id="billing-credit-help" className="small text-body-secondary mb-0">
          Under construction. Credit can be switched on only after customer balances, due dates,
          payment collections and financial ledger checks are released.
        </p>
      </div>
      {!cashEnabled && <p className="alert alert-warning" role="status">
        New checkout will be paused. You can switch Cash back on later; old receipts are retained.
      </p>}
      <label className="form-label" htmlFor="billing-change-reason">Reason for changing payment options</label>
      <textarea className="form-control mb-3" id="billing-change-reason" rows={2}
        maxLength={200} minLength={10} value={reason}
        onChange={event => setReason(event.target.value)} disabled={!canOperate || busy}
        placeholder="Why are you changing the store payment options?" />
      <ActionButton busy={busy} disabled={!changed || needsRefresh || !canOperate || reason.trim().length < 10}>
        Save payment settings
      </ActionButton>
      {!canOperate && <p className="small text-body-secondary mt-2">
        Changes are unavailable while your business subscription is inactive.
      </p>}
    </>}
  </form>
}
