import { useEffect, useState, type FormEvent } from 'react'
import { ArrowRight, ShieldCheck } from 'lucide-react'
import { post } from './api'

type Preview = { email: string; expiresAtUtc: string }
type Result = { verified: boolean; email: string; message: string }

export default function OwnerInviteAcceptance({ token }: { token: string }) {
  const [preview, setPreview] = useState<Preview | null>(null)
  const [loading, setLoading] = useState(true)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  const [result, setResult] = useState<Result | null>(null)
  const [password, setPassword] = useState('')
  const [confirm, setConfirm] = useState('')

  useEffect(() => {
    let alive = true
    post<Preview>('/auth/owner-invitations/inspect', { token })
      .then(data => { if (alive) setPreview(data) })
      .catch(() => { if (alive) setError('This invitation is invalid, expired or has already been used.') })
      .finally(() => { if (alive) setLoading(false) })
    return () => { alive = false }
  }, [token])

  async function accept(event: FormEvent) {
    event.preventDefault()
    if (busy || !preview || password !== confirm) return
    setBusy(true); setError('')
    try {
      const accepted = await post<Result>('/auth/owner-invitations/accept', { token, password })
      setResult(accepted)
      setPassword(''); setConfirm('')
    } catch (cause) {
      setError(cause instanceof Error ? cause.message : 'Unable to complete invitation.')
    } finally { setBusy(false) }
  }

  return <main className="auth-layout">
    <div className="auth-form-container">
      <div className="auth-brand">
        <span className="auth-brand-mark" aria-hidden="true">B</span>
        <div><strong>Business SaaS</strong><small>Secure account access</small></div>
      </div>
      <section className="auth-card" aria-labelledby="invite-title">
        <h1 id="invite-title">{result ? 'Account verified' : 'Accept Owner invitation'}</h1>
        {error && <div className="alert alert-danger" role="alert">{error}</div>}
        {loading && <p role="status">Checking invitation…</p>}
        {result ? <>
          <p role="status">{result.message}</p>
          <a className="btn btn-primary w-100" href="/">Sign in <ArrowRight size={16}/></a>
        </> : preview && <>
          <p><ShieldCheck size={17} /> Set a password for <strong>{preview.email}</strong>.
            Your email will be verified by this invitation; access is assigned separately by the Super Admin.</p>
          <p className="form-hint">Invitation expires {new Date(preview.expiresAtUtc).toLocaleString()}.</p>
          <form onSubmit={event => void accept(event)}>
            <label className="form-label" htmlFor="invite-password">New password</label>
            <input id="invite-password" className="form-control" type="password"
              autoComplete="new-password" required minLength={8}
              value={password} onChange={event => setPassword(event.target.value)} />
            <label className="form-label mt-3" htmlFor="invite-confirm">Confirm password</label>
            <input id="invite-confirm" className="form-control" type="password"
              autoComplete="new-password" required minLength={8}
              value={confirm} onChange={event => setConfirm(event.target.value)} />
            {confirm && confirm !== password && <p role="alert" className="text-danger mt-2">Passwords must match.</p>}
            <button className="btn btn-primary w-100 mt-4" type="submit"
              disabled={busy || password.length < 8 || password !== confirm}>
              {busy ? 'Creating account…' : 'Verify email & create account'} <ArrowRight size={16}/>
            </button>
          </form>
        </>}
        <a href="/" className="d-block mt-3">Back to sign in</a>
      </section>
    </div>
  </main>
}
