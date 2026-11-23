import React from 'react'
import ReactDOM from 'react-dom/client'
import 'bootstrap/dist/css/bootstrap.min.css'
import './styles.css'
import './styles.auth.css'
import './styles.platform.css'
import './styles.business.css'
import './styles.release.css'
import './styles.pos.css'
import App from './App'
import PlatformNoticeBanner from './PlatformNoticeBanner'
import OwnerInviteAcceptance from './OwnerInviteAcceptance'

// Remove the emailed secret from browser history before any API requests or analytics.
const invitationToken = new URLSearchParams(window.location.search).get('invite')
if (invitationToken) window.history.replaceState(null, '', window.location.pathname)

ReactDOM.createRoot(document.getElementById('root')!).render(
  <React.StrictMode>
    <PlatformNoticeBanner />
    {invitationToken ? <OwnerInviteAcceptance token={invitationToken} /> : <App />}
  </React.StrictMode>
)
