import { useEffect, useRef, useState, type FormEvent } from 'react'
import { api, post, operationKey } from './api'
import { baseQuantity } from './stockQuantity'

interface Location { id: number; name: string; canAccess: boolean }
interface Options { locations: Location[]; canWrite: boolean; isOwner: boolean }
interface Product { id: number; name: string; sku: string; unitCode: string; availableStock: number | null }
interface DraftLine extends Product { quantity: string }
interface Transfer { id: number; fromLocationId: number; toLocationId: number; status: string; reason: string; requestedAtUtc: string }
interface Line { id: number; productId: number; name: string; unitCode: string; quantity: number; received: number; damaged: number; missing: number; returned?: number; remaining: number }
interface Receipt { id: number; transferLineId: number; quantity: number; kind: string; reason: string; postedAtUtc: string }
interface Detail { transfer: Transfer; lines: Line[]; history: Receipt[] }
interface Page<T> { items: T[]; hasMore: boolean }
interface TransitLine { id: number; transferId: number; name: string; unitCode: string; fromLocationId: number; toLocationId: number; dispatched: number; remaining: number }
interface ProductPage { items: Product[]; total: number; pageSize: number }
interface Pending { path: string; body: object; transferId: number | null; action: string }

function errorMessage(error: unknown) { return error instanceof Error ? error.message : 'Request failed. Retry.' }

