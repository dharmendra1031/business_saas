import { useLayoutEffect, useRef, useState, type FormEvent } from 'react'
import { api, post } from './api'
import { ActionButton } from './ActionFeedback'
import { moneyCents } from './posMoney'

interface Item { id: number; name: string; barcode: string | null; brand: string; packSize: string; unit: string; revision: number }
interface Page { items: Item[]; nextAfterId: number | null }
export interface AdoptedProduct { id: number; name: string; sku: string; unitCode: string; isActive: boolean; conversions?: { unitId: number; factor: number; unitCode?: string; isActive?: boolean }[] }

export default function GlobalProductPicker({ businessId, currencyCode, initialSearch = '', onClose, onSelect, onPrivateProduct }: {
  onPrivateProduct?: () => void
  businessId: number; currencyCode: string; initialSearch?: string; onClose: () => void; onSelect: (product: AdoptedProduct) => void
}) {
  const dialog = useRef<HTMLDialogElement>(null)
  const lock = useRef(false)
  const [term, setTerm] = useState(initialSearch)
  const [page, setPage] = useState<Page | null>(null)
  const [selected, setSelected] = useState<Item | null>(null)
  const [price, setPrice] = useState('')
  const [linkExisting, setLinkExisting] = useState(false)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  const [savedId, setSavedId] = useState<number | null>(null)
  useLayoutEffect(() => {
    const element = dialog.current
    element?.showModal()
    return () => { element?.close() }
  }, [])
  const base = '/businesses/' + businessId
  const validPrice = price !== '' && moneyCents(price) > 0 && moneyCents(price) <= 999999999999
  async function search(event?: FormEvent, afterId?: number) {
    event?.preventDefault()
    if (lock.current || !term.trim()) return
    lock.current = true; setBusy(true); setError('')
    try {
      const value = term.trim()
      const params = new URLSearchParams({ [/^(?:[0-9]{8}|[0-9]{12,14})$/.test(value) ? 'barcode' : 'search']: value })
      if (afterId) params.set('afterId', String(afterId))
      setPage(await api<Page>(base + '/global-products?' + params))
    } catch (cause) { setPage(null); setError(cause instanceof Error ? cause.message : 'Catalogue search failed.') }
    finally { lock.current = false; setBusy(false) }
  }
  async function adopt(event: FormEvent) {
    event.preventDefault()
    if (!selected || lock.current || !validPrice) return
    lock.current = true; setBusy(true); setError('')
    try {
      const id = savedId ?? (await post<{ id: number }>(base + '/global-products/' + selected.id + '/adopt',
        { salePrice: Number(price), expectedRevision: selected.revision, linkExisting })).id
      setSavedId(id)
      const product = await api<AdoptedProduct>(base + '/products/' + id)
      if (!product.isActive) throw new Error('Product is inactive. Review it in My Products.')
      onSelect(product)
    } catch (cause) { setError(cause instanceof Error ? cause.message : 'Unable to verify product. Retry the same selection.') }
    finally { lock.current = false; setBusy(false) }
  }
  return <dialog ref={dialog} className="entry-dialog" aria-label="Global product catalogue"
    onCancel={event => { event.preventDefault(); if (!lock.current) onClose() }}>
    <div className="d-flex justify-content-between align-items-center gap-2 mb-3">
      <h3 className="h5 mb-0">Global product catalogue</h3>
      <button type="button" className="btn btn-sm btn-outline-secondary" disabled={busy} onClick={onClose}>Close</button>
    </div>
    {!selected ? <>
      <form className="input-group" onSubmit={event => void search(event)}>
        <input aria-label="Catalogue name or barcode" className="form-control" value={term} maxLength={150}
          disabled={busy} onChange={event => { setTerm(event.target.value); setPage(null) }} placeholder="Name or barcode" />
        <ActionButton busy={busy} disabled={!term.trim()}>Search</ActionButton>
      </form>
      {page && <div className="mt-2">
        {page.items.length === 0 && <p role="status">No matching catalogue products.</p>}
        {page.items.length === 0 && onPrivateProduct && <button type="button" className="btn btn-outline-secondary" onClick={onPrivateProduct}>Create private product</button>}
        {page.items.map(item => <button type="button" key={item.id} className="btn btn-outline-secondary text-start w-100 mb-2"
          disabled={busy} onClick={() => { setSelected(item); setPrice(''); setLinkExisting(false); setSavedId(null); setError('') }}>
          <strong>{item.name}</strong><span className="d-block small">{[item.brand, item.packSize, item.unit, item.barcode].filter(Boolean).join(' · ')}</span>
        </button>)}
        {page.nextAfterId != null && <button type="button" className="btn btn-sm btn-outline-secondary" disabled={busy}
          onClick={() => void search(undefined, page.nextAfterId ?? undefined)}>Next results</button>}
      </div>}
    </> : <form onSubmit={event => void adopt(event)}>
      <p><strong>{selected.name}</strong><span className="d-block small">{[selected.brand, selected.packSize, selected.unit, selected.barcode].filter(Boolean).join(' · ')}</span></p>
      <fieldset disabled={busy || savedId !== null}>
        <label htmlFor="catalogue-price" className="form-label">Selling price ({currencyCode}) *</label>
        <input id="catalogue-price" type="number" min="0.01" max="9999999999.99" step="0.01" required className="form-control"
          value={price} onChange={event => setPrice(event.target.value)} />
        {selected.barcode && <label className="d-flex gap-2 mt-2">
          <input type="checkbox" checked={linkExisting} onChange={event => setLinkExisting(event.target.checked)} />
          <span>Link an existing active product with matching barcode and unit; retain its price.</span>
        </label>}
      </fieldset>
      <p className="small mt-2">Stock does not change. Previously linked products keep their price.</p>
      <div className="d-flex gap-2">
        <button type="button" className="btn btn-outline-secondary" disabled={busy || savedId !== null} onClick={() => { setSelected(null); setError('') }}>Back</button>
        <ActionButton busy={busy} disabled={!validPrice}>{savedId !== null ? 'Verify saved product' : 'Add to My Products'}</ActionButton>
      </div>
    </form>}
    {error && <p role="alert" className="text-danger small mt-2 mb-0">{error}</p>}
  </dialog>
}
