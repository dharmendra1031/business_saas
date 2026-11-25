import { useEffect, useState } from 'react'
import { api, SessionExpiredError } from './api'

interface Aging {
  currencyCode: string; outstanding: number; notDue: number; overdue: number;
  days1To30: number; days31To60: number; days61To90: number; daysOver90: number; invoiceCount: number
}
interface Report {
  businessDate: string; timeZoneId: string; totals: Aging[]; page: number; hasMore: boolean
  items: { customerId: number; name: string; isActive: boolean; currencyCode: string;
    outstanding: number; overdue: number; oldestDueDate: string; invoiceCount: number }[]
}
export default function BusinessOutstanding({ businessId }: { businessId: number }) {
  const [search, setSearch] = useState('')
  const [term, setTerm] = useState('')
  const [overdueOnly, setOverdueOnly] = useState(false)
  const [page, setPage] = useState(1)
  const [reload, setReload] = useState(0)
  const [report, setReport] = useState<Report | null>(null)
  const [error, setError] = useState('')
  useEffect(() => {
    let active = true
    const controller = new AbortController()
    setReport(null); setError('')
    const query = new URLSearchParams({ page: String(page), search: term, overdueOnly: String(overdueOnly) })
    api<Report>(`/businesses/${businessId}/customers/outstanding-report?${query}`, { signal: controller.signal })
      .then(value => { if (active) setReport(value) })
      .catch(cause => { if (active && !(cause instanceof SessionExpiredError))
        setError(cause instanceof Error ? cause.message : 'Unable to load outstanding report.') })
    return () => { active = false; controller.abort() }
  }, [businessId, term, overdueOnly, page, reload])
  const money = (value: number) => value.toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 })
  return <>
    <form className="platform-search mb-2" onSubmit={event => { event.preventDefault(); setPage(1); setTerm(search.trim()); setReload(x => x + 1) }}>
      <label>Customer search<input type="search" className="form-control" maxLength={100}
        value={search} onChange={event => setSearch(event.target.value)} placeholder="Name or phone" /></label>
      <label>Show<select className="form-select" value={overdueOnly ? 'overdue' : 'all'}
        onChange={event => { setOverdueOnly(event.target.value === 'overdue'); setPage(1) }}>
        <option value="all">All unpaid invoices</option><option value="overdue">Overdue only</option>
      </select></label>
      <button type="submit" className="btn btn-outline-secondary">Search / Refresh</button>
    </form>
    {error ? <p role="alert">{error} <button type="button" className="btn btn-sm btn-outline-secondary"
      onClick={() => setReload(x => x + 1)}>Retry</button></p> : !report ? <p role="status">Loading outstanding balances…</p> :
      <>
        <p className="small mb-2">Current balances · {report.businessDate} · {report.timeZoneId} · All locations.
          Invoices due today are not overdue. Totals follow the selected filters.</p>
        {report.totals.length === 0 ? <p>No matching unpaid credit invoices.</p> :
          <>
            <div className="table-responsive mb-3"><table className="table table-sm align-middle">
              <caption className="caption-top fw-semibold">Outstanding aging</caption>
              <thead><tr><th>Currency</th><th className="text-end">Outstanding</th><th className="text-end">Not overdue</th>
                <th className="text-end">Overdue</th><th className="text-end">1–30 days</th>
                <th className="text-end">31–60 days</th><th className="text-end">61–90 days</th>
                <th className="text-end">90+ (over 90)</th></tr></thead>
              <tbody>{report.totals.map(x => <tr key={x.currencyCode}><td>{x.currencyCode}</td>
                <td className="text-end fw-bold">{money(x.outstanding)}</td><td className="text-end">{money(x.notDue)}</td>
                <td className="text-end">{money(x.overdue)}</td><td className="text-end">{money(x.days1To30)}</td>
                <td className="text-end">{money(x.days31To60)}</td><td className="text-end">{money(x.days61To90)}</td>
                <td className="text-end">{money(x.daysOver90)}</td></tr>)}</tbody>
            </table></div>
            <div className="table-responsive"><table className="table table-sm align-middle">
              <caption className="caption-top fw-semibold">Customer balances</caption>
              <thead><tr><th>Customer</th><th>Currency</th><th>Oldest due date</th><th className="text-end">Invoices</th>
                <th className="text-end">Outstanding</th><th className="text-end">Overdue</th></tr></thead>
              <tbody>{report.items.map(x => <tr key={x.customerId + '-' + x.currencyCode}><td>{x.name}{!x.isActive && <small className="d-block">Inactive</small>}</td>
                <td>{x.currencyCode}</td><td>{x.oldestDueDate}</td><td className="text-end">{x.invoiceCount}</td>
                <td className="text-end fw-semibold">{money(x.outstanding)}</td><td className="text-end">{money(x.overdue)}</td></tr>)}</tbody>
            </table></div>
          </>}
        <div className="d-flex align-items-center gap-2">
          <button type="button" className="btn btn-sm btn-outline-secondary" disabled={page === 1} onClick={() => setPage(x => x - 1)}>Previous</button>
          <span>Page {report.page}</span>
          <button type="button" className="btn btn-sm btn-outline-secondary" disabled={!report.hasMore} onClick={() => setPage(x => x + 1)}>Next</button>
        </div>
      </>}
  </>
}
