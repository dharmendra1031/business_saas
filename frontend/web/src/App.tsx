import { useEffect, useState, type FormEvent } from 'react'
import { ArrowLeft, ArrowRight, Building2, CheckCircle2, LayoutDashboard, LogOut, Menu, Plus, ShieldCheck, Store, UserRound } from 'lucide-react'
import { api, post, resetCsrf, type Business, type BusinessMarket, type BusinessType, type RetailDashboard, type User } from './api'

type Page = 'loading' | 'login' | 'register' | 'businesses' | 'create' | 'retail'

function operationKey(): string {
  const bytes = crypto.getRandomValues(new Uint8Array(16))
  return Array.from(bytes, value => value.toString(16).padStart(2, '0')).join('')
}

export default function App() {
  const [page, setPage] = useState<Page>('loading')
  const [user, setUser] = useState<User | null>(null)
  const [businesses, setBusinesses] = useState<Business[]>([])
  const [dashboard, setDashboard] = useState<RetailDashboard | null>(null)
  const [error, setError] = useState('')
  const [notice, setNotice] = useState('')
  const [menuOpen, setMenuOpen] = useState(false)

  useEffect(() => {
    api<User>('/auth/me')
      .then(async me => {
        setUser(me)
        const list = await api<Business[]>('/businesses')
        setBusinesses(list)
        setPage('businesses')
      })
      .catch(() => setPage('login'))
  }, [])

  async function loadBusinesses() {
    const list = await api<Business[]>('/businesses')
    setBusinesses(list)
    setDashboard(null)
    setPage('businesses')
  }

  async function openBusiness(id: number) {
    setError('')
    try {
      const details = await api<RetailDashboard>(`/businesses/${id}/dashboard`)
      setDashboard(details)
      setPage('retail')
    } catch (cause) {
      setError(message(cause))
    }
  }

  async function logout() {
    try {
      await api<void>('/auth/logout', { method: 'POST' })
      resetCsrf()
      setUser(null)
      setDashboard(null)
      setBusinesses([])
      setPage('login')
      setError('')
      setNotice('')
    } catch (cause) {
      setError(message(cause))
    }
  }

  function onSignedIn(next: User, list: Business[]) {
    resetCsrf()
    setUser(next)
    setBusinesses(list)
    setPage('businesses')
    setError('')
  }

  if (page === 'loading')
    return <main className="center-screen" aria-live="polite">Loading your workspace…</main>

  if (page === 'login' || page === 'register')
    return <AuthPage mode={page} onChange={setPage} onSignedIn={onSignedIn} />

  return (
    <div className="app-shell">
      {menuOpen && <button type="button" className="sidebar-scrim" aria-label="Close navigation" onClick={() => setMenuOpen(false)} />}
      <aside id="main-navigation" className={`app-sidebar${menuOpen ? ' is-open' : ''}`}>
        <div className="brand"><span className="brand-mark">B</span><div>Business SaaS<small>Business Workspace</small></div></div>
        <div className="sidebar-heading">WORKSPACE</div>
        <button className="nav-entry active" onClick={() => { setMenuOpen(false); setError(''); void loadBusinesses() }}>
          <Building2 size={18} /> My Businesses
        </button>
        {page === 'retail' && <div className="nav-entry active"><LayoutDashboard size={18} /> Retail Dashboard</div>}
        <div className="sidebar-footer">A workspace for every business.</div>
      </aside>

      <div className="app-main">
        <header className="app-topbar">
          <div className="topbar-title">
            <button type="button" className="mobile-menu-toggle" aria-label={menuOpen ? 'Close navigation' : 'Open navigation'}
              aria-controls="main-navigation" aria-expanded={menuOpen} onClick={() => setMenuOpen(open => !open)}>
              <Menu size={20} />
            </button>
            {page === 'retail' ? dashboard?.business.name : 'My Businesses'}
          </div>
          <div className="topbar-account"><UserRound size={17} /> <span className="account-email">{user?.email}</span>
            <button className="btn btn-outline-secondary btn-sm" onClick={() => void logout()}><LogOut size={15} /> Sign out</button>
          </div>
        </header>

        <main className="page-content">
          {error && <div className="alert alert-danger" role="alert">{error}</div>}
          {notice && <div className="alert alert-success" role="status">{notice}</div>}

          {page === 'businesses' && (
            <>
              <div className="page-header">
                <div><h1>My Businesses</h1><p>Choose a business to continue, or create a new one.</p></div>
                <button className="btn btn-primary" onClick={() => { setNotice(''); setError(''); setPage('create') }}>
                  <Plus size={18} /> Create Business
                </button>
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
                  ? <span className="workspace-state"><CheckCircle2 size={16} /> Trial active</span>
                  : <span className="workspace-state">Plan required</span>}
              </div>
              {!dashboard.canOperate && <div className="alert alert-warning" role="status">
                This business does not have an active plan or trial. Transactions will remain unavailable.
              </div>}
              {dashboard.subscription && <p className="small text-secondary">
                {dashboard.subscription.planCode} · {dashboard.subscription.status}
                {dashboard.subscription.status === 'Trial' &&
                  ` · Ends ${new Date(dashboard.subscription.trialEndsAtUtc).toLocaleDateString()}`}
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
              <section className="next-steps">
                <ShieldCheck size={22} />
                <div><strong>Foundation is ready</strong>
                  <p>Account, business ownership and location access are active. Product, Purchase, Stock and POS screens will be added in the Retail phase. No sales figures are shown until real transactions exist.</p>
                </div>
              </section>
            </>
          )}
        </main>
      </div>
    </div>
  )
}

