import { useState } from 'react'
import RetailMasters from './RetailMasters'
import RetailProducts from './RetailProducts'
import RetailContacts from './RetailContacts'
import RetailStock from './RetailStock'
import RetailPurchases from './RetailPurchases'

type CatalogTab = 'products' | 'masters' | 'suppliers' | 'customers' | 'stock' | 'purchases'

export default function RetailCatalog({ businessId, canOperate, currencyCode, locations, roleCode }: {
  businessId: number
  canOperate: boolean
  currencyCode: string
  locations: { id: number; name: string; code: string }[]
  roleCode: 'Owner' | 'Manager'
}) {
  const [tab, setTab] = useState<CatalogTab>('products')

  return (
    <>
      <nav className="catalog-navigation flex-column align-items-stretch" aria-label="Retail workspace">
        {([
          { title: 'Daily work', tabs: [
            ['products', 'Products'], ['stock', 'Stock'], ['purchases', 'Purchase']
          ] },
          { title: 'Shop setup', tabs: [
            ['masters', 'Categories & Units'], ['suppliers', 'Suppliers'], ['customers', 'Customers']
          ] }
        ] as const).map(section => <div key={section.title}>
          <div className="small fw-semibold text-secondary mb-2">{section.title}</div>
          <div className="d-flex flex-wrap gap-2">
            {section.tabs.map(([key, name]) => <button key={key} type="button"
              className={`btn ${tab === key ? 'btn-primary' : 'btn-outline-secondary'}`}
              aria-pressed={tab === key} onClick={() => setTab(key)}>{name}</button>)}
          </div>
        </div>)}
      </nav>
      {tab === 'products'
        ? <RetailProducts businessId={businessId} canOperate={canOperate} currencyCode={currencyCode} />
        : tab === 'masters'
          ? <RetailMasters businessId={businessId} canOperate={canOperate} />
          : tab === 'purchases'
            ? <RetailPurchases businessId={businessId} canOperate={canOperate}
                locations={locations} roleCode={roleCode} currencyCode={currencyCode} />
          : tab === 'stock'
            ? <RetailStock businessId={businessId} canOperate={canOperate}
                locations={locations} isOwner={roleCode === 'Owner'} />
            : <RetailContacts key={tab} type={tab} businessId={businessId} canOperate={canOperate} />}
    </>
  )
}