export default function RetailStockTransfers({ businessId, userId, canOperate }: {
  businessId: number; userId: number; canOperate: boolean
}) {
  const base = '/businesses/' + businessId + '/stock/transfers'
  const storageKey = 'stock-transfer:' + businessId + ':' + userId
  const [recovery] = useState(() => {
    try {
      const raw = sessionStorage.getItem(storageKey)
      if (!raw) return { pending: null as Pending | null, error: '' }
      const pending = JSON.parse(raw) as Pending
      if (typeof pending.path !== 'string' || !pending.body || typeof pending.body !== 'object' ||
        !(pending.path === base || /^\/\d+\/(approve|dispatch|receive|cancel)$/.test(pending.path.slice(base.length)) && pending.path.startsWith(base)))
        throw new Error()
      return { pending, error: '' }
    } catch { return { pending: null as Pending | null, error: 'Saved transfer request cannot be read. Reconcile history before posting another.' } }
  })
  const [pending, setPending] = useState(recovery.pending)
  const [options, setOptions] = useState<Options | null>(null)
  const [data, setData] = useState<Page<Transfer> | null>(null)
  const [detail, setDetail] = useState<Detail | null>(null)
  const [selected, setSelected] = useState<number | null>(recovery.pending?.transferId ?? null)
  const [creating, setCreating] = useState(false)
  const [page, setPage] = useState(1)
  const [view, setView] = useState<"pending" | "all" | "transit">("pending")
  const [transit, setTransit] = useState<Page<TransitLine> | null>(null)
  const [reload, setReload] = useState(0)
  const [error, setError] = useState('')
  const [notice, setNotice] = useState('')
  const [busy, setBusy] = useState(false)
  const lock = useRef(false)
  const [from, setFrom] = useState(0)
  const [to, setTo] = useState(0)
  const [reason, setReason] = useState('')
  const [search, setSearch] = useState('')
  const [query, setQuery] = useState('')
  const [productPage, setProductPage] = useState(1)
  const [products, setProducts] = useState<ProductPage | null>(null)
  const [draft, setDraft] = useState<DraftLine[]>([])
  const [searchError, setSearchError] = useState('')
  const [lineId, setLineId] = useState(0)
  const [qty, setQty] = useState('')
  const [kind, setKind] = useState('Received')
  const [receiveReason, setReceiveReason] = useState('')
  const allowed = canOperate && options?.canWrite === true && !recovery.error
  const name = (id: number) => options?.locations.find(x => x.id === id)?.name ?? '#' + id
  const access = (id: number) => options?.locations.some(x => x.id === id && x.canAccess) === true

  useEffect(() => {
    const controller = new AbortController()
    api<Options>(base + '/options', { signal: controller.signal })
      .then(value => { if (!controller.signal.aborted) setOptions(value) })
      .catch(cause => { if (!controller.signal.aborted) setError(errorMessage(cause)) })
    return () => controller.abort()
  }, [base, reload])
  useEffect(() => {
    const controller = new AbortController()
    if (selected !== null) {
      setDetail(null)
      api<Detail>(base + '/' + selected, { signal: controller.signal })
        .then(value => { if (!controller.signal.aborted) setDetail(value) })
        .catch(cause => { if (!controller.signal.aborted) setError(errorMessage(cause)) })
    } else if (!creating) {
      setData(null); setTransit(null)
      if (view === 'transit') {
        api<Page<TransitLine>>(base + '/in-transit?page=' + page, { signal: controller.signal })
          .then(value => { if (!controller.signal.aborted) setTransit(value) })
          .catch(cause => { if (!controller.signal.aborted) setError(errorMessage(cause)) })
      } else {
        api<Page<Transfer>>(base + '?page=' + page + '&pending=' + (view === 'pending'), { signal: controller.signal })
          .then(value => { if (!controller.signal.aborted) setData(value) })
          .catch(cause => { if (!controller.signal.aborted) setError(errorMessage(cause)) })
      }
    }
    return () => controller.abort()
  }, [base, selected, creating, page, view, reload])
  const sourceAccessible = access(from)
  useEffect(() => {
    setProducts(null); setSearchError('')
    if (!creating || !from || !query) return
    const controller = new AbortController()
    const path = '/businesses/' + businessId + '/products?status=active&page=' + productPage +
      (sourceAccessible ? '&locationId=' + from : '')
    async function load() {
      try {
        let value: ProductPage
        if (/^[0-9]{8,14}$/.test(query)) {
          value = await api<ProductPage>(path + '&code=' + encodeURIComponent(query), { signal: controller.signal })
          if (!value.total) value = await api<ProductPage>(path + '&search=' + encodeURIComponent(query), { signal: controller.signal })
        } else value = await api<ProductPage>(path + '&search=' + encodeURIComponent(query), { signal: controller.signal })
        if (!controller.signal.aborted) setProducts(value)
      } catch (cause) { if (!controller.signal.aborted) setSearchError(errorMessage(cause)) }
    }
    void load()
    return () => controller.abort()
  }, [businessId, creating, from, query, productPage, sourceAccessible])
  function clearDraft() { setDraft([]); setSearch(''); setQuery(''); setProducts(null); setReason('') }
  function refresh() { setError(''); setReload(n => n + 1) }
  async function send(request: Pending) {
    if (!allowed || lock.current) return
    lock.current = true; setBusy(true); setError(''); setNotice('')
    try {
      sessionStorage.setItem(storageKey, JSON.stringify(request))
      setPending(request)
      const result = await post<{ id: number; status: string }>(request.path, request.body)
      if (!Number.isSafeInteger(result.id) || result.id <= 0 ||
        (request.transferId !== null && !request.path.endsWith('/receive') && result.id !== request.transferId) ||
        !['Requested', 'Approved', 'InTransit', 'Completed', 'Cancelled'].includes(result.status))
        throw new Error('Transfer confirmation could not be verified.')
      sessionStorage.removeItem(storageKey)
      setPending(null); setCreating(false); clearDraft()
      setSelected(request.transferId ?? result.id); setQty(''); setLineId(0); setReceiveReason('')
      setNotice('Transfer operation recorded. Status: ' + result.status)
      refresh()
    } catch (cause) { setError(errorMessage(cause) + ' Check transfer history or retry the original request.') }
    finally { lock.current = false; setBusy(false) }
  }
  const validDraft = from > 0 && to > 0 && from !== to && (access(from) || access(to)) &&
    reason.trim().length >= 3 && draft.length > 0 && draft.every(l => baseQuantity(Number(l.quantity), 1) !== null)
  function submitTransfer(event: FormEvent, dispatchNow: boolean) {
    event.preventDefault()
    if (!validDraft || pending || !allowed) return
    if (dispatchNow && !window.confirm('Confirm that these goods are physically being dispatched from the source location.')) return
    void send({ path: base, transferId: null, action: dispatchNow ? 'Approve & dispatch' : 'Request transfer',
      body: { fromLocationId: from, toLocationId: to, reason: reason.trim(), requestKey: operationKey(), dispatchNow,
        lines: draft.map(x => ({ productId: x.id, quantity: Number(x.quantity) })) } })
  }
  function transition(action: string) {
    if (!detail || pending || !allowed) return
    if (action === 'dispatch' && !window.confirm('Confirm physical dispatch of all transfer items.')) return
    if (action === 'cancel' && !window.confirm('Cancel this undispatched transfer?')) return
    void send({ path: base + '/' + detail.transfer.id + '/' + action, transferId: detail.transfer.id, action, body: {} })
  }
  const line = detail?.lines.find(x => x.id === lineId)
  function receive(event: FormEvent) {
    event.preventDefault()
    if (!detail || !line || !allowed || pending || baseQuantity(Number(qty), 1) === null ||
        Number(qty) > line.remaining || kind !== 'Received' && receiveReason.trim().length < 5) return
    if (!window.confirm(kind === 'Received' ? 'Confirm goods physically received and sellable.' : kind === 'Returned' ? 'Confirm sellable goods have physically arrived back at the source location.' : 'Confirm this transit exception. Quantity will not be added to sellable stock.')) return
    void send({ path: base + '/' + detail.transfer.id + '/receive', transferId: detail.transfer.id, action: kind,
      body: { lineId, quantity: Number(qty), kind, reason: receiveReason.trim(), expectedRemaining: line.remaining, requestKey: operationKey() } })
  }
  const canEdit = allowed && !busy && !pending
  return <div>
    {notice && <p role="status" className="text-success">{notice}</p>}
    {error && <p role="alert" className="text-danger">{error}</p>}
    {recovery.error && <p role="alert" className="text-danger">{recovery.error}</p>}
    {pending && <div className="border rounded p-2 mb-3">
      <p className="mb-2">Unresolved operation: {pending.action}. Do not dispatch or receive goods again.</p>
      <button type="button" className="btn btn-sm btn-primary me-2" disabled={!allowed || busy} onClick={() => void send(pending)}>Retry same operation</button>
      <button type="button" className="btn btn-sm btn-outline-secondary" disabled={busy} onClick={() => { setCreating(false); setSelected(null); setView("all"); setPage(1); refresh() }}>Review history</button>
      <button type="button" className="btn btn-sm btn-outline-secondary ms-2" disabled={busy} onClick={() => {
        if (!window.confirm('Only discard after verifying the transfer and its stock movements. Discard recovery record?')) return
        try { sessionStorage.removeItem(storageKey); setPending(null); refresh() } catch (cause) { setError(errorMessage(cause)) }
      }}>Clear after verification</button>
    </div>}
    {!options && !error && <p role="status">Loading locations…</p>}
    {options && !allowed && <p className="small">Transfer posting is unavailable. History is read-only.</p>}
    {options && options.locations.length < 2 && <p>Add a second business location to use stock transfers.</p>}
    {creating && options && <form onSubmit={event => submitTransfer(event, false)}>
      <div className="d-flex justify-content-between align-items-center mb-2"><h3 className="h5">New stock transfer</h3>
        <button type="button" className="btn btn-sm btn-outline-secondary" disabled={busy} onClick={() => { setCreating(false); clearDraft(); refresh() }}>Back</button></div>
      <fieldset disabled={!canEdit}>
        <div className="row g-2 mb-2">
          <div className="col-sm-6"><label className="form-label" htmlFor="transfer-from">From location</label>
            <select id="transfer-from" required className="form-select" value={from} onChange={event => {
              setFrom(Number(event.target.value)); setTo(0); clearDraft()
            }}><option value={0}>Select source</option>{options.locations.map(x => <option key={x.id} value={x.id}>{x.name}</option>)}</select></div>
          <div className="col-sm-6"><label className="form-label" htmlFor="transfer-to">To location</label>
            <select id="transfer-to" required className="form-select" value={to} onChange={event => setTo(Number(event.target.value))}>
              <option value={0}>Select destination</option>{options.locations.filter(x => x.id !== from && (sourceAccessible || x.canAccess)).map(x => <option key={x.id} value={x.id}>{x.name}</option>)}</select></div>
        </div>
        {!sourceAccessible && from > 0 && <p className="small">Source availability is restricted to authorized staff. Stock will be validated at dispatch.</p>}
        <label className="form-label" htmlFor="transfer-search">Search product / scan barcode</label>
        <div className="d-flex gap-2 mb-2"><input id="transfer-search" className="form-control" value={search} maxLength={150}
          disabled={!from} onChange={event => setSearch(event.target.value)} onKeyDown={event => {
            if (event.key === 'Enter') { event.preventDefault(); setQuery(search.trim()); setProductPage(1) }
          }} /><button type="button" className="btn btn-outline-secondary" disabled={!from || !search.trim()}
            onClick={() => { setQuery(search.trim()); setProductPage(1) }}>Search</button></div>
        {searchError && <p role="alert">{searchError}</p>}
        {products && <div className="border rounded p-2 mb-2">
          {products.items.length === 0 && <p>No matching active products.</p>}
          {products.items.map(x => <div key={x.id} className="d-flex justify-content-between align-items-center gap-2 py-1 border-bottom">
            <span>{x.name} · {x.sku} · {x.availableStock == null ? 'Stock restricted' : x.availableStock + ' ' + x.unitCode}</span>
            <button type="button" className="btn btn-sm btn-outline-primary" disabled={draft.length >= 50 || draft.some(l => l.id === x.id)}
              onClick={() => setDraft(value => [...value, { ...x, quantity: '1' }])}>Add</button></div>)}
          <div className="d-flex gap-2 mt-2"><button type="button" className="btn btn-sm btn-outline-secondary" disabled={productPage <= 1} onClick={() => setProductPage(n => n - 1)}>Previous</button>
            <button type="button" className="btn btn-sm btn-outline-secondary" disabled={productPage * products.pageSize >= products.total} onClick={() => setProductPage(n => n + 1)}>Next</button></div>
        </div>}
        <div className="table-responsive"><table className="table table-sm"><thead><tr><th>Product</th><th>Available</th><th>Transfer quantity</th><th>Action</th></tr></thead>
          <tbody>{draft.map(l => <tr key={l.id}><td>{l.name} · {l.unitCode}</td><td>{l.availableStock ?? '—'}</td>
            <td><input type="number" required min="0.001" step="0.001" className="form-control" aria-label={'Transfer quantity for ' + l.name}
              value={l.quantity} onChange={event => setDraft(rows => rows.map(x => x.id === l.id ? { ...x, quantity: event.target.value } : x))} /></td>
            <td><button type="button" className="btn btn-sm btn-outline-secondary" onClick={() => setDraft(rows => rows.filter(x => x.id !== l.id))}>Remove</button></td></tr>)}</tbody></table></div>
        <label htmlFor="transfer-reason" className="form-label">Transfer reason</label><input id="transfer-reason" required minLength={3} maxLength={200}
          className="form-control mb-2" value={reason} onChange={event => setReason(event.target.value)} />
        <div className="d-flex gap-2 flex-wrap"><button type="submit" className="btn btn-outline-primary" disabled={!validDraft}>Submit request</button>
          {options.isOwner && <button type="button" className="btn btn-primary" disabled={!validDraft || draft.some(l => l.availableStock == null || Number(l.quantity) > l.availableStock)}
            onClick={event => submitTransfer(event, true)}>Approve &amp; dispatch</button>}</div>
      </fieldset>
    </form>}
    {!creating && selected !== null && <>
      <button type="button" className="btn btn-sm btn-outline-secondary mb-2" disabled={busy} onClick={() => { setSelected(null); setLineId(0); setQty(''); refresh() }}>Back to transfers</button>
      {!detail && !error && <p role="status">Loading transfer…</p>}
      {detail && <><h3 className="h5">Transfer #{detail.transfer.id} — {detail.transfer.status}</h3>
        <p>{name(detail.transfer.fromLocationId)} → {name(detail.transfer.toLocationId)} · {detail.transfer.reason}</p>
        <div className="d-flex gap-2 flex-wrap mb-2">
          {canEdit && options?.isOwner && detail.transfer.status === 'Requested' && <button type="button" className="btn btn-primary" onClick={() => transition('approve')}>Approve</button>}
          {canEdit && access(detail.transfer.fromLocationId) && detail.transfer.status === 'Approved' && <button type="button" className="btn btn-primary" onClick={() => transition('dispatch')}>Dispatch stock</button>}
          {canEdit && options?.isOwner && ['Requested', 'Approved'].includes(detail.transfer.status) && <button type="button" className="btn btn-outline-secondary" onClick={() => transition('cancel')}>Cancel transfer</button>}
        </div>
        <div className="table-responsive"><table className="table table-sm">
          <thead><tr><th>Product</th><th>Requested</th><th>Received</th><th>Damaged</th><th>Missing</th><th>Returned to source</th><th>In transit</th></tr></thead>
          <tbody>{detail.lines.map(l => <tr key={l.id}><td>{l.name} · {l.unitCode}</td><td>{l.quantity}</td><td>{l.received}</td><td>{l.damaged}</td><td>{l.missing}</td><td>{l.returned ?? 0}</td><td>{l.remaining}</td></tr>)}</tbody></table></div>
        {canEdit && access(detail.transfer.toLocationId) && detail.transfer.status === 'InTransit' && <form onSubmit={receive}>
          <h4 className="h6">Receive / reconcile stock</h4>
          <label className="form-label" htmlFor="transfer-line">Product</label><select id="transfer-line" required className="form-select mb-2" value={lineId}
            onChange={event => { setLineId(Number(event.target.value)); setQty('') }}><option value={0}>Select product</option>{detail.lines.filter(x => x.remaining > 0).map(l => <option key={l.id} value={l.id}>{l.name} ({l.remaining} {l.unitCode})</option>)}</select>
          <label className="form-label" htmlFor="transfer-receive-qty">Quantity</label><input id="transfer-receive-qty" required type="number" min="0.001" max={line?.remaining ?? 0}
            step="0.001" className="form-control mb-2" value={qty} onChange={event => setQty(event.target.value)} />
          <label className="form-label" htmlFor="transfer-kind">Disposition</label><select id="transfer-kind" className="form-select mb-2" value={kind} onChange={event => setKind(event.target.value)}>
            <option value="Received">Received — sellable</option>{options?.isOwner && <><option value="Damaged">Damaged — exclude from sellable stock</option><option value="Missing">Missing — confirmed loss</option><option value="Returned">Returned — received back at source</option></>}</select>
          <label className="form-label" htmlFor="transfer-receive-reason">Reason (required for exceptions)</label><input id="transfer-receive-reason" maxLength={200} className="form-control mb-2" value={receiveReason} onChange={event => setReceiveReason(event.target.value)} />
          {kind === "Returned" && <p className="small">Stock will be restored at {name(detail.transfer.fromLocationId)} only after physical return. Already received quantities require a separate transfer.</p>}
          <button type="submit" className="btn btn-primary" disabled={!line || baseQuantity(Number(qty), 1) === null || Number(qty) > line.remaining || kind !== 'Received' && receiveReason.trim().length < 5}>Post receiving</button>
        </form>}
        <h4 className="h6 mt-3">Recent receiving history</h4>
        {detail.history.length === 0 ? <p>No receiving entries.</p> : <div className="table-responsive"><table className="table table-sm"><thead><tr><th>Reference</th><th>Product</th><th>Quantity</th><th>Disposition</th><th>Reason</th></tr></thead>
          <tbody>{detail.history.map(h => <tr key={h.id}><td>#{h.id}</td><td>{detail.lines.find(l => l.id === h.transferLineId)?.name}</td><td>{h.quantity}</td><td>{h.kind}</td><td>{h.reason}</td></tr>)}</tbody></table></div>}
      </>}
    </>}
    {!creating && selected === null && <>
      <div className="d-flex align-items-end gap-2 flex-wrap mb-3">
        <label className="form-label mb-0">Show transfers<select className="form-select" value={view} onChange={event => { setView(event.target.value as typeof view); setPage(1); setError('') }}>
          <option value="pending">Pending transfers</option><option value="transit">In-transit stock</option><option value="all">All history</option></select></label>
        <button type="button" className="btn btn-outline-secondary" onClick={refresh}>Refresh</button>
        {options && options.locations.length > 1 && <button type="button" className="btn btn-primary" disabled={!canEdit} onClick={() => { setFrom(0); setTo(0); clearDraft(); setCreating(true) }}>New transfer</button>}
      </div>
      {view === 'transit' && <>
        <p className="small">Stock dispatched but not yet received or reconciled. These quantities are not available for sale at either location.</p>
        {!transit && !error && <p role="status">Loading in-transit stock…</p>}
        {transit && <>
          <div className="table-responsive"><table className="table table-sm" aria-label="In-transit stock">
            <thead><tr><th>Transfer</th><th>Product</th><th>From</th><th>To</th><th>Dispatched</th><th>Remaining</th></tr></thead>
            <tbody>{transit.items.map(item => <tr key={item.id}>
              <td><button type="button" className="btn btn-sm btn-outline-secondary"
                onClick={() => { setSelected(item.transferId); setKind('Received'); setNotice(''); setError('') }}>#{item.transferId}</button></td>
              <td>{item.name} · {item.unitCode}</td><td>{name(item.fromLocationId)}</td><td>{name(item.toLocationId)}</td>
              <td>{item.dispatched}</td><td>{item.remaining}</td></tr>)}</tbody>
          </table></div>
          {!transit.items.length && <p>No stock in transit.</p>}
          <div className="d-flex gap-2"><button type="button" className="btn btn-sm btn-outline-secondary" disabled={page <= 1} onClick={() => setPage(n => n - 1)}>Previous</button>
            <button type="button" className="btn btn-sm btn-outline-secondary" disabled={!transit.hasMore} onClick={() => setPage(n => n + 1)}>Next</button></div>
        </>}
      </>}
      {view !== 'transit' && !data && !error && <p role="status">Loading transfers…</p>}
      {view !== 'transit' && data && <><div className="table-responsive"><table className="table table-sm"><thead><tr><th>Reference</th><th>From</th><th>To</th><th>Status</th><th>Action</th></tr></thead>
        <tbody>{data.items.map(item => <tr key={item.id}><td>#{item.id}</td><td>{name(item.fromLocationId)}</td><td>{name(item.toLocationId)}</td><td>{item.status}</td>
          <td><button type="button" className="btn btn-sm btn-outline-secondary" onClick={() => { setSelected(item.id); setNotice(''); setError(''); setKind('Received') }}>View</button></td></tr>)}</tbody></table></div>
        {data.items.length === 0 && <p>No matching transfers.</p>}
        <div className="d-flex gap-2"><button type="button" className="btn btn-sm btn-outline-secondary" disabled={page <= 1} onClick={() => setPage(n => n - 1)}>Previous</button>
          <button type="button" className="btn btn-sm btn-outline-secondary" disabled={!data.hasMore} onClick={() => setPage(n => n + 1)}>Next</button></div></>}
    </>}
  </div>
}
