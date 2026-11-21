import { ArrowRight, Building2, CheckCircle2, LockKeyhole, ScanLine, ShieldCheck } from 'lucide-react'

export type AccessPortal = 'platform' | 'business' | 'pos'

export const accessContent = {
  platform: {
    label: 'Super Admin',
    eyebrow: 'PLATFORM CONTROL',
    title: 'Lead your entire platform.',
    description: 'Manage tenant businesses, plans, security and platform operations from one protected workspace.',
    button: 'Super Admin sign in',
    Icon: ShieldCheck
  },
  business: {
    label: 'Business Owner / Admin',
    eyebrow: 'BUSINESS MANAGEMENT',
    title: 'Your business, in focus.',
    description: 'Run your assigned shop, manage inventory, purchases, staff and the day-to-day work.',
    button: 'Business Admin sign in',
    Icon: Building2
  },
  pos: {
    label: 'POS / Cashier',
    eyebrow: 'POINT OF SALE',
    title: 'A faster way to serve.',
    description: 'Enter your assigned checkout workspace to find products, build a basket and complete sales.',
    button: 'Cashier sign in',
    Icon: ScanLine
  }
} as const

export function WorkspaceEntry({ onChoose }: { onChoose: (portal: AccessPortal) => void }) {
  return <main className="entry-page">
    <header className="entry-header">
      <div className="entry-brand" aria-label="Business SaaS">
        <span className="entry-brand-mark">B</span>
        <span>Business <strong>SaaS</strong><small>ENTERPRISE WORKSPACE</small></span>
      </div>
      <span className="entry-security"><LockKeyhole size={15} /> Secure access</span>
    </header>

    <section className="entry-container" aria-labelledby="entry-heading">
      <div className="entry-heading">
        <span className="entry-eyebrow">ONE PLATFORM · THREE DISTINCT WORKSPACES</span>
        <h1 id="entry-heading">Where would you like to work?</h1>
        <p>Choose the area assigned to your account. Each workspace has its own tools and navigation, with permissions verified by the server.</p>
      </div>

      <div className="entry-grid">
        {(['platform', 'business', 'pos'] as const).map(portal => {
          const item = accessContent[portal]
          const Icon = item.Icon
          return <button key={portal} className={`entry-option entry-option-${portal}`} type="button"
            onClick={() => onChoose(portal)} aria-label={item.button}>
            <span className="entry-card-head">
              <span className="entry-card-icon"><Icon size={26} strokeWidth={1.8} /></span>
              <span className="entry-card-index">{portal === 'platform' ? '01' : portal === 'business' ? '02' : '03'}</span>
            </span>
            <span className="entry-card-tag">{item.eyebrow}</span>
            <span className="entry-card-title">{item.label}</span>
            <span className="entry-card-description">{item.description}</span>
            <span className="entry-card-action">{item.button}<ArrowRight size={18} /></span>
          </button>
        })}
      </div>

      <div className="entry-footnote"><CheckCircle2 size={18} /> One secure identity system. Your selected workspace never grants additional access.</div>
    </section>
    <footer className="entry-footer"><span>Business SaaS · Enterprise platform</span><span>Authorized users only</span></footer>
  </main>
}
