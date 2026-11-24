import { useState } from 'react'
import { post } from './api'

export interface Receipt {
  id: number
  total: number
  tendered: number
  currencyCode: string
  postedAtUtc: string
  printAvailable?: boolean
  businessName?: string | null
  locationName?: string | null
  customerId?: number | null
  customerName?: string | null
  timeZoneId?: string | null
  lines: { id: number; productName: string; unitPrice: number; quantity: number;
    productSku?: string | null; unitCode?: string | null; lineTotal?: number | null }[]
}

function ReturnEstimate({ receipt, path, locationId }: { receipt: Receipt; path: string; locationId: number }) {
  const [quantities, setQuantities] = useState<Record<number, string>>({})
  const [estimate, setEstimate] = useState<{ total: number; currencyCode: string; lines: { lineId: number; amount: number }[] } | null>(null)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  function change(next: Record<number, string>) {
    setQuantities(next); setEstimate(null); setError('')
  }
  const selected = receipt.lines.filter(line => (quantities[line.id] ?? '') !== '' && Number(quantities[line.id]) !== 0)
  const valid = selected.length > 0 && selected.every(line => {
    const value = Number(quantities[line.id])
    return Number.isFinite(value) && value > 0 && value <= line.quantity &&
      Math.abs(Math.round(value * 1000) - value * 1000) < 0.000001
  })
  return <section className="border rounded p-2 mb-3" aria-label="Return estimate">
    <h3 className="h6">Return estimate</h3>
    <p className="small mb-2">Preview only. No refund or stock change is recorded.</p>
    <button type="button" className="btn btn-outline-secondary btn-sm mb-2" disabled={busy}
      onClick={() => change(Object.fromEntries(receipt.lines.map(line => [line.id, String(line.quantity)])))}>
      Select full quantity
    </button>
    <form onSubmit={async event => {
      event.preventDefault()
      if (!valid || busy) return
      setBusy(true); setError(''); setEstimate(null)
      try {
        const result = await post<{ total: number; currencyCode: string; lines: { lineId: number; amount: number }[] }>(
          path, { locationId, lines: selected.map(line => ({ lineId: line.id, quantity: Number(quantities[line.id]) })) })
        setEstimate(result)
      } catch (cause) {
        setError(cause instanceof Error ? cause.message : 'Unable to calculate return estimate.')
      } finally { setBusy(false) }
    }}>
      <div className="table-responsive"><table className="table table-sm align-middle">
        <thead><tr><th scope="col">Product</th><th scope="col">Return quantity</th><th scope="col">Estimated amount</th></tr></thead>
        <tbody>{receipt.lines.map(line => <tr key={line.id}>
          <td>{line.productName} <small className="d-block">Sold: {line.quantity} {line.unitCode}</small></td>
          <td><input type="number" className="form-control form-control-sm" min="0" max={line.quantity} step="0.001"
            aria-label={`Return quantity for ${line.productName}`} disabled={busy} value={quantities[line.id] ?? ''}
            onChange={event => change({ ...quantities, [line.id]: event.target.value })} /></td>
          <td>{estimate?.lines.some(item => item.lineId === line.id)
            ? estimate.currencyCode + ' ' + estimate.lines.find(item => item.lineId === line.id)!.amount.toFixed(2) : '—'}</td>
        </tr>)}</tbody>
      </table></div>
      {error && <p role="alert" className="text-danger">{error}</p>}
      {estimate && <p role="status"><strong>Estimated return amount: {estimate.currencyCode} {estimate.total.toFixed(2)}</strong></p>}
      <button type="submit" className="btn btn-outline-primary btn-sm" disabled={!valid || busy}>
        {busy ? 'Calculating…' : 'Calculate estimate'}
      </button>
    </form>
  </section>
}

export default function CashReceipt({ receipt, businessName, timeZone, pdfPath, printNotice, backLabel, onBack, returnEstimate }: {
  receipt: Receipt
  businessName: string
  timeZone: string
  pdfPath: string
  printNotice?: string
  backLabel: string
  onBack: () => void
  returnEstimate?: { path: string; locationId: number }
}) {
  const [showEstimate, setShowEstimate] = useState(false)
  const money = (amount: number) => receipt.currencyCode + ' ' + amount.toFixed(2)
  return <section className="content-section" aria-label="Cash receipt">
    <h2>Cash receipt #{receipt.id}</h2>
    <p>{receipt.businessName ?? businessName} · {new Date(receipt.postedAtUtc).toLocaleString(undefined, { timeZone: receipt.timeZoneId ?? timeZone })}</p>
    <p className="small mb-2">
      {receipt.locationName && <>Location: <strong>{receipt.locationName}</strong> · </>}
      Customer: <strong>{receipt.customerName || (receipt.customerId ? `Customer #${receipt.customerId}` : 'Walk-in')}</strong>
    </p>
    <div className="table-responsive">
      <table className="table table-sm align-middle" aria-label="Original posted sale lines">
        <thead><tr>
          <th scope="col">Product / SKU</th>
          <th scope="col" className="text-end">Sold quantity</th>
          <th scope="col" className="text-end">Rate</th>
          <th scope="col" className="text-end">Posted amount</th>
        </tr></thead>
        <tbody>{receipt.lines.map(line => <tr key={line.id}>
          <td><strong>{line.productName}</strong>
            {line.productSku && <small className="d-block">{line.productSku}</small>}</td>
          <td className="text-end">{line.quantity}{line.unitCode ? ' ' + line.unitCode : ''}</td>
          <td className="text-end">{money(line.unitPrice)}</td>
          <td className="text-end">{line.lineTotal == null ? '—' : money(line.lineTotal)}</td>
        </tr>)}</tbody>
      </table>
    </div>
    <p><strong>Total {money(receipt.total)}</strong><br />Cash received {money(receipt.tendered)}<br />
      Change {money(receipt.tendered - receipt.total)}</p>
    {printNotice && <p role="status" className="small text-body-secondary">{printNotice}</p>}
    {receipt.printAvailable
      ? <a className="btn btn-outline-secondary me-2" href={pdfPath} target="_blank" rel="noopener noreferrer">Print receipt</a>
      : <p className="small text-body-secondary" role="status">RDLC printing requires a Windows report host.</p>}
    {returnEstimate && <button type="button" className="btn btn-outline-secondary me-2" aria-expanded={showEstimate}
      onClick={() => setShowEstimate(value => !value)}>{showEstimate ? 'Close estimate' : 'Estimate return'}</button>}
    <button type="button" className="btn btn-primary" onClick={onBack}>{backLabel}</button>
    {returnEstimate && showEstimate && <div className="mt-3"><ReturnEstimate receipt={receipt}
      path={returnEstimate.path} locationId={returnEstimate.locationId} /></div>}
  </section>
}
