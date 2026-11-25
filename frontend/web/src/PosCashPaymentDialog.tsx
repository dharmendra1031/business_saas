import { useEffect, useRef, type FormEvent } from 'react'
import { X } from 'lucide-react'
import { moneyCents } from './posMoney'

interface PosCashPaymentDialogProps {
  amount: number
  cash: string
  currency: string
  lineCount: number
  busy: boolean
  error: string
  onCashChange: (value: string) => void
  onCancel: () => void
  autoOpen: boolean
  onConfirm: (openPrint: boolean) => void
}

export default function PosCashPaymentDialog({
  amount, cash, currency, lineCount, busy, error, onCashChange, onCancel, onConfirm, autoOpen
}: PosCashPaymentDialogProps) {
  const dialogRef = useRef<HTMLDivElement>(null)
  const cashRef = useRef<HTMLInputElement>(null)
  const cancelRef = useRef(onCancel)
  const busyRef = useRef(busy)
  cancelRef.current = onCancel
  busyRef.current = busy
  const receivedCents = moneyCents(cash)
  const amountCents = moneyCents(amount)
  const validCash = Number.isFinite(receivedCents) && Number.isFinite(amountCents) &&
    receivedCents >= amountCents
  const change = validCash ? (receivedCents - amountCents) / 100 : 0
  const money = (value: number) => currency + ' ' + value.toFixed(2)

  useEffect(() => {
    const previouslyFocused = document.activeElement instanceof HTMLElement ? document.activeElement : null
    const previousOverflow = document.body.style.overflow
    document.body.style.overflow = 'hidden'
    cashRef.current?.focus()
    function onKeyDown(event: KeyboardEvent) {
      if (event.key === 'Escape' && !busyRef.current) {
        event.preventDefault()
        cancelRef.current()
        return
      }
      if (event.key !== 'Tab') return
      const focusable = Array.from(dialogRef.current?.querySelectorAll<HTMLElement>(
        'button:not([disabled]), input:not([disabled]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])'
      ) ?? [])
      if (!focusable.length) return
      const first = focusable[0]
      const last = focusable[focusable.length - 1]
      if (event.shiftKey && (document.activeElement === first || !dialogRef.current?.contains(document.activeElement))) {
        event.preventDefault()
        last.focus()
      } else if (!event.shiftKey && (document.activeElement === last || !dialogRef.current?.contains(document.activeElement))) {
        event.preventDefault()
        first.focus()
      }
    }
    document.addEventListener('keydown', onKeyDown)
    return () => {
      document.removeEventListener('keydown', onKeyDown)
      document.body.style.overflow = previousOverflow
      previouslyFocused?.focus()
    }
  }, [])

  function submit(event: FormEvent) {
    event.preventDefault()
    if (validCash && !busy) onConfirm(autoOpen)
  }

  return <div className="pos-modal-layer" role="presentation">
    <div className="pos-modal-backdrop" aria-hidden="true" />
    <div ref={dialogRef} className="pos-payment-dialog" role="dialog" aria-modal="true"
      aria-labelledby="pos-payment-heading" aria-describedby="pos-payment-description">
      <div className="pos-dialog-heading">
        <div><h2 id="pos-payment-heading">Complete Payment</h2>
          <p id="pos-payment-description">Cash · {lineCount} {lineCount === 1 ? 'item' : 'items'}</p></div>
        <button type="button" className="pos-dialog-close" onClick={onCancel}
          disabled={busy} aria-label="Close payment and return to basket"><X size={20} /></button>
      </div>
      <form onSubmit={submit}>
        <div className="pos-dialog-body">
          {error && <div className="alert alert-danger" role="alert">{error}</div>}
          <div className="pos-dialog-total"><span>Total Payable</span><strong>{money(amount)}</strong></div>
          <label className="form-label" htmlFor="pos-cash-received">Cash received</label>
          <input ref={cashRef} id="pos-cash-received" className="form-control pos-dialog-cash"
            type="number" min={amount} step="0.01" inputMode="decimal" required
            value={cash} onChange={event => onCashChange(event.target.value)} disabled={busy}
            aria-describedby="pos-cash-help" />
          <p id="pos-cash-help" className="pos-dialog-hint">Enter cash received (up to 2 decimals).</p>
          {cash !== '' && !Number.isFinite(receivedCents) &&
            <p className="text-danger small" role="alert">Enter a valid cash amount within the supported range.</p>}
          <div className="pos-dialog-change"><span>Change to return</span><strong>{money(change)}</strong></div>
          {autoOpen && <p className="pos-dialog-hint">Save &amp; Print opens the receipt for printing.</p>}
        </div>
        <div className="pos-dialog-footer">
          <button type="button" className="btn btn-outline-secondary" disabled={busy} onClick={onCancel}>Back to Cart</button>
          {autoOpen && <button type="button" className="btn btn-outline-primary" disabled={busy || !validCash || amount <= 0}
            onClick={() => onConfirm(false)}>Save Only</button>}
          <button type="submit" className="btn btn-primary" disabled={busy || !validCash || amount <= 0}>
            {busy ? 'Saving…' : autoOpen ? 'Save & Print' : 'Save Only'}
          </button>
        </div>
      </form>
    </div>
  </div>
}
