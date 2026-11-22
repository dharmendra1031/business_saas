import { useEffect, useRef, useState, type FormEvent } from 'react'
import { Activity, ArrowLeft, ArrowRight, Boxes, Building2, CheckCircle2, CreditCard, KeyRound, LayoutDashboard, LogOut, Menu, Package, Search, Settings2, ShieldCheck, ShoppingCart, Store, Tags, Truck, UserRound, Users } from 'lucide-react'
import { api, post, resetCsrf, SessionExpiredError, setSessionExpiredHandler, type Business, type RetailDashboard, type User } from './api'
import CashCheckout from './CashCheckout'
import TeamMembers from './TeamMembers'
import SubscriptionCheckout from './SubscriptionCheckout'
import RetailCatalog, { type RetailSection } from './RetailCatalog'
import BusinessOverview from './BusinessOverview'
import PlatformAdmin, { type PlatformSection } from './PlatformAdmin'
import PlatformMfaSetup from './PlatformMfaSetup'
import { WorkspaceEntry, accessContent, type AccessPortal } from './WorkspaceEntry'

type Page = 'loading' | 'gateway' | 'login' | 'register' | 'mfa-setup' | 'businesses' | 'retail' | 'platform'

function visibleBusinessesForPortal(items: Business[], portal: AccessPortal | null): Business[] {
  if (portal === 'pos') return items.filter(item => item.roleCode === 'Cashier')
  if (portal === 'business') return items.filter(item => item.roleCode === 'Owner' || item.roleCode === 'Manager')
  return items
}

function selectedPortalFromUrl(): AccessPortal | null {
  const value = new URLSearchParams(window.location.search).get('workspace')
  return value === 'platform' || value === 'business' || value === 'pos' ? value : null
}
type RetailPage = RetailSection | 'cash' | 'overview' | 'team'
const retailTitles: Record<RetailPage, string> = {
  cash: 'POS Checkout', overview: 'Business Dashboard', products: 'Products', stock: 'Stock', purchases: 'Purchase Orders',
  masters: 'Categories & Units', suppliers: 'Suppliers', customers: 'Customers', team: 'Staff & Access'
}

