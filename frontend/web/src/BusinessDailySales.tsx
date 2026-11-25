import { useEffect, useState } from 'react'
import { MasterLookup } from './LookupDialog'
import { api, SessionExpiredError, type RetailDashboard } from './api'

interface Report {
  date: string
  timeZoneId: string
  totals: { currencyCode: string; receiptCount: number; salesTotal: number;
    returnCount: number; refundTotal: number; netSales: number }[]
}

export default function BusinessDailySales({ dashboard }: { dashboard: RetailDashboard }) {
  const { business, locations } = dashboard
  const [locationId, setLocationId] = useState(locations[0]?.id ?? 0)
  const [date, setDate] = useState(() => {
    const parts = new Intl.DateTimeFormat('en', {
      timeZone: business.timeZoneId, year: 'numeric', month: '2-digit', day: '2-digit'
    }).formatToParts(new Date())
    return ['year', 'month', 'day'].map(part => parts.find(x => x.type === part)?.value).join('-')
  })
  const [report, setReport] = useState<Report | null>(null)
  const [error, setError] = useState('')
  const [loading, setLoading] = useState(false)
  const [reload, setReload] = useState(0)

  useEffect(() => {
    if (!locationId || !date) return
    let active = true
    setReport(null)
    setError('')
    setLoading(true)
    const params = new URLSearchParams({ locationId: String(locationId), date })
    api<Report>(`/businesses/${business.id}/cash-sales/daily-report?${params}`)
      .then(value => { if (active) setReport(value) })
      .catch(cause => {
        if (active && !(cause instanceof SessionExpiredError)) setError('Unable to load daily cash sales.')
      }).finally(() => { if (active) setLoading(false) })
    return () => { active = false }
  }, [business.id, locationId, date, reload])

  return <>
    <div className="platform-search mb-3">
      <div><label className="form-label" htmlFor={locations.length > 1 ? 'daily-sales-location' : undefined}>Location</label>
        {locations.length === 1 ? <div className="fw-semibold">{locations[0].name}</div> :
          <MasterLookup id="daily-sales-location" title="Select report location" value={locationId}
            emptyLabel="Select location" options={locations} onChange={setLocationId} />}</div>
      <label>Business date<input className="form-control" type="date" value={date}
        min="1900-01-01" max="9998-12-31" onChange={event => setDate(event.target.value)} /></label>
    </div>
    {!locationId ? <p>No authorized locations.</p> : !date ? <p>Choose a business date.</p> :
      loading ? <p role="status">Loading daily cash sales…</p> :
      error ? <div role="alert">{error}{' '}<button type="button" className="btn btn-outline-secondary btn-sm"
        onClick={() => setReload(value => value + 1)}>Retry</button></div> :
      report && <>
        <p>{report.date} · {report.timeZoneId}</p>
        {report.totals.length === 0 ? <p>No posted cash receipts or returns for this date and location.</p> :
          <div className="table-responsive"><table className="table table-sm">
            <thead><tr><th scope="col">Currency</th><th scope="col">Receipts</th><th scope="col">Returns</th>
              <th scope="col" className="text-end">Gross cash sales</th>
              <th scope="col" className="text-end">Cash refunds</th>
              <th scope="col" className="text-end">Net cash sales</th></tr></thead>
            <tbody>{report.totals.map(total => <tr key={total.currencyCode}>
              <td>{total.currencyCode}</td><td>{total.receiptCount.toLocaleString()}</td>
              <td>{total.returnCount.toLocaleString()}</td>
              <td className="text-end">{total.salesTotal.toFixed(2)}</td>
              <td className="text-end">{total.refundTotal.toFixed(2)}</td>
              <td className="text-end"><strong>{total.netSales.toFixed(2)}</strong></td>
            </tr>)}</tbody>
          </table></div>}
      </>}
  </>
}
