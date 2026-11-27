import { useEffect, useRef, useState } from 'react'
import { api, operationKey, post } from './api'
import { moneyCents } from './posMoney'

interface OpeningRequest { locationId: number; requestKey: string; openingCash: number }
interface RegisterSession extends OpeningRequest {
  id: number; actorUserId: number; currencyCode: string; openedAtUtc: string; closedAtUtc: string | null
}
interface CurrentRegister { currencyCode: string; canOpen: boolean; session: RegisterSession | null }

function matchesOpening(session: RegisterSession, request: OpeningRequest, userId: number, currencyCode: string) {
  return Number.isSafeInteger(session.id) && session.id > 0 && session.actorUserId === userId &&
    session.locationId === request.locationId && session.requestKey === request.requestKey &&
    session.openingCash === request.openingCash && session.closedAtUtc === null &&
    session.currencyCode === currencyCode
}

export default function CashRegisterOpening({ userId, businessId, locationId, canOperate, disabled }: {
  userId: number; businessId: number; locationId: number; canOperate: boolean; disabled: boolean
}) {
  const base = `/businesses/${businessId}/cash-register`
  const key = `cash-register-opening:${userId}:${businessId}:${locationId}`
  const [current, setCurrent] = useState<CurrentRegister | null>(null)
  const [pending, setPending] = useState<OpeningRequest | null>(null)
  const [amount, setAmount] = useState('')
  const [error, setError] = useState('')
  const [storageError, setStorageError] = useState(false)
  const [busy, setBusy] = useState(false)
  const [reload, setReload] = useState(0)
  const lock = useRef(false)

  useEffect(() => {
    const controller = new AbortController()
    setCurrent(null); setError(''); setStorageError(false)
    let saved: OpeningRequest | null = null
    try {
      const raw = sessionStorage.getItem(key)
      if (raw) {
        saved = JSON.parse(raw) as OpeningRequest
        if (saved.locationId !== locationId || typeof saved.requestKey !== 'string' ||
          !/^[0-9a-f]{32}$/.test(saved.requestKey) || typeof saved.openingCash !== 'number' ||
          !Number.isFinite(moneyCents(saved.openingCash))) throw new Error('Invalid opening request')
      }
      setPending(saved)
    } catch {
      setStorageError(true); setPending(null)
      setError('Opening recovery storage is unavailable. Reconcile the register before opening another shift.')
      return () => controller.abort()
    }
    api<CurrentRegister>(base + '/current?' + new URLSearchParams({ locationId: String(locationId) }),
      { signal: controller.signal }).then(value => {
      if (controller.signal.aborted) return
      setCurrent(value)
      if (saved && value.session && matchesOpening(value.session, saved, userId, value.currencyCode)) {
        try { sessionStorage.removeItem(key); setPending(null) }
        catch { setError('The opening was verified, but recovery storage could not be cleared.') }
      } else if (saved && value.session) {
        setError('A different shift is already open. Reconcile it before retrying the saved opening.')
      }
    }).catch(() => {
      if (!controller.signal.aborted) setError('Unable to verify the current register. Check status again.')
    })
    return () => controller.abort()
  }, [base, key, locationId, userId, reload])

  async function open(request: OpeningRequest) {
    if (lock.current || disabled || !canOperate || !current?.canOpen || current.session || storageError) return
    lock.current = true; setBusy(true); setError('')
    try {
      // Preserve the exact opening before any write; no new key on retry.
      sessionStorage.setItem(key, JSON.stringify(request))
      setPending(request)
      const result = await post<RegisterSession>(base + '/open', request)
      if (!matchesOpening(result, request, userId, current.currencyCode))
        throw new Error('Register opening response could not be verified.')
      sessionStorage.removeItem(key)
      setPending(null)
      setCurrent({ ...current, canOpen: false, session: result })
    } catch (cause) {
      setError((cause instanceof Error ? cause.message : 'Unable to open register.') +
        ' Check status before retrying. Do not count the opening cash twice.')
    } finally { lock.current = false; setBusy(false) }
  }

  const cents = moneyCents(amount)
  const canSubmit = !busy && !disabled && canOperate && current?.canOpen === true && !storageError
  return <section className="border rounded p-2 mb-2" aria-label="Cash register">
    <div className="d-flex flex-wrap align-items-center justify-content-between gap-2">
      <strong>Cash register</strong>
      <small>Opening pilot · billing is not linked</small>
    </div>
    {error && <p role="alert" className="small text-danger mb-1">{error}</p>}
    {current?.session && <p role="status" className="small mb-1">Shift #{current.session.id} open · Opening cash {current.session.currencyCode} {current.session.openingCash.toFixed(2)}</p>}
    {pending ? <>
      <p className="small mb-1">Pending opening: {pending.openingCash.toFixed(2)}. Verify before counting cash again.</p>
      <div className="d-flex flex-wrap gap-2">
        <button type="button" className="btn btn-outline-secondary btn-sm" disabled={busy || disabled}
          onClick={() => setReload(x => x + 1)}>Check shift status</button>
        <button type="button" className="btn btn-primary btn-sm" disabled={!canSubmit || !!current?.session}
          onClick={() => void open(pending)}>Retry same opening</button>
      </div>
    </> : !current ? error ? <button type="button" className="btn btn-outline-secondary btn-sm"
      disabled={busy || disabled} onClick={() => setReload(x => x + 1)}>Check shift status</button>
      : <p role="status" className="small mb-0">Checking register…</p>
      : !current.session && (current.canOpen && !storageError ? <form className="d-flex flex-wrap align-items-end gap-2"
        onSubmit={event => { event.preventDefault(); if (canSubmit && Number.isFinite(cents))
          void open({ locationId, requestKey: operationKey(), openingCash: cents / 100 }) }}>
        <label className="flex-grow-1 small">Opening cash ({current.currencyCode})
          <input className="form-control form-control-sm" inputMode="decimal" value={amount}
            disabled={busy || disabled} onChange={event => setAmount(event.target.value)} />
        </label>
        <button type="submit" className="btn btn-primary btn-sm" disabled={!canSubmit || !Number.isFinite(cents)}>Open Shift</button>
      </form> : <p className="small mb-0">Register opening is unavailable.</p>)}
  </section>
}
