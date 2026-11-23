import { useEffect, useState, type FormEvent } from 'react'
import { ArrowLeft, ArrowRight, Building2, CheckCircle2, ShieldCheck, Store, UserRound } from 'lucide-react'
import { api, ApiResponseError, operationKey, post, SessionExpiredError, type BusinessMarket, type BusinessType } from './api'

type VerifiedOwner = { id: number; email: string; verified: boolean }
type ProvisioningEligibility = {
  types: BusinessType[]
  block: { code: string; message: string } | null
  trialDays: number | null
}
type ProvisioningResult = { id: number; alreadyExists: boolean; trialEndsAtUtc: string }
const steps = ['Business Details', 'Owner & Access', 'Plan & Location', 'Review']

export default function PlatformCreateBusiness({ onCancel, onCreated }: {
  onCancel: () => void
  onCreated: (id: number) => void
}) {
  const [step, setStep] = useState(0)
  const [types, setTypes] = useState<BusinessType[]>([])
  const [markets, setMarkets] = useState<BusinessMarket[]>([])
  const [trialDays, setTrialDays] = useState<number | null>(null)
  const [loading, setLoading] = useState(true)
  const [loadError, setLoadError] = useState('')
  const [refresh, setRefresh] = useState(0)

  const [name, setName] = useState('')
  const [typeId, setTypeId] = useState(0)
  const [countryCode, setCountryCode] = useState('')
  const market = markets.find(x => x.code === countryCode)
  const [currency, setCurrency] = useState('')
  const [timeZone, setTimeZone] = useState('')
  const [ownerEmail, setOwnerEmail] = useState('')
  const [owner, setOwner] = useState<VerifiedOwner | null>(null)
  const [missingOwner, setMissingOwner] = useState(false)
  const [invitationReason, setInvitationReason] = useState('')
  const [invitedEmail, setInvitedEmail] = useState('')
  const [location, setLocation] = useState('Main Shop')
  const [key] = useState(operationKey)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  const [submissionUncertain, setSubmissionUncertain] = useState(false)
  const [confirmed, setConfirmed] = useState(false)
  const [result, setResult] = useState<ProvisioningResult | null>(null)

  useEffect(() => {
    let active = true
    setLoading(true)
    Promise.all([
      api<ProvisioningEligibility>('/platform/provisioning-eligibility'),
      api<BusinessMarket[]>('/business-markets')
    ]).then(([eligibility, availableMarkets]) => {
      if (!active) return
      setTypes(eligibility.types)
      setMarkets(availableMarkets)
      setTrialDays(eligibility.trialDays)
      setLoadError(eligibility.block?.message ?? '')
    }).catch(cause => {
      if (!active || cause instanceof SessionExpiredError) return
      setLoadError('Unable to load available business setup options.')
    }).finally(() => { if (active) setLoading(false) })
    return () => { active = false }
  }, [refresh])

  async function verifyOwner(event: FormEvent) {
    event.preventDefault()
    setError('')
    setBusy(true)
    setOwner(null)
    setMissingOwner(false)
    try {
      const result = await api<VerifiedOwner>('/platform/owner-lookup?email=' +
        encodeURIComponent(ownerEmail.trim()))
      if (!result.verified) throw new Error('The selected owner account is not email-verified.')
      setOwner(result)
      setInvitedEmail('')
      setStep(2)
    } catch (cause) {
      if (!(cause instanceof SessionExpiredError)) {
        setMissingOwner(cause instanceof ApiResponseError && cause.status === 404)
        setError(cause instanceof Error ? cause.message : 'Unable to verify owner account.')
      }
    } finally { setBusy(false) }
  }

  async function sendOwnerInvitation() {
    if (busy || !missingOwner || invitationReason.trim().length < 10) return
    setBusy(true)
    setError('')
    try {
      const issued = await post<{ email: string; status: string }>(
        '/platform/owner-invitations', {
          email: ownerEmail.trim(), reason: invitationReason.trim()
        })
      if (issued.status !== 'Sent') throw new Error('Email delivery was not confirmed.')
      setInvitedEmail(issued.email)
      setMissingOwner(false)
      setInvitationReason('')
    } catch (cause) {
      if (!(cause instanceof SessionExpiredError))
        setError(cause instanceof Error ? cause.message : 'Unable to deliver invitation.')
    } finally { setBusy(false) }
  }

  async function create(event: FormEvent) {
    event.preventDefault()
    if (busy || !owner || !market || !canSubmit) return
    setBusy(true)
    setError('')
    try {
      const created = await post<ProvisioningResult>('/platform/businesses', {
        name: name.trim(), businessTypeId: typeId, countryCode,
        currencyCode: currency, timeZoneId: timeZone,
        ownerEmail: owner.email, locationName: location.trim(),
        planCode: 'STARTER', requestKey: key, expectedTrialDays: trialDays
      })
      if (!created || !Number.isSafeInteger(created.id) || created.id <= 0 ||
          typeof created.alreadyExists !== 'boolean' || !Number.isFinite(Date.parse(created.trialEndsAtUtc)))
        throw new Error('The server returned an incomplete business creation result.')
      setResult(created)
    } catch (cause) {
      if (!(cause instanceof SessionExpiredError)) {
        if (cause instanceof ApiResponseError && cause.code === 'TRIAL_DURATION_CHANGED') {
          setConfirmed(false)
          setLoading(true)
          setRefresh(value => value + 1)
        }
        const uncertain = cause instanceof ApiResponseError
          ? cause.status >= 500 || cause.status === 408 || (submissionUncertain && cause.status === 429)
          : true
        setSubmissionUncertain(uncertain)
        setError(cause instanceof Error ? cause.message : 'Unable to create business. Check status before retry.')
      }
    } finally { setBusy(false) }
  }

  const currentType = types.find(x => x.id === typeId)
  const canProceedDetails = name.trim().length >= 2 && name.trim().length <= 150 &&
    types.some(x => x.id === typeId) && !!market && market.currencies.includes(currency) &&
    market.timeZones.includes(timeZone)
  const validLocation = location.trim().length >= 2 && location.trim().length <= 100
  const canSubmit = confirmed && canProceedDetails && validLocation &&
    trialDays !== null && !loading && !loadError

  if (result) return <section className="content-section provision-form" aria-labelledby="provision-result">
    <div role="status">
      <h1 id="provision-result">{result.alreadyExists ? 'Business creation confirmed' : 'Business created'}</h1>
      <p>{name.trim()} · Business ID {result.id}</p>
      <p>{owner?.email} was assigned as Owner.</p>
      <p>Trial ends <time dateTime={result.trialEndsAtUtc}>{new Date(result.trialEndsAtUtc).toLocaleString(undefined, {
        timeZone, dateStyle: 'medium', timeStyle: 'short'
      })}</time> ({timeZone}).</p>
    </div>
    <button type="button" className="btn btn-primary" autoFocus onClick={() => onCreated(result.id)}>
      View Business <ArrowRight size={16} />
    </button>
  </section>

  return <div className="platform-provision">
    <div className="page-header provision-heading">
      <div>
        <button type="button" className="back-link" onClick={onCancel} disabled={busy}>
          <ArrowLeft size={16} /> Businesses
        </button>
        <h1>Create Business</h1>
        <p>Provision one independent business and assign its verified Owner.</p>
      </div>
      <span className="provision-security"><ShieldCheck size={16} /> Platform Owner · MFA protected</span>
    </div>

    <nav className="provision-steps" aria-label="Business creation progress" aria-describedby="provision-progress-help">
      <span className="visually-hidden" id="provision-progress-help">Step {step + 1} of {steps.length}: {steps[step]}</span>
      {steps.map((label, index) => <div key={label}
        className={`provision-step${index === step ? ' is-current' : ''}${index < step ? ' is-complete' : ''}`}
        aria-current={index === step ? 'step' : undefined}>
        <span className="provision-step-number">{index < step ? <CheckCircle2 size={18} /> : index + 1}</span>
        <span>{label}</span>
      </div>)}
    </nav>

    <div className="provision-grid">
      <section className="content-section provision-form" aria-labelledby="provision-title">
        {error && <div role="alert" className="alert alert-danger">{error}</div>}
        {loadError && <div role="alert" className="alert alert-danger">{loadError}
          <button type="button" className="btn btn-sm btn-outline-danger ms-2"
            onClick={() => setRefresh(v => v + 1)}>Retry</button>
        </div>}
        {loading && <p role="status">Loading setup options…</p>}

        {step === 0 && !loading && !loadError && <>
          <div className="provision-form-intro">
            <span className="provision-intro-icon"><Building2 size={23} /></span>
            <div><h2 id="provision-title">Business Details</h2>
              <p>Use the legal/trading identity and correct operating market.</p></div>
          </div>
          <form onSubmit={event => { event.preventDefault(); if (canProceedDetails) { setError(''); setStep(1) } }}>
            <div className="form-grid">
              <div className="form-wide">
                <label className="form-label" htmlFor="provision-name">Business Name *</label>
                <input id="provision-name" className="form-control" value={name} minLength={2}
                  maxLength={150} required autoFocus placeholder="e.g. Al Noor Fashion"
                  onChange={event => setName(event.target.value)} />
              </div>
              <div>
                <label className="form-label" htmlFor="provision-type">Business Type *</label>
                <select id="provision-type" className="form-select" required value={typeId}
                  onChange={event => setTypeId(Number(event.target.value))}>
                  <option value={0}>Select a supported type</option>
                  {types.map(type => <option key={type.id} value={type.id}>{type.name}</option>)}
                </select>
              </div>
              <div>
                <label className="form-label" htmlFor="provision-country">Country *</label>
                <select id="provision-country" className="form-select" value={countryCode} required
                  onChange={event => {
                    const code = event.target.value
                    const selected = markets.find(m => m.code === code)
                    setCountryCode(code)
                    setCurrency(selected?.currencies[0] ?? '')
                    setTimeZone(selected?.timeZones[0] ?? '')
                  }}>
                  <option value="">Select country</option>
                  {markets.map(item => <option key={item.code} value={item.code}>{item.name}</option>)}
                </select>
              </div>
              <div>
                <label className="form-label" htmlFor="provision-currency">Currency *</label>
                <select id="provision-currency" className="form-select" value={currency} required disabled={!market}
                  onChange={event => setCurrency(event.target.value)}>
                  {(market?.currencies ?? []).map(code => <option key={code} value={code}>{code}</option>)}
                </select>
              </div>
              <div>
                <label className="form-label" htmlFor="provision-time-zone">Timezone *</label>
                <select id="provision-time-zone" className="form-select" value={timeZone} required disabled={!market}
                  onChange={event => setTimeZone(event.target.value)}>
                  {(market?.timeZones ?? []).map(zone => <option key={zone} value={zone}>{zone}</option>)}
                </select>
              </div>
            </div>
            <div className="form-footer"><button className="btn btn-outline-secondary" type="button" onClick={onCancel}>Cancel</button>
              <button className="btn btn-primary" disabled={!canProceedDetails} type="submit">Next: Owner <ArrowRight size={16} /></button>
            </div>
          </form>
        </>}

        {step === 1 && <>
          <div className="provision-form-intro"><span className="provision-intro-icon"><UserRound size={23} /></span>
            <div><h2 id="provision-title">Owner & Access</h2>
              <p>Verify an existing account or invite the Owner by email before provisioning.</p></div>
          </div>
          <form onSubmit={event => void verifyOwner(event)}>
            <label className="form-label" htmlFor="provision-owner">Owner Email Address *</label>
            <input id="provision-owner" type="email" className="form-control" required autoFocus
              maxLength={254} value={ownerEmail}
              placeholder="owner@company.com"
              onChange={event => {
                setOwnerEmail(event.target.value)
                setOwner(null)
                setMissingOwner(false)
                setInvitedEmail('')
                setError('')
              }} />
            <p className="field-help">The Owner must accept the email invitation and set a password before you can assign the business. Accounts are not automatically created.</p>
            {invitedEmail && <div className="alert alert-success" role="status">
              Invitation emailed to {invitedEmail}. Ask the Owner to complete setup, then select Verify Owner.
            </div>}
            {missingOwner && <div className="border rounded p-3 mt-3">
              <h3 className="h6">Invite new Owner</h3>
              <p className="field-help">An invitation email will be sent only if secure mail delivery is configured. The link expires after 48 hours.</p>
              <label className="form-label" htmlFor="owner-invite-reason">Reason (audit log)</label>
              <textarea id="owner-invite-reason" className="form-control" rows={2}
                minLength={10} maxLength={200} value={invitationReason}
                placeholder="Reason for inviting this Owner"
                onChange={event => setInvitationReason(event.target.value)} />
              <button type="button" className="btn btn-outline-primary mt-2"
                disabled={busy || invitationReason.trim().length < 10}
                onClick={() => void sendOwnerInvitation()}>
                {busy ? 'Sending…' : 'Send secure email invitation'}
              </button>
            </div>}
            <div className="form-footer"><button className="btn btn-outline-secondary" type="button" disabled={busy}
              onClick={() => { setError(''); setStep(0) }}>Back</button>
              <button type="submit" className="btn btn-primary" disabled={busy}>
                {busy ? 'Checking…' : 'Verify Owner'} <ArrowRight size={16} />
              </button></div>
          </form>
        </>}

        {step === 2 && <>
          <div className="provision-form-intro"><span className="provision-intro-icon"><Store size={23} /></span>
            <div><h2 id="provision-title">Plan & First Location</h2>
              <p>Start with the available Starter trial. Paid plan activation requires separate verified billing.</p></div>
          </div>
          <div className="provision-plan-card" aria-label="Selected plan">
            <div><strong>Starter · Trial</strong><small>{trialDays} days from business creation</small></div>
            <span className="provision-plan-selected"><CheckCircle2 size={16} /> Selected</span>
          </div>
          <form onSubmit={event => { event.preventDefault(); if (validLocation) { setError(''); setStep(3) } }}>
            <label className="form-label" htmlFor="provision-location">First Location Name *</label>
            <input id="provision-location" className="form-control" value={location}
              minLength={2} maxLength={100} required
              onChange={event => setLocation(event.target.value)} />
            <p className="field-help">Additional locations and usage limits remain subject to the published plan and permissions.</p>
            <div className="form-footer"><button type="button" className="btn btn-outline-secondary"
              onClick={() => { setError(''); setStep(1) }}>Back</button>
              <button type="submit" className="btn btn-primary" disabled={!validLocation}>
                Review Business <ArrowRight size={16} />
              </button>
            </div>
          </form>
        </>}

        {step === 3 && <>
          <div className="provision-form-intro"><span className="provision-intro-icon"><CheckCircle2 size={23} /></span>
            <div><h2 id="provision-title">Review & Create</h2>
              <p>Confirm details. Provisioning creates an owner membership, first location and a trial once.</p></div>
          </div>
          <dl className="provision-review">
            <div><dt>Business</dt><dd>{name.trim()}</dd></div>
            <div><dt>Type</dt><dd>{currentType?.name}</dd></div>
            <div><dt>Owner</dt><dd>{owner?.email} <span className="provision-verified">Verified</span></dd></div>
            <div><dt>Market</dt><dd>{market?.name} · {currency}</dd></div>
            <div><dt>Timezone</dt><dd>{timeZone}</dd></div>
            <div><dt>Plan</dt><dd>Starter · {trialDays}-day trial</dd></div>
            <div><dt>First Location</dt><dd>{location.trim()}</dd></div>
          </dl>
          <p className="provision-submit-note"><ShieldCheck size={17} /> Confirming will provision a real business and log the action in Platform Audit.</p>
          <form onSubmit={event => void create(event)}>
            <label className="provision-confirm-label">
              <input type="checkbox" className="form-check-input" checked={confirmed}
                disabled={busy || submissionUncertain || loading || !!loadError}
                onChange={event => setConfirmed(event.target.checked)} />
              <span>I have checked the business details and verified the Owner assignment.</span>
            </label>
            {submissionUncertain && <div className="alert alert-warning mt-3" role="alert">
              The previous request may have completed. Retry sends the same details and request key to recover the result safely.
            </div>}
            <div className="form-footer">
              <button type="button" className="btn btn-outline-secondary" disabled={busy || submissionUncertain}
                onClick={() => { setError(''); setStep(2) }}>Back</button>
              <button type="submit" className="btn btn-primary" disabled={busy || !owner || !canSubmit}>
                {busy ? 'Creating business…' : submissionUncertain ? 'Retry Creation' : 'Confirm & Create Business'} <ArrowRight size={16} />
              </button>
            </div>
          </form>
        </>}
      </section>

      <aside className="content-section provision-aside" aria-label="Business creation guidance">
        <span className="provision-side-kicker">PLATFORM SETUP</span>
        <h2>One business. One secure workspace.</h2>
        <p>The new business will be independent from other platform customers. Its Owner only gets their own permitted operations.</p>
        <div className="provision-side-row"><CheckCircle2 size={17} /> Separate Business ID</div>
        <div className="provision-side-row"><CheckCircle2 size={17} /> Owner membership</div>
        <div className="provision-side-row"><CheckCircle2 size={17} /> First shop location</div>
        <div className="provision-side-row"><CheckCircle2 size={17} /> Trial & audit record</div>
        <div className="provision-side-warning">Existing verified owners only. Pending owner invitations and paid plan selection will be added with their secure backend flows.</div>
      </aside>
    </div>
  </div>
}