export default function App() {
  const [page, setPage] = useState<Page>('loading')
  const [accessPortal, setAccessPortal] = useState<AccessPortal | null>(selectedPortalFromUrl)
  const [user, setUser] = useState<User | null>(null)
  const [businesses, setBusinesses] = useState<Business[]>([])
  const [canViewPlatform, setCanViewPlatform] = useState(false)
  const [platformSection, setPlatformSection] = useState<PlatformSection>('overview')
  const [platformBusinessId, setPlatformBusinessId] = useState<number | null>(null)
  const [platformStartCreate, setPlatformStartCreate] = useState(false)
  const [platformSearchDraft, setPlatformSearchDraft] = useState('')
  const [platformBusinessSearch, setPlatformBusinessSearch] = useState('')
  const [dashboard, setDashboard] = useState<RetailDashboard | null>(null)
  const [approvalList, setApprovalList] = useState(false)
  const [billingBusiness, setBillingBusiness] = useState<{ id: number; name: string } | null>(null)
  const [retailPage, setRetailPage] = useState<RetailPage>('overview')
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
      setPage('gateway')
    })

    api<User>('/auth/me')
      .then(async me => {
        setUser(me)
        if (me.isPlatformOwner) {
          setBusinesses([])
          setCanViewPlatform(me.mfaVerified)
          setPlatformSection('overview')
          setPage(me.mfaVerified ? 'platform' : 'mfa-setup')
          return
        }
        const list = visibleBusinessesForPortal(await api<Business[]>('/businesses'), selectedPortalFromUrl())
        setBusinesses(list)
        if (list.length === 1) await openBusiness(list[0].id)
        else setPage('businesses')
      })
      .catch(() => setPage(selectedPortalFromUrl() ? 'login' : 'gateway'))
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
      const list = visibleBusinessesForPortal(await api<Business[]>('/businesses'), accessPortal)
      setBusinesses(list)
      setDashboard(null)
      setPage('businesses')
    } catch (cause) {
      if (!(cause instanceof SessionExpiredError))
        setError(message(cause))
    }
  }

  async function openBusiness(id: number, showOverview = false) {
    setError('')
    try {
      const details = await api<RetailDashboard>(`/businesses/${id}/dashboard`)
      if (details.business.typeCode !== 'RETAIL') {
        setDashboard(null)
        setPage('businesses')
        setError(`${details.business.typeName} workspace is not available yet.`)
        return
      }
      setApprovalList(false)
      setDashboard(details)
      setRetailPage(!showOverview && details.cashSaleEnabled && details.roleCode === 'Cashier' ? 'cash' : 'overview')
      setPage('retail')
    } catch (cause) {
      if (!(cause instanceof SessionExpiredError)) {
        setError(message(cause))
        setPage('businesses')
      }
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
      setPage('gateway')
      setAccessPortal(null)
      const url = new URL(window.location.href)
      url.searchParams.delete('workspace')
      window.history.replaceState(null, '', url)
      setSessionExpired(false)
      setAuthNotice('')
      setError('')
      setNotice('')
    } catch (cause) {
      setError(message(cause))
    }
  }

  function chooseWorkspace(next: AccessPortal | null) {
    setAccessPortal(next)
    setAuthNotice('')
    setError('')
    const url = new URL(window.location.href)
    if (next) url.searchParams.set('workspace', next)
    else url.searchParams.delete('workspace')
    window.history.replaceState(null, '', url)
    setPage(next ? 'login' : 'gateway')
  }

  function onSignedIn(next: User, list: Business[]) {
    resetCsrf()
    setUser(next)
    setCanViewPlatform(next.isPlatformOwner && next.mfaVerified)
    const visible = visibleBusinessesForPortal(list, accessPortal)
    setBusinesses(next.isPlatformOwner ? [] : visible)
    setSessionExpired(false)
    setAuthNotice('')
    setPlatformSection('overview')
    setPage(next.isPlatformOwner ? (next.mfaVerified ? 'platform' : 'mfa-setup') : 'businesses')
    setError('')
    if (!next.isPlatformOwner && visible.length === 1) void openBusiness(visible[0].id)
  }

  if (page === 'loading')
    return <main className="center-screen" aria-live="polite">Loading your workspace…</main>

  if (page === 'gateway' || ((page === 'login' || page === 'register') && !accessPortal))
    return <WorkspaceEntry onChoose={chooseWorkspace} />

  if ((page === 'login' || page === 'register') && accessPortal)
    return <AuthPage key={page + '-' + accessPortal} portal={accessPortal} mode={page}
      onBack={() => chooseWorkspace(null)} onChange={setPage} onSignedIn={onSignedIn}
      sessionExpired={sessionExpired} authNotice={authNotice} onRegistered={setAuthNotice} />

  if (page === 'mfa-setup')
    return <PlatformMfaSetup onFinished={() => {
      resetCsrf()
      setUser(null)
      setCanViewPlatform(false)
      setPage('login')
    }} />

  return (
    <div className={`app-shell${page === 'platform' ? ' platform-shell' : ''}${page === 'retail' && dashboard?.roleCode === 'Cashier' ? ' pos-shell' : ''}${page === 'retail' && dashboard?.roleCode !== 'Cashier' ? ' business-shell' : ''}`}>
      {menuOpen && <button type="button" className="sidebar-scrim" aria-label="Close navigation"
        onClick={() => { setMenuOpen(false); menuToggle.current?.focus() }} />}
      <aside id="main-navigation" className={`app-sidebar${menuOpen ? ' is-open' : ''}`}>
        <div className="brand"><span className="brand-mark">B</span>
          <div>Business SaaS<small>{page === 'platform' ? 'Platform Super Admin' :
            page === 'retail' && dashboard?.roleCode === 'Cashier' ? 'Point of Sale' : 'Business Management'}</small></div>
        </div>
        {page === 'retail' && dashboard && <div className="workspace-identity">
          <span>{dashboard.roleCode === 'Cashier' ? 'POINT OF SALE' :
            dashboard.roleCode === 'Owner' ? 'BUSINESS ADMIN' : 'STORE MANAGER'}</span>
          <strong>{dashboard.business.name}</strong>
          <small>{dashboard.roleCode} · {dashboard.locations[0]?.name ?? 'No location assigned'}</small>
        </div>}
        {!user?.isPlatformOwner && page !== 'platform' && <div className="sidebar-heading">ACCOUNT</div>}
        {!user?.isPlatformOwner && page !== 'platform' && <button type="button" ref={businessNav}
          className={`nav-entry${page === 'businesses' ? ' active' : ''}`}
          aria-current={page === 'businesses' ? 'page' : undefined}
          onClick={() => {
            if (menuOpen) menuToggle.current?.focus()
            setMenuOpen(false)
            setError('')
            void loadBusinesses()
          }}>
          <Building2 size={18} /> {page === 'retail' ? 'Switch Business' : 'My Businesses'}
        </button>}
        {page === 'retail' && dashboard && <>
          <div className="sidebar-heading">{dashboard.roleCode === 'Cashier' ? 'CHECKOUT' : 'BUSINESS OPERATIONS'}</div>
          {([
            ['overview', 'Dashboard', LayoutDashboard],
            ['products', 'Products', Package],
            ['purchases', 'Purchase Orders', ShoppingCart],
            ['stock', 'Inventory', Boxes],
            ['cash', 'Open POS Checkout', ShoppingCart]
          ] as const).filter(([section]) => section === 'cash' ? dashboard.cashSaleEnabled : dashboard.roleCode !== 'Cashier' || section === 'overview')
            .map(([section, label, Icon]) => <button key={section} type="button"
              className={`nav-entry${section === 'cash' ? ' pos-nav-entry' : ''}${retailPage === section ? ' active' : ''}`}
              aria-current={retailPage === section ? 'page' : undefined}
              onClick={() => {
                if (menuOpen) menuToggle.current?.focus()
                setMenuOpen(false)
                setApprovalList(false)
                if (section === 'overview') void openBusiness(dashboard.business.id, true)
                else setRetailPage(section)
              }}><Icon size={18} /> {label}</button>)}
          {dashboard.roleCode !== 'Cashier' && <>
            <div className="sidebar-heading">SHOP SETUP</div>
            {([
              ['masters', 'Categories & Units', Tags],
              ['suppliers', 'Suppliers', Truck],
              ['customers', 'Customers', Users]
            ] as const).map(([section, label, Icon]) => <button key={section} type="button"
              className={`nav-entry${retailPage === section ? ' active' : ''}`}
              aria-current={retailPage === section ? 'page' : undefined}
              onClick={() => {
                if (menuOpen) menuToggle.current?.focus()
                setMenuOpen(false)
                setRetailPage(section)
              }}><Icon size={18} /> {label}</button>)}
          </>}
          {dashboard.roleCode === 'Owner' && <button type="button"
            className={`nav-entry${retailPage === 'team' ? ' active' : ''}`}
            aria-current={retailPage === 'team' ? 'page' : undefined}
            onClick={() => {
              if (menuOpen) menuToggle.current?.focus()
              setMenuOpen(false)
              setRetailPage('team')
            }}><UserRound size={18} /> Staff & Access</button>}
        </>}
        {canViewPlatform && page === 'platform' && <>
          <div className="sidebar-heading">SUPER ADMIN MODULES</div>
          {([
            ['overview', 'Dashboard', LayoutDashboard],
            ['customers', 'Customers', Users],
            ['businesses', 'Businesses', Building2],
            ['plans', 'Plans & Billing', CreditCard],
            ['licenses', 'Licenses', KeyRound],
            ['settings', 'Platform Settings', Settings2],
            ['audit', 'Security & Audit', ShieldCheck],
            ['operations', 'Operations', Activity]
          ] as const).map(([section, label, Icon]) => <button key={section}
                ref={section === 'overview' && user?.isPlatformOwner ? businessNav : undefined}
                type="button"
                className={`nav-entry${page === 'platform' && platformSection === section ? ' active' : ''}`}
                aria-current={page === 'platform' && platformSection === section ? 'page' : undefined}
                onClick={() => {
                  if (menuOpen) menuToggle.current?.focus()
                  setMenuOpen(false)
                  setDashboard(null)
                  setError('')
                  setNotice('')
                  setBillingBusiness(null)
                  setPlatformBusinessId(null)
                  setPlatformStartCreate(false)
                  setPlatformSection(section)
                  setPage('platform')
                }}><Icon size={18} /> {label}</button>)}
        </>}
        <div className="sidebar-footer">{page === 'platform'
          ? <>Platform Administration <small className="d-block mt-1">Manage SaaS accounts, not shop transactions</small></>
          : page === 'retail' && dashboard
            ? <>{dashboard.roleCode === 'Cashier' ? 'Cashier checkout' : 'Shop operations'}<small className="d-block mt-1">Signed in as {dashboard.roleCode}</small></>
            : 'Select a business to manage.'}</div>
      </aside>

      <div className="app-main">
        <header className={`app-topbar${page === 'platform' ? ' platform-topbar' : ''}`}>
          {page === 'platform' && <div className="platform-topbar-brand">
            <span className="platform-topbar-logo" aria-hidden="true">B</span>
            <strong>Business SaaS</strong>
            <span className="platform-topbar-badge">Super Admin</span>
            <span className="platform-topbar-divider" aria-hidden="true" />
            <span className="platform-topbar-caption">Platform Administration</span>
          </div>}
          <div className="topbar-title">
            <button type="button" ref={menuToggle} className="mobile-menu-toggle" aria-label={menuOpen ? 'Close navigation' : 'Open navigation'}
              aria-controls="main-navigation" aria-expanded={menuOpen} onClick={() => setMenuOpen(open => !open)}>
              <Menu size={20} />
            </button>
            <span className="topbar-workspace">
              <small>{page === 'platform' ? 'PLATFORM SUPER ADMIN' :
                page === 'retail' && dashboard?.roleCode === 'Cashier' ? 'POINT OF SALE' :
                page === 'retail' ? dashboard?.roleCode === 'Owner' ? 'BUSINESS ADMIN' : 'STORE MANAGER' :
                'BUSINESS ACCOUNT'}</small>
              <strong>{page === 'retail' ? `${dashboard?.business.name} / ${retailTitles[retailPage]}` :
                page === 'platform' ? 'Platform Management' : 'My Businesses'}</strong>
            </span>
          </div>
          {page === 'platform' && <form className="platform-global-search" role="search" onSubmit={event => {
            event.preventDefault()
            setPlatformStartCreate(false)
            setPlatformBusinessId(null)
            setPlatformBusinessSearch(platformSearchDraft.trim())
            setPlatformSection('businesses')
          }}>
            <Search size={17} aria-hidden="true" />
            <input type="search" aria-label="Search businesses" value={platformSearchDraft}
              maxLength={100} placeholder="Search businesses…" onChange={event => setPlatformSearchDraft(event.target.value)} />
            <button type="submit" aria-label="Run business search">Search</button>
          </form>}
          <div className="topbar-account">
            {canViewPlatform && !user?.isPlatformOwner && page !== 'platform' && <button
              type="button" className="btn btn-outline-primary btn-sm workspace-switch" onClick={() => {
                setDashboard(null)
                setPlatformSection('overview')
                setPlatformBusinessId(null)
                setBillingBusiness(null)
                setPage('platform')
              }}><ShieldCheck size={16} /> Super Admin</button>}
            {page === 'platform' && !user?.isPlatformOwner && <button
              type="button" className="btn btn-outline-secondary btn-sm workspace-switch"
              onClick={() => { void loadBusinesses() }}><Building2 size={16} /> Business Workspace</button>}<UserRound size={17} />
            <span className="account-email">{user?.isPlatformOwner ? 'Platform Owner · ' : ''}{user?.email}</span>
            <button className="btn btn-outline-secondary btn-sm" onClick={() => void logout()}><LogOut size={15} /> Sign out</button>
          </div>
        </header>

        <main className="page-content">
          {error && <div className="alert alert-danger" role="alert">{error}</div>}
          {notice && <div className="alert alert-success" role="status">{notice}</div>}

          {page === 'platform' && canViewPlatform && <PlatformAdmin section={platformSection}
            selectedBusinessId={platformBusinessId} billingBusiness={billingBusiness}
            startCreate={platformStartCreate} businessSearch={platformBusinessSearch}
            onViewAudit={() => { setPlatformBusinessId(null); setPlatformSection('audit') }}
            onCreateHandled={() => setPlatformStartCreate(false)}
            onSeeBusinesses={() => { setPlatformStartCreate(false); setPlatformBusinessId(null); setPlatformSection('businesses') }}
            onStartCreate={() => { setPlatformBusinessId(null); setPlatformStartCreate(true); setPlatformSection('businesses') }}
            onBilling={business => { setBillingBusiness(business); setPlatformSection('plans') }}
            onChooseBusiness={() => { setPlatformBusinessId(null); setPlatformStartCreate(false); setPlatformSection('businesses') }}
            onViewBusiness={id => {
              setPlatformStartCreate(false)
              setPlatformBusinessId(id)
              setPlatformSection('businesses')
            }} />}

          {page === 'businesses' && (
            <>
              <div className="page-header">
                <div><h1>My Assigned Businesses</h1><p>Select a business assigned to your account. New businesses are provisioned by the Platform Super Admin.</p></div>

              </div>
              {businesses.length === 0 ? (
                <section className="empty-state">
                  <Store size={32} />
                  <h2>{accessPortal === 'pos' ? 'No cashier assignment found' : 'No business access yet'}</h2>
                  <p>{accessPortal === 'pos'
                    ? 'Ask your Business Owner to assign your Cashier account to an active Retail location.'
                    : 'A Platform Super Admin must provision your business and assign you as Owner or Manager.'}</p>
                  <p className="small">Signed in as {user?.email}. Refresh after your administrator grants access.</p>
                  <button className="btn btn-outline-secondary me-2" onClick={() => void loadBusinesses()}>Refresh access</button>

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

          {page === 'retail' && dashboard && (
            <>
              <div className="page-header">
                <div><button className="back-link" onClick={() => { setNotice(''); void loadBusinesses() }}><ArrowLeft size={16} /> All businesses</button>
                  <h1>{retailTitles[retailPage]}</h1><p>{dashboard.business.name} · {dashboard.roleCode === 'Cashier' ? 'POS / Cashier' : dashboard.roleCode === 'Owner' ? 'Business Admin' : 'Store Manager'}</p>
                </div>
                {dashboard.canOperate
                  ? <span className="workspace-state"><CheckCircle2 size={16} /> {dashboard.accessStatus === 'Trial' ? 'Trial active' : 'Plan active'}</span>
                  : <span className="workspace-state inactive">
                    {dashboard.accessStatus === 'TrialExpired' ? 'Trial expired' : 'Subscription inactive'}
                  </span>}
              </div>
              {!dashboard.canOperate && <div className="alert alert-warning" role="status">
                {dashboard.accessStatus === 'TrialExpired'
                  ? 'Your free trial has ended. Ask the platform administrator to prepare a subscription payment request.'
                  : 'This business has no active subscription. Contact the platform administrator for help.'}
              </div>}
              {dashboard.subscription && <p className="small text-secondary">
                {dashboard.subscription.planCode} · {dashboard.accessStatus === 'TrialExpired'
                  ? 'Trial expired' : dashboard.subscription.status}
                {dashboard.subscription.status === 'Trial' &&
                  ` · ${dashboard.accessStatus === 'TrialExpired' ? 'Ended' : 'Ends'} ${new Date(dashboard.subscription.trialEndsAtUtc).toLocaleString(undefined, {
                    dateStyle: 'medium', timeStyle: 'short', timeZone: dashboard.business.timeZoneId
                  })}`}
                {dashboard.subscription.paidEndsAtUtc &&
                  ` · Paid through ${new Date(dashboard.subscription.paidEndsAtUtc).toLocaleString(undefined, {
                    dateStyle: 'medium', timeStyle: 'short', timeZone: dashboard.business.timeZoneId
                  })}`}
              </p>}
              {retailPage === 'overview' && dashboard.roleCode !== 'Cashier' && <>
                <BusinessOverview dashboard={dashboard}
                  onNavigate={section => { setApprovalList(false); setRetailPage(section) }}
                  onReviewApprovals={() => { setApprovalList(true); setRetailPage('purchases') }} />
                {dashboard.roleCode === 'Owner' && <details className="content-section business-subscription-panel">
                  <summary><CreditCard size={17} /> Subscription payment and billing status</summary>
                  <SubscriptionCheckout key={dashboard.business.id} businessId={dashboard.business.id} />
                </details>}
              </>}
              {retailPage === 'overview' && dashboard.roleCode === 'Cashier' && !dashboard.cashSaleEnabled &&
                <section className="content-section">
                  <h2>POS checkout is not available</h2>
                  <p>This environment has not enabled POS billing. Ask your shop owner or administrator about checkout availability.</p>
                </section>}
              {retailPage === 'cash' && dashboard.cashSaleEnabled && user && <CashCheckout
                key={user.id + '-' + dashboard.business.id} userId={user.id} dashboard={dashboard} />}
              {retailPage !== 'cash' && retailPage !== 'overview' && retailPage !== 'team' && dashboard.roleCode !== 'Cashier' &&
                <RetailCatalog key={dashboard.business.id + '-' + retailPage} section={retailPage} approvalsOnly={approvalList}
                  businessId={dashboard.business.id} canOperate={dashboard.canOperate}
                  currencyCode={dashboard.business.currencyCode} locations={dashboard.locations}
                  roleCode={dashboard.roleCode} />}
              {retailPage === 'team' && dashboard.roleCode === 'Owner' &&
                <TeamMembers businessId={dashboard.business.id} canOperate={dashboard.canOperate} cashSaleEnabled={!!dashboard.cashSaleEnabled} />}
            </>
          )}
        </main>
      </div>
    </div>
  )
}

function AuthPage({ mode, portal, onBack, onChange, onSignedIn, sessionExpired, authNotice, onRegistered }: {
  portal: AccessPortal
  onBack: () => void
  mode: 'login' | 'register'
  onChange: (page: Page) => void
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
      setError(message(cause))
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

function message(error: unknown): string {
  return error instanceof Error ? error.message : 'Something went wrong. Please try again.'
}
