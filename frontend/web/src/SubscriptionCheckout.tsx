import { useEffect, useState } from 'react'
import { api, post, SessionExpiredError } from './api'

interface CheckoutSummary {
  enabled: boolean
  available?: boolean
  paymentRequestId?: number
  amount?: number
  currencyCode?: string
  status?: string
  canPay?: boolean
}

interface CheckoutOrder {
  paymentRequestId: number
  orderId: string
  keyId: string
  amount: number
  currency: string
}

interface RazorpayWindow extends Window {
  Razorpay?: new (options: {
    key: string
    amount: number
    currency: string
    order_id: string
    name: string
    description: string
    modal: { ondismiss: () => void }
    handler: () => void
  }) => { open: () => void }
}

async function loadCheckoutScript() {
  const razorpay = window as RazorpayWindow
  if (razorpay.Razorpay) return
  await new Promise<void>((resolve, reject) => {
    const script = document.createElement('script')
    script.src = 'https://checkout.razorpay.com/v1/checkout.js'
    script.async = true
    script.onload = () => resolve()
    script.onerror = () => reject(new Error('Unable to load secure payment checkout.'))
    document.body.appendChild(script)
  })
  if (!razorpay.Razorpay) throw new Error('Payment checkout is unavailable.')
}

export default function SubscriptionCheckout({ businessId }: { businessId: number }) {
  const [payment, setPayment] = useState<CheckoutSummary | null>(null)
  const [busy, setBusy] = useState(false)
  const [open, setOpen] = useState(false)
  const [error, setError] = useState('')
  const [notice, setNotice] = useState('')

  const [refresh, setRefresh] = useState(0)
  useEffect(() => {
    setError('')
    setPayment(null)
    let active = true
    api<CheckoutSummary>(`/businesses/${businessId}/billing/checkout`)
      .then(result => {
        if (!active) return
        setPayment(result)
        if (result.status === 'Captured') setNotice('Payment verified by the server.')
      })
      .catch(cause => {
        if (active && !(cause instanceof SessionExpiredError))
          setError('Unable to load subscription payment details.')
      })
    return () => { active = false }
  }, [businessId, refresh])

  async function pay() {
    if (!payment?.paymentRequestId || busy || open) return
    setBusy(true)
    setError('')
    setNotice('')
    try {
      await loadCheckoutScript()
      const order = await post<CheckoutOrder>(`/businesses/${businessId}/billing/checkout`, {
        paymentRequestId: payment.paymentRequestId
      })
      const Razorpay = (window as RazorpayWindow).Razorpay
      if (!Razorpay) throw new Error('Payment checkout is unavailable.')
      new Razorpay({
        key: order.keyId,
        amount: order.amount,
        currency: order.currency,
        order_id: order.orderId,
        name: 'Business SaaS',
        description: `Subscription request #${order.paymentRequestId}`,
        modal: { ondismiss: () => setOpen(false) },
        handler: () => {
          setOpen(false)
          setNotice('Payment submitted. Checking server verification.')
          setRefresh(n => n + 1)
        }
      }).open()
      setOpen(true)
    } catch (cause) {
      if (!(cause instanceof SessionExpiredError))
        setError(cause instanceof Error ? cause.message : 'Unable to open checkout.')
    } finally {
      setBusy(false)
    }
  }

  if (payment && !payment.enabled) return null
  if (!payment || !payment.available) return <section className="content-section" aria-label="Subscription payment">
    <h2>Subscription payment</h2>
    {error ? <p role="alert">{error}</p> : <p role="status">{!payment ? 'Checking payment status…' :
      'No current subscription payment request.'}</p>}
    <button className="btn btn-outline-secondary" disabled={!payment && !error}
      onClick={() => setRefresh(n => n + 1)}>{error ? 'Retry' : 'Refresh status'}</button>
  </section>

  return <section className="content-section" aria-label="Subscription payment">
    <h2>Subscription payment</h2>
    <p className="small text-secondary">
      Request #{payment.paymentRequestId} · {payment.currencyCode} {payment.amount?.toFixed(2)}
      {' · '}{payment.status}
    </p>
    {error && <p className="alert alert-danger" role="alert">{error}</p>}
    {notice && <p className="alert alert-info" role="status">{notice}</p>}
    <button className="btn btn-link" type="button" onClick={() => setRefresh(n => n + 1)}>Refresh status</button>
    {payment.canPay && !notice
      ? <button className="btn btn-primary" type="button" disabled={busy || open}
          onClick={() => void pay()}>{busy ? 'Opening checkout…' : 'Pay securely'}</button>
      : <p className="small text-secondary">
          Status: {payment.status}. {payment.status === 'Captured' ? 'Payment verified.' :
            'Check status again or contact support before starting another payment.'}
        </p>}
  </section>
}