function AuthPage({ mode, onChange, onSignedIn }: {
  mode: 'login' | 'register'
  onChange: (page: Page) => void
  onSignedIn: (user: User, businesses: Business[]) => void
}) {
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  const [notice, setNotice] = useState('')

  async function submit(event: FormEvent) {
    event.preventDefault()
    setBusy(true)
    setError('')
    try {
      if (mode === 'register') {
        await post('/auth/register', { email, password })
        setPassword('')
        setNotice('Account created. You can now sign in.')
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
          <h1>Everything your business needs, in one place.</h1>
          <p>Create a workspace, set up your shop and get ready for daily operations.</p>
        </div>
        <span className="auth-bottom">Business SaaS · Foundation Release</span>
      </aside>
      <main className="auth-content">
        <form className="auth-card" onSubmit={event => void submit(event)}>
          <span className="form-eyebrow">ACCOUNT ACCESS</span>
          <h2>{mode === 'login' ? 'Sign in' : 'Create your account'}</h2>
          <p className="form-hint">{mode === 'login' ? 'Access your business workspace.' : 'Start with your email and a secure password.'}</p>
          {error && <div className="alert alert-danger" role="alert">{error}</div>}
          {notice && <div className="alert alert-success" role="status">{notice}</div>}
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
          <p className="auth-switch">{mode === 'login' ? 'New to Business SaaS?' : 'Already have an account?'}{' '}
            <button type="button" onClick={() => { setError(''); onChange(mode === 'login' ? 'register' : 'login') }}>
              {mode === 'login' ? 'Create account' : 'Sign in'}
            </button>
          </p>
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
  const [country, setCountry] = useState('IN')
  const market = markets.find(item => item.code === country)
  const [requestKey] = useState(operationKey)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')

  useEffect(() => {
    Promise.all([
      api<BusinessType[]>('/business-types'),
      api<BusinessMarket[]>('/business-markets')
    ]).then(([list, supportedMarkets]) => {
      setTypes(list)
      setTypeId(list[0]?.id ?? 0)
      setMarkets(supportedMarkets)
      setCountry(supportedMarkets[0]?.code ?? '')
    }).catch(cause => setError(message(cause)))
  }, [])

  async function create(event: FormEvent) {
    event.preventDefault()
    setBusy(true)
    setError('')
    try {
      const result = await post<{ id: number }>('/businesses', {
        name, businessTypeId: typeId, countryCode: country,
        currencyCode: market?.currencyCode, timeZoneId: market?.timeZoneId, requestKey
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
        <form onSubmit={event => void create(event)}>
          {error && <div className="alert alert-danger" role="alert">{error}</div>}
          <div className="form-grid">
            <div><label className="form-label" htmlFor="businessName">Business name *</label>
              <input id="businessName" className="form-control" value={name} onChange={event => setName(event.target.value)}
                maxLength={150} minLength={2} required autoFocus placeholder="Enter business name" /></div>
            <div><label className="form-label" htmlFor="businessType">Business type *</label>
              <select id="businessType" className="form-select" value={typeId}
                onChange={event => setTypeId(Number(event.target.value))} required>
                {types.length === 0 && <option value={0}>No business types available</option>}
                {types.map(type => <option value={type.id} key={type.id}>{type.name}</option>)}
              </select></div>
            <div><label className="form-label" htmlFor="country">Country *</label>
              <select id="country" className="form-select" value={country}
                onChange={event => setCountry(event.target.value)} required>
                {markets.length === 0 && <option value="">No markets available</option>}
                {markets.map(item => <option value={item.code} key={item.code}>{item.name}</option>)}
              </select></div>
            <div><label className="form-label" htmlFor="currency">Currency</label>
              <input id="currency" className="form-control" value={market?.currencyCode ?? ''} readOnly /></div>
            <div className="form-wide"><label className="form-label" htmlFor="timezone">Timezone</label>
              <input id="timezone" className="form-control" value={market?.timeZoneId ?? ''} readOnly />
              <div className="field-help">Currency and timezone are set from the selected country.</div>
            </div>
          </div>
          <div className="form-footer"><button type="button" className="btn btn-outline-secondary" onClick={onCancel}>Cancel</button>
            <button type="submit" className="btn btn-primary" disabled={busy || typeId === 0 || !market}>
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
