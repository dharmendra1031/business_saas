import { useEffect, useState, type FormEvent } from 'react'
import { api, ApiResponseError, SessionExpiredError, UncertainWriteError } from './api'
import { ActionButton, useActionFeedback } from './ActionFeedback'

interface ReceiptSettings {
  paperSize: 'Thermal80' | 'A4'
  showTime: boolean
  showUnitPrice: boolean
  showCashReceived: boolean
  showChange: boolean
  showBranch: boolean
  showSku: boolean
  showUnit: boolean
  showFooter: boolean
  legalName: string
  showLegalName: boolean
  address: string
  showAddress: boolean
  phone: string
  showPhone: boolean
  email: string
  showEmail: boolean
  footerText: string
  revision: number
  mandatoryFields: string[]
  rendererAvailable: boolean
}

const options = [
  { key: 'showTime', label: 'Time of sale', help: 'Receipt date always remains visible.' },
  { key: 'showUnitPrice', label: 'Price per unit', help: 'Line quantities and amounts always remain visible.' },
  { key: 'showCashReceived', label: 'Cash received', help: 'Only for settled cash receipts.' },
  { key: 'showChange', label: 'Change returned', help: 'Total payable always remains visible.' },
  { key: 'showBranch', label: 'Branch / location', help: 'Shows the location recorded at checkout.' },
  { key: 'showSku', label: 'Product SKU', help: 'Shows the SKU recorded at checkout.' },
  { key: 'showUnit', label: 'Unit of measure', help: 'Shows the unit recorded at checkout.' },
  { key: 'showLegalName', label: 'Legal business name', help: 'Shows the value saved when the sale was posted.' },
  { key: 'showAddress', label: 'Business address', help: 'Shows the value saved when the sale was posted.' },
  { key: 'showPhone', label: 'Business phone', help: 'Shows the value saved when the sale was posted.' },
  { key: 'showEmail', label: 'Business email', help: 'Shows the value saved when the sale was posted.' },
  { key: 'showFooter', label: 'Footer message', help: 'Show your approved receipt footer.' }
] as const

