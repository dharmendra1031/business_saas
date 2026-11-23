// One classification tree shared by Product Masters and Product forms.
// Categories store all three levels; Brand and Unit remain independent masters.
export interface ClassificationNode {
  id: number
  name: string
  parentId?: number | null
  isActive?: boolean
}

export function classificationTrail<T extends ClassificationNode>(
  nodes: readonly T[], selectedId: number
): T[] {
  const result: T[] = []
  const visited = new Set<number>()
  let current = selectedId
  while (current > 0 && !visited.has(current) && result.length < 3) {
    visited.add(current)
    const node = nodes.find(item => item.id === current)
    if (!node) return []
    result.unshift(node)
    current = node.parentId ?? 0
  }
  // Never present a broken or cyclic tree as a valid classification path.
  return current > 0 ? [] : result
}

export function classificationPath(nodes: readonly ClassificationNode[], id: number): string {
  return classificationTrail(nodes, id).map(node => node.name).join(' / ')
}
