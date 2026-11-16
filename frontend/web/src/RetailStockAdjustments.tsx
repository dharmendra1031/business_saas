import { useEffect, useRef, useState, type FormEvent } from 'react'
import { api, post, SessionExpiredError } from './api'

export interface StockSelection {
  id: number
  name: string
  sku: string
  quantity: number
}

interface Adjustment {
  id: number
  productId: number
  name: string
  sku: string
  systemQuantity: number
  countedQuantity: number
  difference: number
  reason: string
  remarks: string | null
  status: string
  requestedAtUtc: string
  reviewRemarks: string | null
  postedAtUtc: string | null
}

interface AdjustmentPage {
  items: Adjustment[]
  total: number
  page: number
  pageSize: number
}

const reasons = ['Physical Count', 'Damage', 'Expiry', 'Loss', 'Other']

function errorMessage(error: unknown): string {
  return error instanceof Error ? error.message : 'Request failed. Please try again.'
}

export default function RetailStockAdjustments({ businessId, locationId, canOperate,
  isOwner, selected, onCancel, onPosted }: {
  businessId: number
  locationId: number
  canOperate: boolean
  isOwner: boolean
  selected: StockSelection | null
  onCancel: () => void
  onPosted: () => void
}) {
  const [items, setItems] = useState<Adjustment[]>([])
  const [page, setPage] = useState(1)
  const [total, setTotal] = useState(0)
  const [reload, setReload] = useState(0)
  const [loading, setLoading] = useState(true)
  const [loadError, setLoadError] = useState('')
  const [error, setError] = useState('')
  const [notice, setNotice] = useState('')
  const [busyId, setBusyId] = useState<number | null>(null)
  const [saving, setSaving] = useState(false)
  const [counted, setCounted] = useState('')
  const [reason, setReason] = useState('')
  const [remarks, setRemarks] = useState('')
  const [rejectId, setRejectId] = useState<number | null>(null)
  const [reviewRemarks, setReviewRemarks] = useState('')
  const [confirmPostId, setConfirmPostId] = useState<number | null>(null)
  const countedInput = useRef<HTMLInputElement>(null)
  const basePath = `/businesses/${businessId}/stock/locations/${locationId}/adjustments`

  useEffect(() => {
    if (!selected) return
    setCounted(String(selected.quantity))
    setReason('')
    setRemarks('')
    setError('')
    countedInput.current?.focus()
  }, [selected])

  useEffect(() => {
    let active = true
    setLoading(true)
    setLoadError('')
    setItems([])
    setTotal(0)
    api<AdjustmentPage>(basePath + '?page=' + page)
      .then(data => {
        if (!active) return
        setItems(data.items)
        setTotal(data.total)
      })
      .catch(cause => {
        if (active && !(cause instanceof SessionExpiredError)) setLoadError(errorMessage(cause))
      })
      .finally(() => { if (active) setLoading(false) })
    return () => { active = false }
  }, [basePath, page, reload])

  async function create(event: FormEvent) {
    event.preventDefault()
    if (!selected || !canOperate || saving || !counted.trim()) return
    const countedQuantity = Number(counted)
    const difference = countedQuantity - selected.quantity
    if (!Number.isFinite(countedQuantity) || countedQuantity < 0 ||
      countedQuantity > 9999999999.999 ||
      Math.abs(countedQuantity * 1000 - Math.round(countedQuantity * 1000)) > 0.000001 ||
      difference === 0 || !reason) {
      setError('Enter a valid changed physical count and reason (up to 3 decimal places).')
      return
    }

    setSaving(true)
    setError('')
    setNotice('')
    try {
      await post(basePath, {
        productId: selected.id,
        systemQuantity: selected.quantity,
        countedQuantity,
        reason,
        remarks: remarks.trim()
      })
      onCancel()
      setPage(1)
      setReload(value => value + 1)
      setNotice('Adjustment submitted for approval. Stock has not changed.')
    } catch (cause) {
      if (!(cause instanceof SessionExpiredError)) {
        setError(errorMessage(cause))
        onPosted()
      }
    } finally {
      setSaving(false)
    }
  }

  async function review(id: number, approve: boolean) {
    if (!canOperate || !isOwner || busyId !== null) return
    if (!approve && !reviewRemarks.trim()) {
      setError('Enter a reason for rejection.')
      return
    }

    setBusyId(id)
    setError('')
    setNotice('')
    try {
      await post(`${basePath}/${id}/review`, {
        approve, remarks: approve ? '' : reviewRemarks.trim()
      })
      setNotice(approve ? 'Adjustment approved. Stock has not changed.'
        : 'Adjustment rejected.')
      setRejectId(null)
      setReviewRemarks('')
      setReload(value => value + 1)
    } catch (cause) {
      if (!(cause instanceof SessionExpiredError)) setError(errorMessage(cause))
    } finally {
      setBusyId(null)
    }
  }

  async function postApproved(id: number) {
    if (!canOperate || !isOwner || busyId !== null) return
    setBusyId(id)
    setError('')
    setNotice('')
    try {
      await post(`${basePath}/${id}/post`, {})
      setNotice('Stock adjustment posted. Balance and history updated.')
      setConfirmPostId(null)
      setReload(value => value + 1)
      onPosted()
    } catch (cause) {
      if (!(cause instanceof SessionExpiredError)) {
        setError(errorMessage(cause))
        onPosted()
      }
    } finally {
      setBusyId(null)
    }
  }

  const newQuantity = Number(counted)
  const difference = selected && counted.trim() && Number.isFinite(newQuantity)
    ? newQuantity - selected.quantity : 0

  return (
    <div className="product-form" aria-label="Stock adjustments">
      <div className="masters-heading">
        <div>
          <h3>Stock adjustments</h3>
          <p>Submit a physical count. Only the owner can approve and post stock changes.</p>
        </div>
      </div>
      {loadError && <div className="alert alert-danger" role="alert">
        {loadError}
        <button type="button" className="btn btn-sm btn-outline-secondary ms-2"
          onClick={() => setReload(value => value + 1)}>Retry</button>
      </div>}
      {error && <div className="alert alert-danger" role="alert">{error}</div>}
      {notice && <div className="alert alert-success" role="status">{notice}</div>}

      {selected && canOperate && <form onSubmit={event => void create(event)}>
        <h3>New adjustment — {selected.name}</h3>
        <div className="form-grid">
          <div>
            <label className="form-label">Current system stock</label>
            <div className="form-control bg-light" aria-label="Current system stock">
              {selected.quantity.toFixed(3)}
            </div>
          </div>
          <div>
            <label className="form-label" htmlFor="stock-counted">Physical count *</label>
            <input ref={countedInput} className="form-control" id="stock-counted"
              type="number" min="0" max="9999999999.999" step="0.001" required
              value={counted} onChange={event => setCounted(event.target.value)} />
          </div>
          <div>
            <label className="form-label">Difference</label>
            <div className="form-control bg-light" aria-live="polite">
              {difference > 0 ? '+' : ''}{difference.toFixed(3)}
            </div>
          </div>
          <div>
            <label className="form-label" htmlFor="stock-reason">Reason *</label>
            <select className="form-select" id="stock-reason" required value={reason}
              onChange={event => setReason(event.target.value)}>
              <option value="">Select a reason</option>
              {reasons.map(item => <option key={item} value={item}>{item}</option>)}
            </select>
          </div>
          <div className="form-wide">
            <label className="form-label" htmlFor="stock-remarks">Remarks (optional)</label>
            <textarea id="stock-remarks" className="form-control" rows={2}
              maxLength={500} value={remarks}
              onChange={event => setRemarks(event.target.value)} />
          </div>
        </div>
        <div className="form-footer">
          <button type="button" className="btn btn-outline-secondary"
            onClick={onCancel}>Cancel</button>
          <button type="submit" className="btn btn-primary"
            disabled={saving || !reason || difference === 0}>
            {saving ? 'Submitting…' : 'Submit for approval'}
          </button>
        </div>
      </form>}

      {loading ? <p role="status" className="text-secondary">Loading adjustments…</p>
        : loadError ? null
          : items.length === 0 ? <p className="masters-empty">No stock adjustments yet.</p>
            : <div className="table-responsive">
              <table className="table table-sm align-middle">
                <thead>
                  <tr><th scope="col">Product</th><th scope="col">Reason</th>
                    <th scope="col" className="text-end">System</th>
                    <th scope="col" className="text-end">Counted</th>
                    <th scope="col" className="text-end">Difference</th>
                    <th scope="col">Status</th>
                    {isOwner && canOperate && <th scope="col">Action</th>}
                  </tr>
                </thead>
                <tbody>
                  {items.map(item => <tr key={item.id}>
                    <td>
                      <strong>{item.name}</strong>
                      <span className="product-secondary">{item.sku} · #{item.id}</span>
                    </td>
                    <td>{item.reason}
                      {item.reviewRemarks && <span className="product-secondary">{item.reviewRemarks}</span>}
                    </td>
                    <td className="text-end">{item.systemQuantity.toFixed(3)}</td>
                    <td className="text-end">{item.countedQuantity.toFixed(3)}</td>
                    <td className="text-end">{item.difference > 0 ? '+' : ''}{item.difference.toFixed(3)}</td>
                    <td>{item.status}</td>
                    {isOwner && canOperate && <td>
                      {item.status === 'Pending Approval' && <>
                        <button className="btn btn-sm btn-outline-primary me-2"
                          type="button" disabled={busyId !== null}
                          onClick={() => void review(item.id, true)}>Approve</button>
                        {rejectId !== item.id
                          ? <button className="btn btn-sm btn-outline-secondary"
                              type="button" disabled={busyId !== null}
                              onClick={() => { setRejectId(item.id); setReviewRemarks('') }}>
                              Reject
                            </button>
                          : <div>
                              <label className="visually-hidden" htmlFor="adjustment-rejection">Rejection reason</label>
                              <input id="adjustment-rejection" className="form-control form-control-sm my-2"
                                placeholder="Reason for rejection" maxLength={500} value={reviewRemarks}
                                onChange={event => setReviewRemarks(event.target.value)} />
                              <button className="btn btn-sm btn-outline-danger me-2"
                                type="button" disabled={busyId !== null || !reviewRemarks.trim()}
                                onClick={() => void review(item.id, false)}>Confirm reject</button>
                              <button className="btn btn-sm btn-outline-secondary" type="button"
                                onClick={() => setRejectId(null)}>Cancel</button>
                            </div>}
                      </>}
                      {item.status === 'Approved' && (
                        confirmPostId === item.id
                          ? <div>
                              <span className="product-secondary">This changes actual stock.</span>
                              <button className="btn btn-sm btn-primary me-2" type="button"
                                disabled={busyId !== null}
                                onClick={() => void postApproved(item.id)}>Confirm post</button>
                              <button className="btn btn-sm btn-outline-secondary" type="button"
                                onClick={() => setConfirmPostId(null)}>Cancel</button>
                            </div>
                          : <button className="btn btn-sm btn-outline-primary" type="button"
                              disabled={busyId !== null}
                              onClick={() => setConfirmPostId(item.id)}>Post stock</button>
                      )}
                    </td>}
                  </tr>)}
                </tbody>
              </table>
            </div>}

      {!loading && !loadError && total > 0 && <div className="masters-pages">
        <span className="small text-secondary">Page {page} · {total} adjustment requests</span>
        <div className="btn-group">
          <button type="button" className="btn btn-sm btn-outline-secondary"
            disabled={page === 1} onClick={() => setPage(value => value - 1)}>Previous</button>
          <button type="button" className="btn btn-sm btn-outline-secondary"
            disabled={page * 20 >= total} onClick={() => setPage(value => value + 1)}>Next</button>
        </div>
      </div>}
    </div>
  )
}
