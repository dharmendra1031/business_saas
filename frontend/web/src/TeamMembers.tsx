import { useEffect, useState, type FormEvent } from 'react'
import { api, post, SessionExpiredError, type StaffMember } from './api'

export default function TeamMembers({ businessId }: { businessId: number }) {
  const [members, setMembers] = useState<StaffMember[]>([])
  const [email, setEmail] = useState('')
  const [role, setRole] = useState<'Manager' | 'Cashier'>('Manager')
  const [loading, setLoading] = useState(true)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  const [notice, setNotice] = useState('')
  const path = `/businesses/${businessId}/staff`

  useEffect(() => {
    let active = true
    api<StaffMember[]>(path)
      .then(list => { if (active) setMembers(list) })
      .catch(cause => {
        if (active && !(cause instanceof SessionExpiredError))
          setError(cause instanceof Error ? cause.message : 'Unable to load staff.')
      })
      .finally(() => { if (active) setLoading(false) })

    return () => { active = false }
  }, [path])

  async function add(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (busy) return
    setBusy(true)
    setError('')
    setNotice('')
    try {
      await post(path, { email: email.trim(), roleCode: role })
      setMembers(await api<StaffMember[]>(path))
      setEmail('')
      setNotice('Team member added.')
    } catch (cause) {
      if (!(cause instanceof SessionExpiredError))
        setError(cause instanceof Error ? cause.message : 'Unable to add user.')
    } finally {
      setBusy(false)
    }
  }

  async function remove(member: StaffMember) {
    if (busy || !window.confirm(`Remove ${member.email} from this business?`)) return
    setBusy(true)
    setError('')
    setNotice('')
    try {
      await api<void>(`${path}/${member.userId}`, { method: 'DELETE' })
      setMembers(current => current.filter(item => item.userId !== member.userId))
      setNotice('Business access removed.')
    } catch (cause) {
      if (!(cause instanceof SessionExpiredError))
        setError(cause instanceof Error ? cause.message : 'Unable to remove user.')
    } finally {
      setBusy(false)
    }
  }

  return (
    <section className="content-section mt-3" aria-labelledby="team-heading">
      <h2 id="team-heading">Team Access</h2>
      <p className="text-secondary small mb-3">
        Add someone who already has a Business SaaS account. Email invitations are not available yet.
      </p>
      {error && <div className="alert alert-danger" role="alert">{error}</div>}
      {notice && <div className="alert alert-success" role="status">{notice}</div>}
      <form onSubmit={event => void add(event)} className="team-form mb-4">
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
        <button className="btn btn-primary" type="submit" disabled={busy}>
          {busy ? 'Saving…' : 'Add Team Member'}
        </button>
      </form>

      {loading ? <p className="text-secondary" role="status">Loading team…</p>
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
                      {member.roleCode !== 'Owner' &&
                        <button type="button" className="btn btn-outline-danger btn-sm"
                          disabled={busy} onClick={() => void remove(member)}
                          aria-label={`Remove ${member.email}`}>Remove</button>}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>}
    </section>
  )
}
