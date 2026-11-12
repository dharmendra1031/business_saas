import { useEffect, useRef, useState, type FormEvent } from 'react'
import { ArrowLeft, ArrowRight, Building2, CheckCircle2, LayoutDashboard, LogOut, Menu, Plus, ShieldCheck, Store, UserRound } from 'lucide-react'
import { api, post, resetCsrf, SessionExpiredError, setSessionExpiredHandler, type Business, type BusinessMarket, type BusinessType, type RetailDashboard, type User } from './api'
import TeamMembers from './TeamMembers'
import PlatformAdmin from './PlatformAdmin'

type Page = 'loading' | 'login' | 'register' | 'businesses' | 'create' | 'retail' | 'platform'

function operationKey(): string {
  const bytes = crypto.getRandomValues(new Uint8Array(16))
  return Array.from(bytes, value => value.toString(16).padStart(2, '0')).join('')
}

export default function App() {
  const [page, setPage] = useState<Page>('loading')
  const [user, setUser] = useState<User | null>(null)
  const [businesses, setBusinesses] = useState<Business[]>([])
  const [canViewPlatform, setCanViewPlatform] = useState(false)
  const [dashboard, setDashboard] = useState<RetailDashboard | null>(null)
  const [error, setError] = useState('')
  const [notice, setNotice] = useState('')
  const [menuOpen, setMenuOpen] = useState(false)
  const menuToggle = useRef<HTMLButtonElement>(null)
  const businessNav = useRef<HTMLButtonElement>(null)
  const [sessionExpired, setSessionExpired] = useState(false)
  const [authNotice, setAuthNotice] = useState('')

  useEffect(() => {
    setSessionExpiredHandler(() => {
      resetCsrf()
      setUser(null)
      setCanViewPlatform(false)
      setBusinesses([])
      setDashboard(null)
      setMenuOpen(false)
      setError('')
      setSessionExpired(true)
      setAuthNotice('')
      setPage('login')
    })

    api<User>('/auth/me')
      .then(async me => {
        setUser(me)
        const list = await api<Business[]>('/businesses')
        setBusinesses(list)
        setPage('businesses')
      })
      .catch(() => setPage('login'))
    return () => setSessionExpiredHandler(null)
  }, [])

  useEffect(() => {
    if (!user) return
    let active = true

    api<{ canViewBusinesses: boolean }>('/platform/access')
      .then(access => { if (active) setCanViewPlatform(access.canViewBusinesses) })
      .catch(() => { if (active) setCanViewPlatform(false) })

    return () => { active = false }
  }, [user])

  useEffect(() => {
    if (!menuOpen) return

    businessNav.current?.focus()
    function onKeyDown(event: KeyboardEvent) {
      if (event.key !== 'Escape') return
      setMenuOpen(false)
      menuToggle.current?.focus()
    }

    window.addEventListener('keydown', onKeyDown)
    return () => window.removeEventListener('keydown', onKeyDown)
  }, [menuOpen])

  async function loadBusinesses() {
    try {
      const list = await api<Business[]>('/businesses')
      setBusinesses(list)
      setDashboard(null)
      setPage('businesses')
    } catch (cause) {
      if (!(cause instanceof SessionExpiredError))
        setError(message(cause))
    }
  }

  async function openBusiness(id: number) {
    setError('')
    try {
      const details = await api<RetailDashboard>(`/businesses/${id}/dashboard`)
      setDashboard(details)
      setPage('retail')
    } catch (cause) {
      if (!(cause instanceof SessionExpiredError))
        setError(message(cause))
    }
  }

  async function logout() {
    try {
      await api<void>('/auth/logout', { method: 'POST' })
      resetCsrf()
      setUser(null)
      setCanViewPlatform(false)
      setDashboard(null)
      setBusinesses([])
      setPage('login')
      setSessionExpired(false)
      setAuthNotice('')
      setError('')
      setNotice('')
    } catch (cause) {
      setError(message(cause))
    }
  }

  function onSignedIn(next: User, list: Business[]) {
    resetCsrf()
    setUser(next)
    setCanViewPlatform(false)
    setBusinesses(list)
    setSessionExpired(false)
    setAuthNotice('')
    setPage('businesses')
    setError('')
  }

  if (page === 'loading')
    return <main className="center-screen" aria-live="polite">Loading your workspace…</main>

  if (page === 'login' || page === 'register')
    return <AuthPage key={page} mode={page} onChange={setPage} onSignedIn={onSignedIn}
      sessionExpired={sessionExpired} authNotice={authNotice} onRegistered={setAuthNotice} />

  return (
    <div className="app-shell">
      {menuOpen && <button type="button" className="sidebar-scrim" aria-label="Close navigation"
        onClick={() => { setMenuOpen(false); menuToggle.current?.focus() }} />}
      <aside id="main-navigation" className={`app-sidebar${menuOpen ? ' is-open' : ''}`}>
        <div className="brand"><span className="brand-mark">B</span><div>Business SaaS<small>Business Workspace</small></div></div>
        <div className="sidebar-heading">WORKSPACE</div>
        <button type="button" ref={businessNav} className={`nav-entry${page === 'businesses' || page === 'create' ? ' active' : ''}`}
          aria-current={page === 'businesses' || page === 'create' ? 'page' : undefined}
          onClick={() => {
            if (menuOpen) menuToggle.current?.focus()
            setMenuOpen(false)
            setError('')
            void loadBusinesses()
          }}>
          <Building2 size={18} /> My Businesses
        </button>
        {page === 'retail' && <div className="nav-entry active" aria-current="page">
          <LayoutDashboard size={18} /> Retail Dashboard
        </div>}
        {canViewPlatform && <button type="button"
          className={`nav-entry${page === 'platform' ? ' active' : ''}`}
          aria-current={page === 'platform' ? 'page' : undefined}
          onClick={() => {
            if (menuOpen) menuToggle.current?.focus()
            setMenuOpen(false)
            setDashboard(null)
            setError('')
            setNotice('')
            setPage('platform')
          }}>
          <ShieldCheck size={18} /> Platform Admin
        </button>}
        <div className="sidebar-footer">A workspace for every business.</div>
      </aside>

      <div className="app-main">
        <header className="app-topbar">
          <div className="topbar-title">
            <button type="button" ref={menuToggle} className="mobile-menu-toggle" aria-label={menuOpen ? 'Close navigation' : 'Open navigation'}
              aria-controls="main-navigation" aria-expanded={menuOpen} onClick={() => setMenuOpen(open => !open)}>
              <Menu size={20} />
            </button>
            {page === 'retail' ? dashboard?.business.name
              : page === 'create' ? 'Create Business'
              : page === 'platform' ? 'Platform Admin' : 'My Businesses'}
          </div>
          <div className="topbar-account"><UserRound size={17} /> <span className="account-email">{user?.email}</span>
            <button className="btn btn-outline-secondary btn-sm" onClick={() => void logout()}><LogOut size={15} /> Sign out</button>
          </div>
        </header>

        <main className="page-content">
          {error && <div className="alert alert-danger" role="alert">{error}</div>}
          {notice && <div className="alert alert-success" role="status">{notice}</div>}

          {page === 'platform' && canViewPlatform && <PlatformAdmin />}

          {page === 'businesses' && (
            <>
              <div className="page-header">
                <div><h1>My Businesses</h1><p>Choose a business to continue, or create a new one.</p></div>
                {businesses.length > 0 && <button className="btn btn-primary" onClick={() => { setNotice(''); setError(''); setPage('create') }}>
                  <Plus size={18} /> Create Business
                </button>}
              </div>
              {businesses.length === 0 ? (
                <section className="empty-state">
                  <Store size={32} />
                  <h2>Set up your first business</h2>
                  <p>Create a retail workspace to manage your shop. No sample transactions will be added.</p>
                  <button className="btn btn-primary" onClick={() => setPage('create')}>Create Business <ArrowRight size={16} /></button>
                </section>
              ) : (
                <div className="business-list">
                  {businesses.map(business => (
                    <button className="business-row" key={business.id} onClick={() => void openBusiness(business.id)}>
                      <span className="shop-symbol"><Store size={22} /></span>
                      <span className="business-summary"><strong>{business.name}</strong><small>{business.typeName} · {business.roleCode} · {business.countryCode}</small></span>
                      <ArrowRight size={18} />
                    </button>
                  ))}
                </div>
              )}
            </>
          )}

          {page === 'create' && (
            <CreateBusiness onCancel={() => setPage('businesses')} onCreated={async id => {
              await loadBusinesses()
              setNotice('Business created successfully.')
              await openBusiness(id)
            }} />
          )}

          {page === 'retail' && dashboard && (
            <>
              <div className="page-header">
                <div><button className="back-link" onClick={() => { setNotice(''); void loadBusinesses() }}><ArrowLeft size={16} /> All businesses</button>
                  <h1>Retail Dashboard</h1><p>{dashboard.business.name} · {dashboard.business.typeName}</p>
                </div>
                {dashboard.canOperate
                  ? <span className="workspace-state"><CheckCircle2 size={16} /> {dashboard.accessStatus === 'Trial' ? 'Trial active' : 'Plan active'}</span>
                  : <span className="workspace-state inactive">
                    {dashboard.accessStatus === 'TrialExpired' ? 'Trial expired' : 'Subscription inactive'}
                  </span>}
              </div>
              {!dashboard.canOperate && <div className="alert alert-warning" role="status">
                {dashboard.accessStatus === 'TrialExpired'
                  ? 'Your free trial has ended. Subscription activation is not available yet. Contact the platform administrator.'
                  : 'This business has no active subscription. Contact the platform administrator for help.'}
              </div>}
              {dashboard.subscription && <p className="small text-secondary">
                {dashboard.subscription.planCode} · {dashboard.accessStatus === 'TrialExpired'
                  ? 'Trial expired' : dashboard.subscription.status}
                {dashboard.subscription.status === 'Trial' &&
                  ` · ${dashboard.accessStatus === 'TrialExpired' ? 'Ended' : 'Ends'} ${new Date(dashboard.subscription.trialEndsAtUtc).toLocaleDateString()}`}
              </p>}
              <div className="detail-grid">
                <section className="content-section">
                  <h2>Store Details</h2>
                  <dl className="detail-list">
                    <div><dt>Business</dt><dd>{dashboard.business.name}</dd></div>
                    <div><dt>Business Type</dt><dd>{dashboard.business.typeName}</dd></div>
                    <div><dt>Country</dt><dd>{dashboard.business.countryCode}</dd></div>
                    <div><dt>Currency</dt><dd>{dashboard.business.currencyCode}</dd></div>
                    <div><dt>Timezone</dt><dd>{dashboard.business.timeZoneId}</dd></div>
                  </dl>
                </section>
                <section className="content-section">
                  <h2>Shop Locations</h2>
                  {dashboard.locations.map(location => (
                    <div className="location-row" key={location.id}><Store size={18} /><div><strong>{location.name}</strong><small>{location.code}</small></div></div>
                  ))}
                </section>
              </div>
              {dashboard.roleCode === 'Owner' &&
                <TeamMembers businessId={dashboard.business.id} />}
              <section className="next-steps">
                <ShieldCheck size={22} />
                <div><strong>Retail features are coming later</strong>
                  <p>Product, Purchase, Stock and POS are not available yet. {dashboard.roleCode === 'Owner'
                    ? 'You can manage team access above.'
                    : 'Ask your business owner about access.'} No sample sales figures are shown.</p>
                </div>
              </section>
            </>
          )}
        </main>
      </div>
    </div>
  )
}

