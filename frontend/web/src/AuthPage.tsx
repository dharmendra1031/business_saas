import { useEffect, useState, type FormEvent } from 'react'
import { ArrowLeft, ArrowRight, Building2, ShieldCheck, ShoppingCart } from 'lucide-react'
import { api, post, resetCsrf, type Business, type User } from './api'
import { accessContent, type AccessPortal } from './WorkspaceEntry'

export default function AuthPage({ mode, portal, onBack, onChange, onSignedIn, sessionExpired, authNotice, onRegistered }: {
  portal: AccessPortal
  onBack: () => void
  mode: 'login' | 'register'
  onChange: (page: 'login' | 'register') => void
  onSignedIn: (user: User, businesses: Business[]) => void
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
            setSecondFactor(true)
            return
          }
        }
        resetCsrf()
        const me = await api<User>('/auth/me')
        const list = me.isPlatformOwner ? [] : await api<Business[]>('/businesses')
        const wrongWorkspace =
          portal === 'platform' ? !me.isPlatformOwner
            : me.isPlatformOwner || (portal === 'pos'
              ? !list.some(b => b.roleCode === 'Cashier')
              : list.length > 0 && !list.some(b => b.roleCode === 'Owner' || b.roleCode === 'Manager'))
        if (wrongWorkspace) {
          await api<void>('/auth/logout', { method: 'POST' })
          resetCsrf()
          setError('This account has no access to the selected workspace. Choose the workspace assigned by your administrator.')
          return
        }
        onSignedIn(me, list)
      }
    } catch (cause) {
      setError(cause instanceof Error ? cause.message : 'Something went wrong. Please try again.')
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className={`auth-layout auth-layout-${portal}`}>
      <aside className="auth-aside">
        <div className="brand"><span className="brand-mark">B</span><div>Business SaaS<small>{accessContent[portal].label}</small></div></div>
        <div className="auth-intro">
          <div className="auth-overline">{accessContent[portal].eyebrow}</div>
          <h1>{accessContent[portal].title}</h1>
          <p>{accessContent[portal].description}</p>
          <div className="auth-assurance"><ShieldCheck size={18} /> Access is verified against your assigned account and business role.</div>
        </div>
        <span className="auth-bottom">Business SaaS · Secure {accessContent[portal].label} access</span>
      </aside>
      <main className="auth-content">
        <div className="auth-form-container">
          <button type="button" className="auth-back" onClick={onBack} disabled={busy}>
            <ArrowLeft size={17} /> All workspaces
          </button>
          <form className="auth-card" onSubmit={event => void submit(event)}>
          <div className="auth-portal-icon">{portal === 'platform' ? <ShieldCheck size={25} /> :
            portal === 'business' ? <Building2 size={25} /> : <ShoppingCart size={25} />}</div>
          <span className="form-eyebrow">{accessContent[portal].eyebrow}</span>
          <h2>{secondFactor ? 'Verify your sign in' :
            mode === 'register' ? 'Create your account' : accessContent[portal].button}</h2>
          <p className="form-hint">{secondFactor
            ? 'Enter the code from your authenticator app or a recovery code.'
            : mode === 'login' ? 'Sign in with your assigned account. Access is checked after authentication.'
              : 'Creating an account does not create a business or grant workspace permissions.'}</p>
          {error && <div className="alert alert-danger" role="alert">{error}</div>}
          {sessionExpired && mode === 'login' && <div className="alert alert-warning" role="status">Your session expired. Please sign in again.</div>}
          {authNotice && mode === 'login' && <div className="alert alert-success" role="status">{authNotice}</div>}
          {secondFactor ? <>
            <label className="form-label" htmlFor="verification-code">
              {usingRecovery ? 'Recovery code' : '6-digit authenticator code'}
            </label>
            <input className="form-control" id="verification-code" type="text"
              inputMode={usingRecovery ? 'text' : 'numeric'} autoComplete="one-time-code"
              maxLength={usingRecovery ? 32 : 6} value={verificationCode}
              onChange={event => setVerificationCode(event.target.value)} required autoFocus />
            <button type="button" className="btn btn-link btn-sm ps-0 mt-2"
              onClick={() => { setUsingRecovery(value => !value); setVerificationCode(''); setError('') }}>
              {usingRecovery ? 'Use authenticator app instead' : 'Use a recovery code'}
            </button>
          </> : <>
            <label className="form-label" htmlFor="email">Email address</label>
            <input className="form-control" id="email" type="email" autoComplete="email" value={email}
              onChange={event => setEmail(event.target.value)} required autoFocus />
            <label className="form-label mt-3" htmlFor="password">Password</label>
            <input className="form-control" id="password" type="password"
              autoComplete={mode === 'login' ? 'current-password' : 'new-password'} minLength={8}
              value={password} onChange={event => setPassword(event.target.value)} required />
          </>}
          <button className="btn btn-primary w-100 mt-4" type="submit" disabled={busy}>
            {busy ? 'Please wait…' : secondFactor ? 'Verify and sign in' : mode === 'login' ? 'Sign in' : 'Create account'} <ArrowRight size={16} />
          </button>
          {secondFactor && <button type="button" className="btn btn-link w-100"
            onClick={() => { setSecondFactor(false); setVerificationCode(''); setError('') }}>
            Back to password sign in
          </button>}
          {!secondFactor && portal !== 'platform' && registrationEnabled ? (
            <p className="auth-switch">{mode === 'login' ? 'New to Business SaaS?' : 'Already have an account?'}{' '}
              <button type="button" onClick={() => { setError(''); onChange(mode === 'login' ? 'register' : 'login') }}>
                {mode === 'login' ? 'Create account' : 'Sign in'}
              </button>
            </p>
          ) : portal !== 'platform' && registrationEnabled === false && mode === 'login' ? (
            <p className="auth-switch">Need access? Ask your platform administrator to assign you to a business.</p>
          ) : null}
          </form>
          <p className="auth-helper-note">{portal === 'platform' ? 'Platform accounts require approved Super Admin access and MFA.' :
            portal === 'pos' ? 'Cashier access requires an assigned business role and store location.' :
              'You can only manage businesses assigned to your account.'}</p>
        </div>
      </main>
    </div>
  )
}
