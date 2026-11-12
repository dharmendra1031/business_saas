import { useState } from 'react'
import RetailMasters from './RetailMasters'
import RetailProducts from './RetailProducts'

export default function RetailCatalog({ businessId, canOperate, currencyCode }: {
  businessId: number
  canOperate: boolean
  currencyCode: string
}) {
  const [tab, setTab] = useState<'products' | 'masters'>('products')

  return (
    <>
      <nav className="catalog-navigation" aria-label="Retail catalog">
        <button type="button" className={`btn ${tab === 'products' ? 'btn-primary' : 'btn-outline-secondary'}`}
          aria-pressed={tab === 'products'} onClick={() => setTab('products')}>Products</button>
        <button type="button" className={`btn ${tab === 'masters' ? 'btn-primary' : 'btn-outline-secondary'}`}
          aria-pressed={tab === 'masters'} onClick={() => setTab('masters')}>Categories &amp; Units</button>
      </nav>
      {tab === 'products'
        ? <RetailProducts businessId={businessId} canOperate={canOperate} currencyCode={currencyCode} />
        : <RetailMasters businessId={businessId} canOperate={canOperate} />}
    </>
  )
}
