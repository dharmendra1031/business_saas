// POS amounts use integer minor units (two decimal places, matching the API).
// Stay within a range where JSON numbers still distinguish adjacent cents.
export const maxExactCents = 2 ** 52 - 1

export function moneyCents(value: string | number): number {
  const text = String(value).trim()
  if (!/^(?:\d+(?:\.\d*)?|\.\d+)$/.test(text)) return NaN
  const [whole, fraction = ''] = text.split('.')
  if (/[1-9]/.test(fraction.slice(2))) return NaN
  const cents = Number(whole || '0') * 100 + Number((fraction + '00').slice(0, 2))
  return Number.isSafeInteger(cents) && cents <= maxExactCents ? cents : NaN
}
