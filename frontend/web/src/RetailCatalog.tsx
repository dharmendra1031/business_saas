import RetailMasters from './RetailMasters'
import RetailProducts from './RetailProducts'
import RetailContacts from './RetailContacts'
import RetailStock from './RetailStock'
import RetailPurchases from './RetailPurchases'

export type RetailSection = 'products' | 'masters' | 'suppliers' | 'customers' | 'stock' | 'purchases'

export default function RetailCatalog({ section, businessId, canOperate, currencyCode, locations, roleCode, approvalsOnly, initialStockAlertLocationId, userId }: {
  userId: number
  approvalsOnly: boolean
  initialStockAlertLocationId: number | null
  section: RetailSection
  businessId: number
  canOperate: boolean
  currencyCode: string
  locations: { id: number; name: string; code: string }[]
  roleCode: 'Owner' | 'Manager'
}) {
  switch (section) {
    case 'products':
      return <RetailProducts businessId={businessId} canOperate={canOperate} currencyCode={currencyCode} />
    case 'masters':
      return <RetailMasters businessId={businessId} canOperate={canOperate} />
    case 'stock':
      return <RetailStock businessId={businessId} canOperate={canOperate}
        locations={locations} isOwner={roleCode === 'Owner'}
        initialAlertLocationId={initialStockAlertLocationId} />
    case 'purchases':
      return <RetailPurchases initialApprovalsOnly={approvalsOnly} businessId={businessId} canOperate={canOperate}
        locations={locations} roleCode={roleCode} currencyCode={currencyCode} />
    case 'suppliers':
    case 'customers':
      return <RetailContacts type={section} businessId={businessId} canOperate={canOperate}
        isOwner={roleCode === 'Owner'} currencyCode={currencyCode} userId={userId} />
  }
}
