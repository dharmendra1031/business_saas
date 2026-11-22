import { useEffect, useRef, useState } from 'react'
import { Activity, ArrowRight, BarChart3, Boxes, Building2, CreditCard, KeyRound, LayoutDashboard, LogOut, Menu, Package, ReceiptText, Search, Settings2, ShieldCheck, ShoppingCart, Store, Tags, Truck, UserRound, Users, Wallet } from 'lucide-react'
import { api, ApiResponseError, resetCsrf, SessionExpiredError, setSessionExpiredHandler, type Business, type RetailDashboard, type User } from './api'
import CashCheckout from './CashCheckout'
import TeamMembers from './TeamMembers'
import SubscriptionCheckout from './SubscriptionCheckout'
import RetailCatalog, { type RetailSection } from './RetailCatalog'
import BusinessOverview from './BusinessOverview'
import BusinessPreview, { type BusinessPreviewModule } from './BusinessPreview'
import PlatformAdmin, { type PlatformSection } from './PlatformAdmin'
import PlatformMfaSetup from './PlatformMfaSetup'
import AuthPage from './AuthPage'

type Page = 'loading' | 'unavailable' | 'login' | 'register' | 'mfa-setup' | 'businesses' | 'retail' | 'platform'

type RetailPage = RetailSection | 'cash' | 'overview' | 'team' | BusinessPreviewModule
function isRetailSection(value: RetailPage): value is RetailSection {
  return value === 'products' || value === 'masters' || value === 'stock' ||
    value === 'purchases' || value === 'suppliers' || value === 'customers'
}