export default function BusinessReceiptSettings({ businessId, canOperate }: {
  businessId: number; canOperate: boolean
}) {
  const { run } = useActionFeedback()
  const [saved, setSaved] = useState<ReceiptSettings | null>(null)
  const [draft, setDraft] = useState<ReceiptSettings | null>(null)
  const [reason, setReason] = useState('')
  const [loadingError, setLoadingError] = useState('')
  const [reload, setReload] = useState(0)
  const [needsRefresh, setNeedsRefresh] = useState(false)
  const [busy, setBusy] = useState(false)

  useEffect(() => {
    let active = true
    setSaved(null)
    setDraft(null)
    setLoadingError('')
    setNeedsRefresh(false)
    api<ReceiptSettings>(`/businesses/${businessId}/receipt-settings`)
      .then(data => {
        if (!active) return
        setSaved(data)
        setDraft(data)
      })
      .catch(error => {
        if (active && !(error instanceof SessionExpiredError))
          setLoadingError(error instanceof Error ? error.message : 'Unable to load print settings.')
      })
    return () => { active = false }
  }, [businessId, reload])

  useEffect(() => { setReason('') }, [businessId])

  const changed = !!saved && !!draft && (
    saved.paperSize !== draft.paperSize || saved.footerText !== draft.footerText ||
    saved.legalName !== draft.legalName ||
    saved.address !== draft.address ||
    saved.phone !== draft.phone ||
    saved.email !== draft.email ||
    options.some(option => saved[option.key] !== draft[option.key])
  )

  async function save(event: FormEvent) {
    event.preventDefault()
    if (!canOperate || !draft || !saved || !changed || busy || needsRefresh ||
        reason.trim().length < 10) return
    setBusy(true)
    try {
      const result = await run('Saving invoice print preferences…',
        'Print preferences saved. RDLC printing remains inactive until its renderer is verified.',
        () => api<{ settings: ReceiptSettings }>(`/businesses/${businessId}/receipt-settings`, {
          method: 'PUT',
          body: JSON.stringify({
            paperSize: draft.paperSize,
            showTime: draft.showTime,
            showUnitPrice: draft.showUnitPrice,
            showCashReceived: draft.showCashReceived,
            showChange: draft.showChange,
            showBranch: draft.showBranch,
            showSku: draft.showSku,
            showUnit: draft.showUnit,
            showFooter: draft.showFooter,
            legalName: draft.legalName,
            showLegalName: draft.showLegalName,
            address: draft.address,
            showAddress: draft.showAddress,
            phone: draft.phone,
            showPhone: draft.showPhone,
            email: draft.email,
            showEmail: draft.showEmail,
            footerText: draft.footerText,
            expectedRevision: saved.revision,
            reason: reason.trim()
          })
        }))
      setSaved(result.settings)
      setDraft(result.settings)
      setReason('')
    } catch (cause) {
      if (cause instanceof UncertainWriteError ||
          (cause instanceof ApiResponseError && cause.status === 409))
        setNeedsRefresh(true)
      // The shared feedback popup owns mutation error handling.
    } finally {
      setBusy(false)
    }
  }

  function change<K extends keyof ReceiptSettings>(key: K, value: ReceiptSettings[K]) {
    setDraft(current => current ? { ...current, [key]: value } : current)
  }

  return <form aria-label="Invoice print settings" aria-busy={busy}
    onSubmit={event => void save(event)}>
    {loadingError && <div className="alert alert-danger" role="alert">
      {loadingError} <button type="button" className="btn btn-outline-secondary btn-sm"
        onClick={() => setReload(value => value + 1)}>Retry loading</button>
    </div>}
    {!draft ? !loadingError && <p role="status">Loading invoice print settings…</p> : <>
      <p className="small text-body-secondary">
        These options are saved per business. A developer maintains the fixed RDLC layouts.
        No RDLC markup is generated or edited by users.
      </p>
      {!draft.rendererAvailable && <div className="alert alert-warning" role="status">
        RDLC render/print adapter is not connected yet. Saving preferences does not change
        the existing browser-printed receipt.
      </div>}
      {needsRefresh && <div className="alert alert-warning" role="alert">
        The save may have completed or settings changed elsewhere. Reload before another update.
        <button type="button" className="btn btn-outline-secondary btn-sm ms-2"
          disabled={busy} onClick={() => setReload(v => v + 1)}>Reload saved settings</button>
      </div>}
      <fieldset disabled={!canOperate || busy || needsRefresh}>
        <legend className="h6">Paper and layout</legend>
        <label htmlFor="receipt-paper-size" className="form-label">Print template</label>
        <select id="receipt-paper-size" className="form-select mb-3"
          value={draft.paperSize} onChange={event =>
            change('paperSize', event.target.value as ReceiptSettings['paperSize'])}>
          <option value="Thermal80">80mm compact receipt</option>
          <option value="A4">A4 retail receipt</option>
        </select>
        <p className="small text-body-secondary">
          A4 is a retail receipt layout, not a released statutory GST tax invoice.
        </p>

        <fieldset className="mb-3">
          <legend className="h6">Receipt merchant details</legend>
          <p className="small text-body-secondary">Optional details are recorded on new cash sales. Existing receipts are not rewritten.</p>
          <label htmlFor="receipt-legalName" className="form-label">Legal business name</label>
          <input id="receipt-legalName" className="form-control mb-2" maxLength={150}
            value={draft.legalName} onChange={event => change('legalName', event.target.value)} />
          <label htmlFor="receipt-address" className="form-label">Business address</label>
          <input id="receipt-address" className="form-control mb-2" maxLength={300}
            value={draft.address} onChange={event => change('address', event.target.value)} />
          <label htmlFor="receipt-phone" className="form-label">Business phone</label>
          <input id="receipt-phone" className="form-control mb-2" type="tel" maxLength={25}
            value={draft.phone} onChange={event => change('phone', event.target.value)} />
          <label htmlFor="receipt-email" className="form-label">Business email</label>
          <input id="receipt-email" className="form-control mb-2" type="email" maxLength={254}
            value={draft.email} onChange={event => change('email', event.target.value)} />
        </fieldset>
        <fieldset className="mb-3">
          <legend className="h6">Optional fields to show</legend>
          {options.map(option => <div className="form-check mb-2" key={option.key}>
            <input id={`receipt-${option.key}`} className="form-check-input" type="checkbox"
              checked={draft[option.key]}
              onChange={event => change(option.key, event.target.checked)} />
            <label htmlFor={`receipt-${option.key}`} className="form-check-label">
              {option.label}
              <span className="d-block small text-body-secondary">{option.help}</span>
            </label>
          </div>)}
        </fieldset>
        <label htmlFor="receipt-footer-text" className="form-label">Footer text (up to 120 characters)</label>
        <input id="receipt-footer-text" className="form-control mb-3"
          maxLength={120} value={draft.footerText}
          onChange={event => change('footerText', event.target.value)} />
        <p className="small text-body-secondary">
          Always shown: {draft.mandatoryFields.join(', ')}.
        </p>
        <label htmlFor="receipt-settings-reason" className="form-label">
          Reason for settings change
        </label>
        <textarea id="receipt-settings-reason" className="form-control mb-3"
          rows={2} minLength={10} maxLength={200} required value={reason}
          onChange={event => setReason(event.target.value)}
          placeholder="Why is the printed invoice layout changing?" />
        <ActionButton busy={busy}
          disabled={!changed || needsRefresh || reason.trim().length < 10}>Save print settings</ActionButton>
      </fieldset>
      {!canOperate && <p className="small text-body-secondary">
        Your subscription is inactive. Print settings are read-only.
      </p>}
    </>}
  </form>
}
