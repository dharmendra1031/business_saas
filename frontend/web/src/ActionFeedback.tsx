import { createContext, useCallback, useContext, useEffect, useRef, useState, type ButtonHTMLAttributes, type ReactNode } from 'react'
import { AlertCircle, CheckCircle2, LoaderCircle, X } from 'lucide-react'
import { SessionExpiredError, UncertainWriteError } from './api'

type Phase = 'saving' | 'success' | 'error' | 'uncertain'
interface Feedback { id: number; phase: Phase; message: string }

interface FeedbackActions {
  run<T>(savingMessage: string, successMessage: string | ((result: T) => string),
    action: () => Promise<T>): Promise<T>
}

const Context = createContext<FeedbackActions | null>(null)

// One app-level UI surface, no extra state machine or API mutation interception.
// Only confirmed CRUD actions use this; payments/receipts keep their own recovery UI.
export function ActionFeedbackProvider({ children }: { children: ReactNode }) {
  const [feedback, setFeedback] = useState<Feedback | null>(null)
  const sequence = useRef(0)

  const run = useCallback(async <T,>(savingMessage: string,
    successMessage: string | ((result: T) => string),
    action: () => Promise<T>): Promise<T> => {
    const id = ++sequence.current
    setFeedback({ id, phase: 'saving', message: savingMessage })
    try {
      const result = await action()
      setFeedback(current => current?.id === id
        ? { id, phase: 'success',
          message: typeof successMessage === 'function' ? successMessage(result) : successMessage }
        : current)
      return result
    } catch (cause) {
      setFeedback(current => current?.id === id
        ? cause instanceof SessionExpiredError ? null
          : { id, phase: cause instanceof UncertainWriteError ? 'uncertain' : 'error',
            message: cause instanceof Error ? cause.message : 'Could not save. Review the form and try again.' }
        : current)
      throw cause
    }
  }, [])

  useEffect(() => {
    if (feedback?.phase !== 'success') return
    const id = feedback.id
    const timeout = window.setTimeout(() => {
      setFeedback(current => current?.id === id ? null : current)
    }, 5000)
    return () => window.clearTimeout(timeout)
  }, [feedback])

  return <Context.Provider value={{ run }}>
    {children}
    {feedback && <div className={`action-feedback action-feedback--${feedback.phase}`}
      role={feedback.phase === 'error' || feedback.phase === 'uncertain' ? 'alert' : 'status'}
      aria-live={feedback.phase === 'error' || feedback.phase === 'uncertain' ? 'assertive' : 'polite'}
      aria-atomic="true">
      {feedback.phase === 'saving'
        ? <LoaderCircle className="action-feedback-spinner" size={22} aria-hidden="true" />
        : feedback.phase === 'success'
          ? <CheckCircle2 size={22} aria-hidden="true" />
          : <AlertCircle size={22} aria-hidden="true" />}
      <div className="action-feedback-copy">
        <strong>{feedback.phase === 'saving' ? 'Saving changes' :
          feedback.phase === 'success' ? 'Saved successfully' :
          feedback.phase === 'uncertain' ? 'Save status unknown' : 'Could not save'}</strong>
        <span>{feedback.message}</span>
      </div>
      {feedback.phase !== 'saving' && <button type="button"
        className="action-feedback-dismiss" aria-label="Dismiss notification"
        onClick={() => setFeedback(null)}><X size={18} aria-hidden="true" /></button>}
    </div>}
  </Context.Provider>
}

export function useActionFeedback() {
  const context = useContext(Context)
  if (!context) throw new Error('Action feedback provider is missing.')
  return context
}

interface ActionButtonProps extends ButtonHTMLAttributes<HTMLButtonElement> {
  busy: boolean
  busyLabel?: string
  children: ReactNode
}

export function ActionButton({ busy, busyLabel = 'Saving…', children,
  disabled, className = '', type = 'submit', ...props }: ActionButtonProps) {
  return <button {...props} type={type}
    className={`btn btn-primary ${className}`.trim()}
    disabled={disabled || busy} aria-busy={busy}>
    {busy && <LoaderCircle size={17} aria-hidden="true"
      className="action-feedback-spinner" />}
    {busy ? busyLabel : children}
  </button>
}