export default function App() {
  const [page, setPage] = useState<Page>('loading')
  const [user, setUser] = useState<User | null>(null)
  const [businesses, setBusinesses] = useState<Business[]>([])
  const canViewPlatform = !!user?.isPlatformOwner && user.mfaVerified
  const [sessionError, setSessionError] = useState('')
  const [businessesLoading, setBusinessesLoading] = useState(false)
  const [businessesError, setBusinessesError] = useState('')
  const [openingBusinessId, setOpeningBusinessId] = useState<number | null>(null)
  const [platformSection, setPlatformSection] = useState<PlatformSection>('overview')
  const [platformBusinessId, setPlatformBusinessId] = useState<number | null>(null)
  const [platformStartCreate, setPlatformStartCreate] = useState(false)
  const [platformSearchDraft, setPlatformSearchDraft] = useState('')
  const [platformBusinessSearch, setPlatformBusinessSearch] = useState('')
  const [dashboard, setDashboard] = useState<RetailDashboard | null>(null)
  const [approvalList, setApprovalList] = useState(false)
  const [billingOpen, setBillingOpen] = useState(false)
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
      setBusinesses([])
      setDashboard(null)
      setMenuOpen(false)
      setError('')
      setBusinessesError('')
      setSessionExpired(true)
      setAuthNotice('')
      setPage('login')
    })
    void initializeSession()
    return () => setSessionExpiredHandler(null)
  }, [])

  async function initializeSession() {
    setPage('loading')
    setSessionError('')
    try {
      const me = await api<User>('/auth/me')
      setUser(me)
      setSessionExpired(false)
      if (me.isPlatformOwner) {
        setBusinesses([])
        setPlatformSection('overview')
        setPage(me.mfaVerified ? 'platform' : 'mfa-setup')
      } else {
        await loadBusinesses(true)
      }
    } catch (cause) {
      setUser(null)
      setBusinesses([])
      if (cause instanceof ApiResponseError && cause.status === 401) {
        setPage('login')
      } else {
        setSessionError(message(cause))
        setPage('unavailable')
      }
    }
  }

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

  async function loadBusinesses(openSingle = false) {
    setError('')
    setBusinessesError('')
    setBusinessesLoading(true)
    setOpeningBusinessId(null)
    setDashboard(null)
    setPage('businesses')
    try {
      const list = await api<Business[]>('/businesses')
      setBusinesses(list)
      if (openSingle && list.length === 1) await openBusiness(list[0].id)
    } catch (cause) {
      if (!(cause instanceof SessionExpiredError))
        setBusinessesError(message(cause))
    } finally {
      setBusinessesLoading(false)
    }
  }

  async function openBusiness(id: number, showOverview = false) {
    if (openingBusinessId !== null) return
    setError('')
    setOpeningBusinessId(id)
    try {
      const details = await api<RetailDashboard>(`/businesses/${id}/dashboard`)
      if (details.business.typeCode !== 'RETAIL') {
        setDashboard(null)
        setPage('businesses')
        setError(`${details.business.typeName} workspace is not available yet.`)
        return
      }
      setApprovalList(false)
      setBillingOpen(false)
      setDashboard(details)
      setRetailPage(!showOverview && details.cashSaleEnabled && details.roleCode === 'Cashier' ? 'cash' : 'overview')
      setPage('retail')
    } catch (cause) {
      if (!(cause instanceof SessionExpiredError)) {
        setError(message(cause))
        setPage('businesses')
      }
    } finally {
      setOpeningBusinessId(null)
    }
  }

  async function logout() {
    try {
      await api<void>('/auth/logout', { method: 'POST' })
      resetCsrf()
      setUser(null)
      setDashboard(null)
      setBillingOpen(false)
      setBusinesses([])
      setPage('login')
      setSessionExpired(false)
      setBusinessesError('')
      setAuthNotice('')
      setError('')
      setNotice('')
    } catch (cause) {
      setError(message(cause))
    }
  }

  if (page === 'loading')
    return <main className="center-screen" aria-live="polite">Loading your workspace…</main>

  if (page === 'unavailable')
    return <main className="auth-layout">
      <section className="auth-form-container">
        <div className="auth-card" role="alert">
          <h1>Unable to load your session</h1>
          <p className="form-hint">{sessionError || 'Please check your connection and try again.'}</p>
          <button type="button" className="btn btn-primary w-100" onClick={() => void initializeSession()}>Retry connection</button>
        </div>
      </section>
    </main>

  if (page === 'login' || page === 'register')
    return <AuthPage key={page} mode={page} onChange={setPage} onAuthenticated={() => void initializeSession()}
      sessionExpired={sessionExpired} authNotice={authNotice} onRegistered={setAuthNotice} />

  if (page === 'mfa-setup')
    return <PlatformMfaSetup onFinished={() => {
      resetCsrf()
      setUser(null)
      setPage('login')
    }} />

  const isPosView = page === 'retail' && retailPage === 'cash' && !!dashboard?.cashSaleEnabled
  const isBusinessOverview = page === 'retail' && retailPage === 'overview' && !!dashboard && dashboard.roleCode !== 'Cashier'

  return (
    <div className={`app-shell${page === 'platform' ? ' platform-shell' : ''}${isPosView ? ' pos-shell' : ''}${page === 'retail' && dashboard?.roleCode !== 'Cashier' ? ' business-shell' : ''}`}>
      {!isPosView && menuOpen && <button type="button" className="sidebar-scrim" aria-label="Close navigation"
        onClick={() => { setMenuOpen(false); menuToggle.current?.focus() }} />}
      {!isPosView && <aside id="main-navigation" className={`app-sidebar${menuOpen ? ' is-open' : ''}`}>
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
        {!user?.isPlatformOwner && page !== 'platform' && (page === 'businesses' || businesses.length > 1) && <div className="sidebar-heading">ACCOUNT</div>}
        {!user?.isPlatformOwner && page !== 'platform' && (page === 'businesses' || businesses.length > 1) && <button type="button" ref={businessNav}
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
          ] as const).filter(([section]) => dashboard.roleCode === 'Cashier'
            ? (section === 'cash' ? dashboard.cashSaleEnabled : section === 'overview' && !dashboard.cashSaleEnabled)
            : (section !== 'cash' || dashboard.cashSaleEnabled))
            .map(([section, label, Icon]) => <button key={section} type="button"
              ref={section === 'overview' && businesses.length <= 1 ? businessNav : undefined}
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
          {dashboard.roleCode !== 'Cashier' && <>
            <div className="sidebar-heading">SALES & REPORTING</div>
            {([
              ['sales', 'Sales & Returns', ReceiptText],
              ['register', 'Cash Register', Wallet],
              ['reports', 'Reports', BarChart3]
            ] as const).map(([section, label, Icon]) => <button key={section} type="button"
              className={`nav-entry business-preview-nav${retailPage === section ? ' active' : ''}`}
              aria-current={retailPage === section ? 'page' : undefined}
              onClick={() => {
                if (menuOpen) menuToggle.current?.focus()
                setMenuOpen(false)
                setRetailPage(section)
              }}><Icon size={18} /> <span>{label}</span><small>Preview</small></button>)}
          </>}
          {dashboard.roleCode === 'Owner' && <button type="button"
            className={`nav-entry${retailPage === 'team' ? ' active' : ''}`}
            aria-current={retailPage === 'team' ? 'page' : undefined}
            onClick={() => {
              if (menuOpen) menuToggle.current?.focus()
              setMenuOpen(false)
              setRetailPage('team')
            }}><UserRound size={18} /> Staff & Access</button>}
          {dashboard.roleCode === 'Owner' && <button type="button"
            className={`nav-entry business-preview-nav${retailPage === 'settings' ? ' active' : ''}`}
            aria-current={retailPage === 'settings' ? 'page' : undefined}
            onClick={() => {
              if (menuOpen) menuToggle.current?.focus()
              setMenuOpen(false)
              setRetailPage('settings')
            }}><Settings2 size={18} /> <span>Business Settings</span><small>Preview</small></button>}
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
        {page === 'platform' && <div className="sidebar-footer">
          Platform Administration
          <small className="d-block mt-1">Manage SaaS accounts, not shop transactions</small>
        </div>}
      </aside>}

      <div className="app-main">
        <header className={`app-topbar${page === 'platform' ? ' platform-topbar' : ''}`}>
          {isPosView && dashboard ? <>
            <div className="pos-topbar-brand"><span className="pos-brand-mark">B</span>
              <span><strong>Business SaaS</strong><small>POINT OF SALE</small></span>
            </div>
            <div className="pos-topbar-store"><Store size={17} />
              <span><strong>{dashboard.business.name}</strong>
                <small>{dashboard.locations.length === 1 ? dashboard.locations[0].name : 'Location selected below'} · {dashboard.roleCode}</small></span>
            </div>
            <div className="pos-topbar-account">
              <span className="pos-topbar-email">{user?.email}</span>
              <button type="button" className="btn btn-outline-secondary btn-sm"
                onClick={() => void logout()}><LogOut size={15} /> Sign out</button>
            </div>
          </> : <>
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
              {page !== 'retail' && <small>{page === 'platform' ? 'PLATFORM SUPER ADMIN' : 'BUSINESS ACCOUNT'}</small>}
              <strong>{page === 'retail' ? (isBusinessOverview ? 'Dashboard' : dashboard?.business.name) :
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
            <span className="account-email">{user?.isPlatformOwner ? 'Platform Owner · ' : ''}{user?.email}</span>
            <button className="btn btn-outline-secondary btn-sm" onClick={() => void logout()}><LogOut size={15} /> Sign out</button>
          </div>
          </>}
        </header>

        <main className={`page-content${isBusinessOverview ? ' business-dashboard-page' : ''}`}>
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
              <div className="page-header business-picker-header">
                <div><h1>Choose a business</h1>{!businessesLoading && !businessesError && businesses.length > 1 && <p>Select an assigned business to continue.</p>}</div>

              </div>
              {businessesLoading ? (
                <section className="empty-state" role="status">
                  <p>Loading assigned businesses…</p>
                </section>
              ) : businessesError ? (
                <section className="empty-state" role="alert">
                  <h2>Unable to load businesses</h2>
                  <p>{businessesError}</p>
                  <button className="btn btn-primary" onClick={() => void loadBusinesses(true)}>Retry loading businesses</button>
                </section>
              ) : businesses.length === 0 ? (
                <section className="empty-state">
                  <Store size={32} />
                  <h2>No business access yet</h2>
                  <p>Ask your administrator to assign your account to a business and role.</p>
                  <button className="btn btn-outline-secondary me-2" onClick={() => void loadBusinesses()}>Refresh access</button>

                </section>
              ) : (
                <div className="business-list business-assigned-list" aria-label="Assigned business workspaces">
                  {businesses.map(business => (
                    <button className="business-row" type="button" key={business.id}
                      disabled={openingBusinessId !== null} aria-busy={openingBusinessId === business.id}
                      onClick={() => void openBusiness(business.id)}>
                      <span className="shop-symbol"><Store size={22} /></span>
                      <span className="business-summary"><strong>{business.name}</strong><small>{business.typeName} · {business.countryCode}</small></span>
                      <span className="business-assigned-role">{business.roleCode}</span>
                      {openingBusinessId === business.id
                        ? <span className="business-opening-label">Opening…</span>
                        : <ArrowRight size={18} aria-hidden="true" />}
                    </button>
                  ))}
                </div>
              )}
            </>
          )}

          {page === 'retail' && dashboard && (
            <>
              {!dashboard.canOperate && <div className="alert alert-warning" role="status">
                {dashboard.accessStatus === 'TrialExpired'
                  ? 'Your free trial has ended. Ask the platform administrator to prepare a subscription payment request.'
                  : dashboard.accessStatus === 'Suspended'
                    ? 'This business is suspended. Contact the platform administrator for assistance.'
                    : 'This business has no active subscription. Contact the platform administrator for help.'}
              </div>}
              {retailPage === 'overview' && dashboard.roleCode !== 'Cashier' && <>
                <BusinessOverview dashboard={dashboard}
                  onNavigate={section => { setApprovalList(false); setRetailPage(section) }}
                  onReviewApprovals={() => { setApprovalList(true); setRetailPage('purchases') }} />
                {dashboard.roleCode === 'Owner' && <details className="content-section business-subscription-panel" onToggle={event => setBillingOpen(event.currentTarget.open)}>
                  <summary><CreditCard size={17} /> Subscription & billing</summary>
                  {billingOpen && <SubscriptionCheckout key={dashboard.business.id} businessId={dashboard.business.id} />}
                </details>}
              </>}
              {retailPage === 'overview' && dashboard.roleCode === 'Cashier' && !dashboard.cashSaleEnabled &&
                <section className="content-section">
                  <h2>POS checkout is not available</h2>
                  <p>This environment has not enabled POS billing. Ask your shop owner or administrator about checkout availability.</p>
                </section>}
              {retailPage === 'cash' && dashboard.cashSaleEnabled && user && <CashCheckout
                key={user.id + '-' + dashboard.business.id} userId={user.id} dashboard={dashboard}
                onExit={() => dashboard.roleCode === 'Cashier' ? void loadBusinesses() : setRetailPage('overview')} />}
              {isRetailSection(retailPage) && dashboard.roleCode !== 'Cashier' &&
                <RetailCatalog key={dashboard.business.id + '-' + retailPage} section={retailPage} approvalsOnly={approvalList}
                  businessId={dashboard.business.id} canOperate={dashboard.canOperate}
                  currencyCode={dashboard.business.currencyCode} locations={dashboard.locations}
                  roleCode={dashboard.roleCode} />}
              {retailPage === 'team' && dashboard.roleCode === 'Owner' &&
                <TeamMembers businessId={dashboard.business.id} canOperate={dashboard.canOperate} cashSaleEnabled={!!dashboard.cashSaleEnabled} />}
              {(retailPage === 'sales' || retailPage === 'register' || retailPage === 'reports' ||
                (retailPage === 'settings' && dashboard.roleCode === 'Owner')) &&
                dashboard.roleCode !== 'Cashier' && <BusinessPreview key={retailPage}
                  module={retailPage} dashboard={dashboard} />}
            </>
          )}
        </main>
      </div>
    </div>
  )
}

function message(error: unknown): string {
  return error instanceof Error ? error.message : 'Something went wrong. Please try again.'
}
