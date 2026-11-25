import { useLayoutEffect, useRef, useState } from 'react'
import { Search } from 'lucide-react'

export default function LookupDialog({ title, searchId, search, placeholder, columns, rows, status, context, pagination, onSearch, onSelect, onClose }: {
  title: string
  searchId: string
  search: string
  placeholder: string
  columns: string[]
  rows: { id: number; cells: string[]; label?: string }[]
  status: string
  context?: string
  pagination?: { page: number; hasMore: boolean; busy: boolean; onChange: (page: number) => void }
  onSearch: (value: string) => void
  onSelect: (id: number) => void
  onClose: () => void
}) {
  const dialog = useRef<HTMLDialogElement>(null)
  const input = useRef<HTMLInputElement>(null)
  useLayoutEffect(() => {
    const element = dialog.current
    if (!element) return
    element.showModal()
    input.current?.focus()
    return () => { element.close() }
  }, [])
  return <dialog ref={dialog} className="lookup-dialog" aria-label={title}
    onCancel={event => { event.preventDefault(); onClose() }}>
    <div className="d-flex align-items-center justify-content-between gap-2 mb-3">
      <h2 className="h5 mb-0">{title}</h2>
      <button type="button" className="btn btn-outline-secondary btn-sm" onClick={onClose}>Close</button>
    </div>
    {context && <p className="small mb-2">Location: {context}</p>}
    <label className="visually-hidden" htmlFor={searchId}>{placeholder}</label>
    <input ref={input} id={searchId} className="form-control mb-3" type="search" autoComplete="off"
      maxLength={100} value={search} placeholder={placeholder} onChange={event => onSearch(event.target.value)}
      onKeyDown={event => {
        if (event.key === 'Enter') {
          event.preventDefault()
          if (rows.length === 1) onSelect(rows[0].id)
        }
        if (event.key === 'ArrowDown') {
          event.preventDefault()
          dialog.current?.querySelector<HTMLButtonElement>('tbody button')?.focus()
        }
      }} />
    <div className="lookup-results table-responsive">
      <table className="table table-hover table-sm align-middle mb-0">
        <thead><tr>{columns.map(column => <th key={column} scope="col">{column}</th>)}</tr></thead>
        <tbody>{rows.map(row => <tr key={row.id} onClick={() => onSelect(row.id)}>
          <td><button type="button" className="btn btn-link text-dark p-0 text-start"
            id={searchId + '-option-' + row.id} aria-label={row.label ?? row.cells.join(' · ')}
            onClick={event => { event.stopPropagation(); onSelect(row.id) }}>{row.cells[0]}</button></td>
          {row.cells.slice(1).map((cell, index) => <td key={index}>{cell}</td>)}
        </tr>)}</tbody>
      </table>
    </div>
    <p className="lookup-status small mt-2 mb-0" role="status">{status}</p>
    {pagination && <div className="d-flex justify-content-end align-items-center gap-2 mt-2">
      <button type="button" className="btn btn-outline-secondary btn-sm" disabled={pagination.busy || pagination.page === 1}
        onClick={() => pagination.onChange(pagination.page - 1)}>Previous</button>
      <span className="small">Page {pagination.page}</span>
      <button type="button" className="btn btn-outline-secondary btn-sm" disabled={pagination.busy || !pagination.hasMore}
        onClick={() => pagination.onChange(pagination.page + 1)}>Next</button>
    </div>}
  </dialog>
}

export function MasterLookup({ id, title, value, options, emptyLabel, disabled, onChange }: {
  id: string
  title: string
  value: number
  options: { id: number; name: string }[]
  emptyLabel: string
  disabled?: boolean
  onChange: (id: number) => void
}) {
  const [open, setOpen] = useState(false)
  const [search, setSearch] = useState('')
  const selected = options.find(item => item.id === value)
  function show() { setSearch(''); setOpen(true) }
  const rows = options.filter(item => item.name.toLowerCase().includes(search.trim().toLowerCase()))
    .map(item => ({ id: item.id, cells: [item.name] }))
  return <>
    <button id={id} type="button" className="btn btn-outline-secondary w-100 text-start d-flex align-items-center justify-content-between gap-2"
      data-selected-id={value} disabled={disabled} aria-haspopup="dialog" aria-expanded={open} onClick={show}>
      <span>{selected?.name ?? emptyLabel}</span><Search size={16} aria-hidden="true" />
    </button>
    {open && <LookupDialog title={title} searchId={id + '-search'} search={search} placeholder="Search"
      columns={['Name']} rows={rows} status={rows.length ? '' : 'No matching records'}
      onSearch={setSearch} onSelect={selectedId => { onChange(selectedId); setOpen(false) }} onClose={() => setOpen(false)} />}
  </>
}
