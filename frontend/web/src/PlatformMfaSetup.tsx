import { useEffect, useState, type FormEvent } from 'react'
import { api, post, SessionExpiredError } from './api'

export default function PlatformMfaSetup({ onFinished }: { onFinished: () => void }) {
  const [enabled, setEnabled] = useState<boolean | null>(null)
  const [password, setPassword] = useState('')
  const [secret, setSecret] = useState('')
  const [uri, setUri] = useState('')
  const [code, setCode] = useState('')
  const [recoveryCodes, setRecoveryCodes] = useState<string[]>([])
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')

  useEffect(() => {
    let active = true
    api<{ enabled: boolean }>('/auth/mfa/status')
      .then(result => { if (active) setEnabled(result.enabled) })
      .catch(cause => {
        if (active && !(cause instanceof SessionExpiredError))
          setError('Unable to load security setup. Sign out and try again.')
      })
    return () => { active = false }
  }, [])

  async function start(event: FormEvent) {
    event.preventDefault()
    setError('')
    setBusy(true)
    try {
      const result = await post<{ secret: string; uri: string }>('/auth/mfa/setup', { password })
      setSecret(result.secret)
      setUri(result.uri)
    } catch (cause) {
      setError(cause instanceof Error ? cause.message : 'Unable to start authenticator setup.')
    } finally {
      setBusy(false)
    }
  }

  async function enable(event: FormEvent) {
    event.preventDefault()
    setError('')
    setBusy(true)
    try {
      const result = await post<{ recoveryCodes: string[] }>('/auth/mfa/enable', { password, code })
      setRecoveryCodes(result.recoveryCodes)
      setPassword('')
      setSecret('')
      setUri('')
      setCode('')
    } catch (cause) {
      setError(cause instanceof Error ? cause.message : 'Unable to enable authenticator.')
    } finally {
      setBusy(false)
    }
  }

  return <div className="page-content">
    <div className="page-header">
      <div><h1>Secure your Super Admin account</h1>
        <p>Authenticator verification is required before accessing platform controls.</p>
      </div>
    </div>
    {error && <div className="alert alert-danger" role="alert">{error}</div>}
    {recoveryCodes.length > 0 ? <section className="content-section">
      <h2>Save your recovery codes</h2>
      <p>Store these one-time codes offline. They will not be shown again.</p>
      <div className="row g-2">
        {recoveryCodes.map(value => <div className="col-6" key={value}><code>{value}</code></div>)}
      </div>
      <button className="btn btn-primary mt-4" type="button" onClick={onFinished}>
        I saved the codes — Sign in again
      </button>
    </section> : enabled === null ? <section className="content-section">
      <p role="status">{error ? 'Security setup unavailable.' : 'Loading security setup…'}</p>
      <button type="button" className="btn btn-outline-secondary" onClick={onFinished}>
        Return to sign in
      </button>
    </section> : enabled ? <section className="content-section">
      <h2>Authenticator already enabled</h2>
      <p>Sign out and sign in again to complete verification.</p>
      <button className="btn btn-primary" type="button" onClick={onFinished}>Return to sign in</button>
    </section> : <section className="content-section">
      {!secret ? <form onSubmit={event => void start(event)}>
        <h2>1. Connect authenticator app</h2>
        <p>Re-enter your account password. Keep the setup key private.</p>
        <label className="form-label" htmlFor="mfa-password">Account password</label>
        <input className="form-control" id="mfa-password" type="password"
          autoComplete="current-password" value={password} required
          onChange={event => setPassword(event.target.value)} />
        <button className="btn btn-primary mt-3" disabled={busy} type="submit">
          {busy ? 'Please wait…' : 'Generate setup key'}
        </button>
      </form> : <form onSubmit={event => void enable(event)}>
        <h2>2. Verify authenticator</h2>
        <p>Add the setup key to an authenticator app, then enter its 6-digit code.</p>
        <label className="form-label" htmlFor="mfa-secret">Setup key</label>
        <input id="mfa-secret" className="form-control" value={secret} readOnly
          onFocus={event => event.currentTarget.select()} />
        <details className="mt-2"><summary>Setup URI</summary>
          <code className="text-break">{uri}</code>
        </details>
        <label className="form-label mt-3" htmlFor="mfa-confirm">6-digit app code</label>
        <input id="mfa-confirm" className="form-control" type="text" inputMode="numeric"
          autoComplete="one-time-code" pattern="[0-9]{6}" maxLength={6}
          value={code} required onChange={event => setCode(event.target.value)} />
        <button className="btn btn-primary mt-3" type="submit" disabled={busy}>
          {busy ? 'Verifying…' : 'Enable authenticator'}
        </button>
      </form>}
    </section>}
  </div>
}
