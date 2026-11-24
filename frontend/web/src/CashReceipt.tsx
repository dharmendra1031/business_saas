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

export default function CashReceipt({ receipt, businessName, timeZone, pdfPath, printNotice, backLabel, onBack }: {
  receipt: Receipt
  businessName: string
  timeZone: string
  pdfPath: string
  printNotice?: string
  backLabel: string
  onBack: () => void
}) {
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
    <button type="button" className="btn btn-primary" onClick={onBack}>{backLabel}</button>
  </section>
}