function AuthPage({ mode, onChange, onSignedIn, sessionExpired, authNotice, onRegistered }: {
  mode: 'login' | 'register'
  onChange: (page: Page) => void
  onSignedIn: (user: User, businesses: Business[]) => void
  sessionExpired: boolean
  authNotice: string
  onRegistered: (notice: string) => void
}) {
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
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
        await post('/auth/login', { email, password })
        resetCsrf()
        const me = await api<User>('/auth/me')
        const list = await api<Business[]>('/businesses')
        onSignedIn(me, list)
      }
    } catch (cause) {
      setError(message(cause))
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className="auth-layout">
      <aside className="auth-aside"><div className="brand"><span className="brand-mark">B</span><div>Business SaaS<small>Business Workspace</small></div></div>
        <div className="auth-intro"><div className="auth-overline">ONE ACCOUNT. MULTIPLE BUSINESSES.</div>
          <h1>One account for your businesses.</h1>
          <p>Set up your Retail workspace today. Product, stock and billing features are still in development.</p>
        </div>
        <span className="auth-bottom">Business SaaS · Foundation Release</span>
      </aside>
      <main className="auth-content">
        <form className="auth-card" onSubmit={event => void submit(event)}>
          <span className="form-eyebrow">ACCOUNT ACCESS</span>
          <h2>{mode === 'login' ? 'Sign in' : 'Create your account'}</h2>
          <p className="form-hint">{mode === 'login' ? 'Access your business workspace.' : 'Start with your email and a secure password.'}</p>
          {error && <div className="alert alert-danger" role="alert">{error}</div>}
          {sessionExpired && mode === 'login' && <div className="alert alert-warning" role="status">Your session expired. Please sign in again.</div>}
          {authNotice && mode === 'login' && <div className="alert alert-success" role="status">{authNotice}</div>}
          <label className="form-label" htmlFor="email">Email address</label>
          <input className="form-control" id="email" type="email" autoComplete="email" value={email}
            onChange={event => setEmail(event.target.value)} required autoFocus />
          <label className="form-label mt-3" htmlFor="password">Password</label>
          <input className="form-control" id="password" type="password"
            autoComplete={mode === 'login' ? 'current-password' : 'new-password'} minLength={8}
            value={password} onChange={event => setPassword(event.target.value)} required />
          <button className="btn btn-primary w-100 mt-4" type="submit" disabled={busy}>
            {busy ? 'Please wait…' : mode === 'login' ? 'Sign in' : 'Create account'} <ArrowRight size={16} />
          </button>
          {registrationEnabled ? (
            <p className="auth-switch">{mode === 'login' ? 'New to Business SaaS?' : 'Already have an account?'}{' '}
              <button type="button" onClick={() => { setError(''); onChange(mode === 'login' ? 'register' : 'login') }}>
                {mode === 'login' ? 'Create account' : 'Sign in'}
              </button>
            </p>
          ) : registrationEnabled === false && mode === 'login' ? (
            <p className="auth-switch">New account registration is not available yet.</p>
          ) : null}
        </form>
      </main>
    </div>
  )
}

