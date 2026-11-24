export interface Receipt {
  id: number
  total: number
  tendered: number
  currencyCode: string
  postedAtUtc: string
  printAvailable?: boolean
  businessName?: string | null
  timeZoneId?: string | null
  lines: { id: number; productName: string; unitPrice: number; quantity: number }[]
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
    <ul>{receipt.lines.map(line => <li key={line.id}>{line.productName} · {line.quantity} × {money(line.unitPrice)}</li>)}</ul>
    <p><strong>Total {money(receipt.total)}</strong><br />Cash received {money(receipt.tendered)}<br />
      Change {money(receipt.tendered - receipt.total)}</p>
    {printNotice && <p role="status" className="small text-body-secondary">{printNotice}</p>}
    {receipt.printAvailable
      ? <a className="btn btn-outline-secondary me-2" href={pdfPath} target="_blank" rel="noopener noreferrer">Print receipt</a>
      : <p className="small text-body-secondary" role="status">RDLC printing requires a Windows report host.</p>}
    <button type="button" className="btn btn-primary" onClick={onBack}>{backLabel}</button>
  </section>
}
