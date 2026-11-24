import { useEffect, useState, type FormEvent } from 'react'
import { api, post, SessionExpiredError, type StaffMember } from './api'
import { ActionButton, useActionFeedback } from './ActionFeedback'

export default function TeamMembers({ businessId, canOperate, cashSaleEnabled }: { businessId: number; canOperate: boolean; cashSaleEnabled: boolean }) {
  const { run } = useActionFeedback()
  const [members, setMembers] = useState<StaffMember[]>([])
  const [email, setEmail] = useState('')
  const [role, setRole] = useState<'Manager' | 'Cashier'>('Manager')
  const [loading, setLoading] = useState(true)
  const [loadFailed, setLoadFailed] = useState(false)
  const [reload, setReload] = useState(0)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  const [confirmRemove, setConfirmRemove] = useState<number | null>(null)
  const path = `/businesses/${businessId}/staff`

  useEffect(() => {
    let active = true
    setLoading(true)
    setLoadFailed(false)
    setMembers([])
    setError('')
    api<StaffMember[]>(path)
      .then(list => { if (active) setMembers(list) })
      .catch(cause => {
        if (!active || cause instanceof SessionExpiredError) return
        setLoadFailed(true)
        setError(cause instanceof Error ? cause.message : 'Unable to load staff.')
      })
      .finally(() => { if (active) setLoading(false) })

    return () => { active = false }
  }, [path, reload])

  async function add(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (busy) return
    setBusy(true)
    setError('')
    try {
      const member = await run('Adding team member…', 'Team member added.',
        () => post<StaffMember>(path, { email: email.trim(), roleCode: role }))
      setMembers(current => [...current, member])
      setEmail('')
    } catch (cause) {
      if (!(cause instanceof SessionExpiredError))
        setError(cause instanceof Error ? cause.message : 'Unable to add user.')
    } finally {
      setBusy(false)
    }
  }

  async function remove(member: StaffMember) {
    if (busy || confirmRemove !== member.userId) return
    setBusy(true)
    setError('')
    try {
      await run('Removing team access…', 'Business access removed.',
        () => api<void>(`${path}/${member.userId}`, { method: 'DELETE' }))
      setMembers(current => current.filter(item => item.userId !== member.userId))
      setConfirmRemove(null)
    } catch (cause) {
      if (!(cause instanceof SessionExpiredError))
        setError(cause instanceof Error ? cause.message : 'Unable to remove user.')
    } finally {
      setBusy(false)
    }
  }

  return (
    <section className="content-section" aria-labelledby="team-heading">
      <h2 id="team-heading">Team Access</h2>
      <p className="text-secondary small mb-3">
        Only existing accounts can be added. Team members work at the primary location. {cashSaleEnabled ? "Cashiers can record cash sales." : "Cash checkout is unavailable."}
      </p>
      {error && <div className="alert alert-danger" role="alert">{error}</div>}
      {!canOperate && <p role="status">Team access is read-only while the subscription is inactive.</p>}
      <form onSubmit={event => void add(event)} className="team-form mb-4" aria-busy={busy}>
        <div>
          <label className="form-label" htmlFor="staff-email">Account email</label>
          <input id="staff-email" className="form-control" type="email" autoComplete="off"
            value={email} onChange={event => setEmail(event.target.value)}
            placeholder="person@example.com" maxLength={256} required />
        </div>
        <div>
          <label className="form-label" htmlFor="staff-role">Access level</label>
          <select id="staff-role" className="form-select" value={role}
            onChange={event => setRole(event.target.value as 'Manager' | 'Cashier')}>
            <option value="Manager">Manager</option>
            <option value="Cashier">Cashier</option>
          </select>
        </div>
        <ActionButton busy={busy} disabled={!canOperate || loading || loadFailed}>
          Add Team Member
        </ActionButton>
      </form>

      {loading ? <p className="text-secondary" role="status">Loading team…</p>
        : loadFailed ? <button type="button" className="btn btn-outline-secondary"
            onClick={() => setReload(value => value + 1)}>Retry loading team</button>
          : members.length === 0 ? <p className="text-secondary">No team members yet.</p>
            : <div className="table-responsive">
            <table className="table table-sm align-middle mb-0">
              <thead><tr><th scope="col">Account</th><th scope="col">Role</th><th scope="col" className="text-end">Action</th></tr></thead>
              <tbody>
                {members.map(member => (
                  <tr key={member.userId}>
                    <td className="text-break">{member.email}</td>
                    <td>{member.roleCode}</td>
                    <td className="text-end">
                      {member.roleCode !== 'Owner' && (confirmRemove === member.userId
                        ? <span role="group" aria-label={`Confirm removal of ${member.email}`}
                          className="d-inline-flex align-items-center flex-wrap gap-2">
                            <span className="small">Remove access?</span>
                            <button type="button" className="btn btn-danger btn-sm" disabled={!canOperate || busy}
                              onClick={() => void remove(member)}>Confirm</button>
                            <button type="button" className="btn btn-outline-secondary btn-sm" disabled={busy}
                              onClick={() => setConfirmRemove(null)}>Cancel</button>
                          </span>
                        : <button type="button" className="btn btn-outline-danger btn-sm"
                            disabled={!canOperate || busy} onClick={() => setConfirmRemove(member.userId)}
                            aria-label={`Remove ${member.email}`}>Remove</button>)}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>}
    </section>
  )
}
