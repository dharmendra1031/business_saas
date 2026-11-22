import { useEffect, useState, type FormEvent } from 'react'
import { ArrowRight } from 'lucide-react'
import { api, post, resetCsrf } from './api'

export default function AuthPage({ mode, onChange, onAuthenticated, sessionExpired, authNotice, onRegistered }: {
  mode: 'login' | 'register'
  onChange: (page: 'login' | 'register') => void
  onAuthenticated: () => void
  sessionExpired: boolean
  authNotice: string
  onRegistered: (notice: string) => void
}) {
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [secondFactor, setSecondFactor] = useState(false)
  const [usingRecovery, setUsingRecovery] = useState(false)
  const [verificationCode, setVerificationCode] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  const [registrationEnabled, setRegistrationEnabled] = useState<boolean | null>(null)

  useEffect(() => {
    api<{ canRegister: boolean }>('/auth/options')
      .then(options => setRegistrationEnabled(options.canRegister))
      .catch(() => setRegistrationEnabled(false))
  }, [])

  async function submit(event: FormEvent) {
    event.preventDefault()
    setBusy(true)
    setError('')
    try {
      if (mode === 'register') {
        await post('/auth/register', { email, password })
        setPassword('')
        onRegistered('Account created. Sign in to continue.')
        onChange('login')
      } else {
        if (secondFactor) {
          await post<void>('/auth/mfa/verify', {
            code: usingRecovery ? null : verificationCode,
            recoveryCode: usingRecovery ? verificationCode : null
          })
        } else {
          const result = await post<{ requiresTwoFactor: boolean } | undefined>('/auth/login', { email, password })
          if (result?.requiresTwoFactor) {
            setPassword('')
            setVerificationCode('')
            setUsingRecovery(false)
            setSecondFactor(true)
            return
          }
        }
        resetCsrf()
        onAuthenticated()
      }
    } catch (cause) {
      setError(cause instanceof Error ? cause.message : 'Something went wrong. Please try again.')
    } finally {
      setBusy(false)
    }
  }

  return (
    <main className="auth-layout">
      <div className="auth-form-container">
        <div className="auth-brand">
          <span className="auth-brand-mark" aria-hidden="true">B</span>
          <div><strong>Business SaaS</strong><small>Secure account access</small></div>
        </div>
        <form className="auth-card" onSubmit={event => void submit(event)}>
          <h1>{secondFactor ? 'Verify your sign in' : mode === 'register' ? 'Create your account' : 'Sign in'}</h1>
          <p className="form-hint">{secondFactor
            ? 'Enter your authenticator code or a recovery code.'
            : mode === 'register' ? 'Create an account to request business access.'
              : 'Use your assigned email and password.'}</p>
          {error && <div className="alert alert-danger" role="alert">{error}</div>}
          {sessionExpired && mode === 'login' && !secondFactor && <div className="alert alert-warning" role="status">Your session expired. Please sign in again.</div>}
          {authNotice && mode === 'login' && <div className="alert alert-success" role="status">{authNotice}</div>}
          {secondFactor ? <>
            <label className="form-label" htmlFor="verification-code">
              {usingRecovery ? 'Recovery code' : '6-digit authenticator code'}
            </label>
            <input className="form-control" id="verification-code" type="text"
              inputMode={usingRecovery ? 'text' : 'numeric'} autoComplete="one-time-code"
              maxLength={usingRecovery ? 32 : 6} pattern={usingRecovery ? undefined : '[0-9]{6}'}
              spellCheck={false} value={verificationCode}
              onChange={event => { setVerificationCode(event.target.value); setError('') }} required autoFocus />
            <button type="button" className="btn btn-link btn-sm ps-0 mt-2"
              onClick={() => { setUsingRecovery(value => !value); setVerificationCode(''); setError('') }}>
              {usingRecovery ? 'Use authenticator app instead' : 'Use a recovery code'}
            </button>
          </> : <>
            <label className="form-label" htmlFor="email">Email address</label>
            <input className="form-control" id="email" type="email" autoComplete="email" value={email}
              onChange={event => { setEmail(event.target.value); setError('') }} required autoFocus />
            <label className="form-label mt-3" htmlFor="password">Password</label>
            <input className="form-control" id="password" type="password"
              autoComplete={mode === 'login' ? 'current-password' : 'new-password'} minLength={8}
              value={password} onChange={event => { setPassword(event.target.value); setError('') }} required />
          </>}
          <button className="btn btn-primary w-100 mt-4" type="submit" disabled={busy}>
            {busy ? 'Please wait…' : secondFactor ? 'Verify and sign in' : mode === 'login' ? 'Sign in' : 'Create account'} <ArrowRight size={16} />
          </button>
          {secondFactor && <button type="button" className="btn btn-link w-100 mt-2"
            onClick={() => { setSecondFactor(false); setUsingRecovery(false); setVerificationCode(''); setError('') }}>
            Back to password sign in
          </button>}
          {!secondFactor && registrationEnabled && (
            <p className="auth-switch">{mode === 'login' ? 'Need an account?' : 'Already have an account?'}{' '}
              <button type="button" onClick={() => { setError(''); onChange(mode === 'login' ? 'register' : 'login') }}>
                {mode === 'login' ? 'Create account' : 'Sign in'}
              </button>
            </p>
          )}
        </form>
      </div>
    </main>
  )
}
