import { useEffect, useRef, useState, type FormEvent } from 'react'
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
  showCustomerName: boolean
  showCustomerPhone: boolean
  showCustomerAddress: boolean
  logo: string
  showLogo: boolean
  printCopies: number
  footerText: string
  revision: number
  mandatoryFields: string[]
  rendererAvailable: boolean
}

const options = [
  { group: 'Receipt details', key: 'showTime', label: 'Time of sale', help: 'Receipt date always remains visible.' },
  { group: 'Item details', key: 'showUnitPrice', label: 'Price per unit', help: 'Line quantities and amounts always remain visible.' },
  { group: 'Receipt details', key: 'showCashReceived', label: 'Cash received', help: 'Only for settled cash receipts.' },
  { group: 'Receipt details', key: 'showChange', label: 'Change returned', help: 'Total payable always remains visible.' },
  { group: 'Company details', key: 'showBranch', label: 'Branch / location', help: 'Shows the location recorded at checkout.' },
  { group: 'Item details', key: 'showSku', label: 'Product SKU', help: 'Shows the SKU recorded at checkout.' },
  { group: 'Item details', key: 'showUnit', label: 'Unit of measure', help: 'Shows the unit recorded at checkout.' },
  { group: 'Company details', key: 'showLegalName', label: 'Legal business name', help: 'Shows the value saved when the sale was posted.' },
  { group: 'Company details', key: 'showAddress', label: 'Business address', help: 'Shows the value saved when the sale was posted.' },
  { group: 'Company details', key: 'showPhone', label: 'Business phone', help: 'Shows the value saved when the sale was posted.' },
  { group: 'Company details', key: 'showEmail', label: 'Business email', help: 'Shows the value saved when the sale was posted.' },
  { group: 'Customer details', key: 'showCustomerName', label: 'Customer name', help: 'Shown only when saved on the selected customer receipt.' },
  { group: 'Customer details', key: 'showCustomerPhone', label: 'Customer phone', help: 'Shown only when saved on the selected customer receipt.' },
  { group: 'Customer details', key: 'showCustomerAddress', label: 'Customer address', help: 'Shown only when saved on the selected customer receipt.' },
  { group: 'Company details', key: 'showLogo', label: 'Company logo', help: 'Shows the logo saved when the sale was posted.' },
  { group: 'Receipt details', key: 'showFooter', label: 'Footer message', help: 'Show your approved receipt footer.' }
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
  const [logoBusy, setLogoBusy] = useState(false)
  const [logoError, setLogoError] = useState('')
  const logoRequest = useRef(0)

  useEffect(() => {
    let active = true
    logoRequest.current++
    setLogoBusy(false)
    setLogoError('')
    setSaved(null)
    setDraft(null)
    setLoadingError('')
    setNeedsRefresh(false)
    setReason('')
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
    return () => { active = false; logoRequest.current++ }
  }, [businessId, reload])

  const changed = !!saved && !!draft && (
    saved.paperSize !== draft.paperSize || saved.footerText !== draft.footerText ||
    saved.logo !== draft.logo || saved.printCopies !== draft.printCopies ||
    saved.legalName !== draft.legalName ||
    saved.address !== draft.address ||
    saved.phone !== draft.phone ||
    saved.email !== draft.email ||
    options.some(option => saved[option.key] !== draft[option.key])
  )

  async function save(event: FormEvent) {
    event.preventDefault()
    if (!canOperate || !draft || !saved || !changed || busy || logoBusy || needsRefresh ||
        reason.trim().length < 10) return
    setBusy(true)
    try {
      const result = await run('Saving invoice print preferences…',
        'Print preferences saved.',
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
            showCustomerName: draft.showCustomerName,
            showCustomerPhone: draft.showCustomerPhone,
            showCustomerAddress: draft.showCustomerAddress,
            // Omit unchanged logo bytes rather than resending them on every settings save.
            logo: draft.logo === saved.logo ? undefined : draft.logo,
            showLogo: draft.showLogo,
            printCopies: draft.printCopies,
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

  async function selectLogo(file: File) {
    const id = ++logoRequest.current
    setLogoBusy(true)
    setLogoError('')
    try {
      if (!['image/png', 'image/jpeg'].includes(file.type) || file.size > 2 * 1024 * 1024)
        throw new Error('Choose a PNG or JPEG image up to 2 MB.')
      const image = await createImageBitmap(file)
      try {
        const scale = Math.min(1, 384 / image.width, 192 / image.height)
        const canvas = document.createElement('canvas')
        canvas.width = Math.max(1, Math.round(image.width * scale))
        canvas.height = Math.max(1, Math.round(image.height * scale))
        const context = canvas.getContext('2d')
        if (!context) throw new Error('Image conversion is unavailable.')
        context.drawImage(image, 0, 0, canvas.width, canvas.height)
        const encoded = canvas.toDataURL('image/png').split(',')[1]
        if (!encoded || atob(encoded).length > 32768)
          throw new Error('Logo is too detailed. Choose a simpler image (32 KB after conversion).')
        if (id === logoRequest.current) change('logo', encoded)
      } finally { image.close() }
    } catch (error) {
      if (id === logoRequest.current)
        setLogoError(error instanceof Error ? error.message : 'Unable to read logo.')
    } finally { if (id === logoRequest.current) setLogoBusy(false) }
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
        RDLC render/print adapter is not connected yet on this host. Preferences are saved, but
        printing requires a Windows development report host.
      </div>}
      {needsRefresh && <div className="alert alert-warning" role="alert">
        The save may have completed or settings changed elsewhere. Reload before another update.
        <button type="button" className="btn btn-outline-secondary btn-sm ms-2"
          disabled={busy} onClick={() => setReload(v => v + 1)}>Reload saved settings</button>
      </div>}
      <fieldset disabled={!canOperate || busy || logoBusy || needsRefresh}>
        <legend className="h6">Paper and layout</legend>
        <label htmlFor="receipt-paper-size" className="form-label">Print template</label>
        <select id="receipt-paper-size" className="form-select mb-3"
          value={draft.paperSize} onChange={event =>
            change('paperSize', event.target.value as ReceiptSettings['paperSize'])}>
          <option value="Thermal80">80mm compact receipt</option>
          <option value="A4">A4 retail receipt</option>
        </select>
        <label htmlFor="receipt-copies" className="form-label">Print copies</label>
        <select id="receipt-copies" className="form-select mb-2" value={draft.printCopies}
          onChange={event => change('printCopies', Number(event.target.value))}>
          {[1, 2, 3, 4, 5].map(count => <option key={count} value={count}>{count}</option>)}
        </select>
        <p className="small text-body-secondary">Select the saved number of copies in the PDF print dialog.</p>
        <p className="small text-body-secondary">
          A4 is a retail receipt layout, not a released statutory GST tax invoice.
        </p>

        <fieldset className="mb-3">
          <legend className="h6">Receipt merchant details</legend>
          <p className="small text-body-secondary">Optional details are recorded on new cash sales. Existing receipts are not rewritten.</p>
          <label htmlFor="receipt-logo" className="form-label">Company logo (PNG or JPEG)</label>
          {draft.logo && <div className="mb-2 d-flex align-items-center gap-2">
            <img src={'data:image/png;base64,' + draft.logo} alt="Selected company logo"
              width={120} height={60} style={{ objectFit: 'contain' }} />
            <button type="button" className="btn btn-sm btn-outline-secondary"
              onClick={() => { change('logo', ''); setLogoError('') }}>Remove logo</button>
          </div>}
          <input id="receipt-logo" type="file" accept="image/png,image/jpeg" className="form-control mb-2"
            onChange={event => { const file = event.target.files?.[0]; event.target.value = ''; if (file) void selectLogo(file) }} />
          {logoBusy && <p role="status" className="small">Preparing logo…</p>}
          {logoError && <p role="alert" className="text-danger small">{logoError}</p>}
          <p className="small text-body-secondary">Optimized PNG, up to 384 × 192 pixels and 32 KB. Save to apply.</p>
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
          <div className="row g-3">
            {(['Company details', 'Customer details', 'Item details', 'Receipt details'] as const).map(group =>
              <div className="col-12 col-md-6" key={group}>
                <h3 className="h6">{group}</h3>
                {options.filter(option => option.group === group).map(option =>
                  <div className="form-check mb-2" key={option.key}>
                    <input id={`receipt-${option.key}`} className="form-check-input" type="checkbox"
                      checked={draft[option.key]}
                      onChange={event => change(option.key, event.target.checked)} />
                    <label htmlFor={`receipt-${option.key}`} className="form-check-label">
                      {option.label}
                      <span className="d-block small text-body-secondary">{option.help}</span>
                    </label>
                  </div>)}
              </div>)}
          </div>
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
          disabled={!changed || needsRefresh || logoBusy || reason.trim().length < 10}>Save print settings</ActionButton>
      </fieldset>
      {!canOperate && <p className="small text-body-secondary">
        Your subscription is inactive. Print settings are read-only.
      </p>}
    </>}
  </form>
}