function CreateBusiness({ onCancel, onCreated }: {
  onCancel: () => void
  onCreated: (id: number) => Promise<void>
}) {
  const [types, setTypes] = useState<BusinessType[]>([])
  const [typeId, setTypeId] = useState(0)
  const [name, setName] = useState('')
  const [markets, setMarkets] = useState<BusinessMarket[]>([])
  const [countrySearch, setCountrySearch] = useState('')
  const [selectedCurrency, setSelectedCurrency] = useState('')
  const [selectedTimeZone, setSelectedTimeZone] = useState('')
  const market = markets.find(item =>
    `${item.name} (${item.code})`.toLowerCase() === countrySearch.trim().toLowerCase() ||
    item.name.toLowerCase() === countrySearch.trim().toLowerCase() ||
    item.code.toLowerCase() === countrySearch.trim().toLowerCase())
  const currency = market?.currencies.includes(selectedCurrency) ? selectedCurrency : market?.currencies[0] ?? ''
  const timeZone = market?.timeZones.includes(selectedTimeZone) ? selectedTimeZone : market?.timeZones[0] ?? ''
  const [requestKey] = useState(operationKey)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  const [loadingOptions, setLoadingOptions] = useState(true)
  const [loadFailed, setLoadFailed] = useState(false)
  const [retryOptions, setRetryOptions] = useState(0)

  useEffect(() => {
    let active = true
    setLoadingOptions(true)
    setLoadFailed(false)
    setError('')
    Promise.all([
      api<BusinessType[]>('/business-types'),
      api<BusinessMarket[]>('/business-markets')
    ]).then(([list, supportedMarkets]) => {
      if (!active) return
      setTypes(list)
      setTypeId(list[0]?.id ?? 0)
      setMarkets(supportedMarkets)
    }).catch(cause => {
      if (!active || cause instanceof SessionExpiredError) return
      setError(message(cause))
      setLoadFailed(true)
    }).finally(() => {
      if (active) setLoadingOptions(false)
    })
    return () => { active = false }
  }, [retryOptions])

  async function create(event: FormEvent) {
    event.preventDefault()
    setBusy(true)
    setError('')
    try {
      const result = await post<{ id: number }>('/businesses', {
        name, businessTypeId: typeId, countryCode: market?.code,
        currencyCode: currency, timeZoneId: timeZone, requestKey
      })
      await onCreated(result.id)
    } catch (cause) {
      setError(message(cause))
    } finally {
      setBusy(false)
    }
  }

  return (
    <>
      <div className="page-header"><div>
        <button className="back-link" onClick={onCancel}><ArrowLeft size={16} /> My Businesses</button>
        <h1>Create Business</h1><p>Choose a supported business type and enter your shop details.</p>
      </div></div>
      <section className="content-section form-panel">
        <form onSubmit={event => void create(event)} aria-busy={busy || loadingOptions}>
          {error && <div className="alert alert-danger" role="alert">{error}</div>}
          {loadingOptions && <p className="text-secondary" role="status">Loading business setup options…</p>}
          {loadFailed && <button type="button" className="btn btn-outline-secondary mb-3"
            onClick={() => setRetryOptions(value => value + 1)}>Retry loading options</button>}
          {!loadingOptions && !loadFailed && types.length === 0 &&
            <p className="alert alert-info" role="status">No business types are available yet.</p>}
          <div className="form-grid">
            <div><label className="form-label" htmlFor="businessName">Business name *</label>
              <input id="businessName" className="form-control" value={name} onChange={event => setName(event.target.value)}
                maxLength={150} minLength={2} required autoFocus placeholder="Enter business name" /></div>
            <div><label className="form-label" htmlFor="businessType">Business type *</label>
              <select id="businessType" className="form-select" value={typeId}
                onChange={event => setTypeId(Number(event.target.value))} required>
                {types.length === 0 && <option value={0}>{loadingOptions ? 'Loading business types…' : 'No business types available'}</option>}
                {types.map(type => <option value={type.id} key={type.id}>{type.name}</option>)}
              </select></div>
            <div className="form-wide">
              <label className="form-label" htmlFor="country">Country *</label>
              <input id="country" className="form-control" type="search" list="business-countries"
                aria-describedby={countrySearch && !market ? 'country-help' : undefined}
                aria-invalid={Boolean(countrySearch && !market)}
                autoComplete="off" placeholder="Search country name or ISO code"
                value={countrySearch}
                onChange={event => {
                  setCountrySearch(event.target.value)
                  setSelectedCurrency('')
                  setSelectedTimeZone('')
                }} required />
              <datalist id="business-countries">
                {markets.map(item => <option key={item.code} value={`${item.name} (${item.code})`} />)}
              </datalist>
              {countrySearch && !market && <div id="country-help" className="field-help text-danger">Select a country from the suggestions.</div>}
            </div>
            <div>
              <label className="form-label" htmlFor="currency">Currency *</label>
              <select id="currency" className="form-select" value={currency}
                onChange={event => setSelectedCurrency(event.target.value)} disabled={!market} required>
                {market?.currencies.map(code => <option key={code} value={code}>{code === 'XXX' ? 'No official currency' : code}</option>)}
              </select>
            </div>
            <div>
              <label className="form-label" htmlFor="timezone">Timezone *</label>
              <select id="timezone" className="form-select" value={timeZone}
                onChange={event => setSelectedTimeZone(event.target.value)} disabled={!market} required>
                {market?.timeZones.map(zone => <option key={zone} value={zone}>{zone}</option>)}
              </select>
            </div>
          </div>
          <div className="form-footer"><button type="button" className="btn btn-outline-secondary" onClick={onCancel}>Cancel</button>
            <button type="submit" className="btn btn-primary" disabled={busy || loadingOptions || loadFailed || typeId === 0 || !market}>
              {busy ? 'Creating…' : 'Create Business'} <ArrowRight size={16} />
            </button></div>
        </form>
      </section>
    </>
  )
}

function message(error: unknown): string {
  return error instanceof Error ? error.message : 'Something went wrong. Please try again.'
}
