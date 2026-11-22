import { useEffect, useRef, type FormEvent } from 'react'
import { Banknote, LockKeyhole, X } from 'lucide-react'

interface PosCashPaymentDialogProps {
  amount: number
  cash: string
  currency: string
  lineCount: number
  busy: boolean
  onCashChange: (value: string) => void
  onCancel: () => void
  onConfirm: () => void
}

export default function PosCashPaymentDialog({
  amount, cash, currency, lineCount, busy, onCashChange, onCancel, onConfirm
}: PosCashPaymentDialogProps) {
  const dialogRef = useRef<HTMLDivElement>(null)
  const cashRef = useRef<HTMLInputElement>(null)
  const cancelRef = useRef(onCancel)
  const busyRef = useRef(busy)
  cancelRef.current = onCancel
  busyRef.current = busy
  const received = Number(cash)
  const validCash = cash.trim() !== '' && Number.isFinite(received) && received >= amount &&
    Math.abs(Math.round(received * 100) - received * 100) < 0.000001
  const change = validCash ? Math.round((received - amount) * 100) / 100 : 0
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
    if (validCash && !busy) onConfirm()
  }

  return <div className="pos-modal-layer" role="presentation">
    <div className="pos-modal-backdrop" aria-hidden="true" />
    <div ref={dialogRef} className="pos-payment-dialog" role="dialog" aria-modal="true"
      aria-labelledby="pos-payment-heading" aria-describedby="pos-payment-description">
      <div className="pos-dialog-heading">
        <div><span className="pos-dialog-kicker">NEW SALE · CASH</span>
          <h2 id="pos-payment-heading">Complete Payment</h2>
          <p id="pos-payment-description">{lineCount} {lineCount === 1 ? 'item' : 'items'} in this draft sale.
            Payment is only recorded after confirmation.</p></div>
        <button type="button" className="pos-dialog-close" onClick={onCancel}
          disabled={busy} aria-label="Close payment and return to basket"><X size={20} /></button>
      </div>
      <form onSubmit={submit}>
        <div className="pos-dialog-body">
          <div className="pos-dialog-total"><span>Total Payable</span><strong>{money(amount)}</strong>
            <small>Cash-only checkout · {currency}</small></div>
          <div className="pos-dialog-method"><span><Banknote size={21} /> Cash</span>
            <strong>Selected</strong></div>
          <label className="form-label" htmlFor="pos-cash-received">Cash received</label>
          <input ref={cashRef} id="pos-cash-received" className="form-control pos-dialog-cash"
            type="number" min={amount} step="0.01" inputMode="decimal" required
            value={cash} onChange={event => onCashChange(event.target.value)} disabled={busy}
            aria-describedby="pos-cash-help" />
          <p id="pos-cash-help" className="pos-dialog-hint">Enter the exact amount handed over by the customer.
            Insufficient payment cannot be confirmed.</p>
          <div className="pos-dialog-change"><span>Change to return</span><strong>{money(change)}</strong></div>
          <p className="pos-dialog-security"><LockKeyhole size={15} /> Final price and stock are checked by the server before posting.</p>
        </div>
        <div className="pos-dialog-footer">
          <button type="button" className="btn btn-outline-secondary" disabled={busy} onClick={onCancel}>Back to Cart</button>
          <button type="submit" className="btn btn-primary" disabled={busy || !validCash || amount <= 0}>
            {busy ? 'Confirming…' : 'Confirm Cash Payment'}
          </button>
        </div>
      </form>
    </div>
  </div>
}
