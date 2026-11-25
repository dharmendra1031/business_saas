// Keep converted quantities exact to the same three decimals as the stock ledger.
export function baseQuantity(quantity: number, factor: number): number | null {
  if (!Number.isFinite(quantity) || quantity <= 0 || quantity > 9999999.999 ||
    !Number.isFinite(factor) || factor < 0.001 || factor > 9999999.999 ||
    Math.round(quantity * 1000) / 1000 !== quantity ||
    Math.round(factor * 1000) / 1000 !== factor) return null
  const scaled = BigInt(Math.round(quantity * 1000)) * BigInt(Math.round(factor * 1000))
  if (scaled % 1000n !== 0n || scaled / 1000n > 9999999999999n) return null
  return Number(scaled / 1000n) / 1000
}
