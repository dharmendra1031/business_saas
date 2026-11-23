import { useEffect, useState } from 'react'
import { api } from './api'

type ActiveNotice = { id: number; level: string; title: string; message: string }
type Availability = { maintenance: { id: number; mode: string; title: string; message: string } | null;
  announcements: ActiveNotice[] }

// All notice content is server-provided plain text; never insert HTML.
export default function PlatformNoticeBanner() {
  const [data, setData] = useState<Availability | null>(null)
  const [hidden, setHidden] = useState<string[]>([])
  useEffect(() => {
    let active = true
    api<Availability>('/public/platform-availability')
      .then(value => { if (active) setData(value) })
      .catch(() => { if (active) setData(null) })
    return () => { active = false }
  }, [])

  if (!data) return null
  const notices = [
    ...(data.maintenance ? [{
      key: 'maintenance-' + data.maintenance.id,
      level: data.maintenance.mode === 'NoticeOnly' ? 'Information' : 'Warning',
      title: data.maintenance.title, message: data.maintenance.message
    }] : []),
    ...data.announcements.map(x => ({
      key: 'announcement-' + x.id, level: x.level, title: x.title, message: x.message
    }))
  ].filter(x => !hidden.includes(x.key))

  return <div className="platform-public-notices" aria-label="Platform notices">
    {notices.map(x => <div key={x.key} role="status"
      className={`platform-public-notice level-${x.level.toLowerCase()}`}>
      <span><strong>{x.title}</strong> {x.message}</span>
      <button type="button" aria-label={`Dismiss ${x.title}`}
        onClick={() => setHidden(current => [...current, x.key])}>Dismiss</button>
    </div>)}
  </div>
}
