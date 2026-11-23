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

ReactDOM.createRoot(document.getElementById('root')!).render(
  <React.StrictMode>
    <PlatformNoticeBanner />
    <App />
  </React.StrictMode>
)
