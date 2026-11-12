import { useState } from 'react'
import RetailMasters from './RetailMasters'
import RetailProducts from './RetailProducts'
import RetailContacts from './RetailContacts'
import RetailStock from './RetailStock'

type CatalogTab = 'products' | 'masters' | 'suppliers' | 'customers' | 'stock'

export default function RetailCatalog({ businessId, canOperate, currencyCode, locations }: {
  businessId: number
  canOperate: boolean
  currencyCode: string
  locations: { id: number; name: string; code: string }[]
}) {
  const [tab, setTab] = useState<CatalogTab>('products')

  return (
    <>
      <nav className="catalog-navigation" aria-label="Retail catalog">
        {([
          ['products', 'Products'],
          ['masters', 'Categories & Units'],
          ['suppliers', 'Suppliers'],
          ['customers', 'Customers'],
          ['stock', 'Stock']
        ] as const).map(([key, name]) =>
          <button key={key} type="button"
            className={`btn ${tab === key ? 'btn-primary' : 'btn-outline-secondary'}`}
            aria-pressed={tab === key} onClick={() => setTab(key)}>{name}</button>
        )}
      </nav>
      {tab === 'products'
        ? <RetailProducts businessId={businessId} canOperate={canOperate} currencyCode={currencyCode} />
        : tab === 'masters'
          ? <RetailMasters businessId={businessId} canOperate={canOperate} />
          : tab === 'stock'
            ? <RetailStock businessId={businessId} canOperate={canOperate} locations={locations} />
            : <RetailContacts key={tab} type={tab} businessId={businessId} canOperate={canOperate} />}
    </>
  )
}
