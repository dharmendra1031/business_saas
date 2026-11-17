// A small read-only smoke check, not a production load or capacity test.
const target = new URL(process.argv[2] ?? 'http://127.0.0.1:5080/api/ready')
const count = Number(process.argv[3] ?? 50)
const concurrency = Number(process.argv[4] ?? 5)

if (!['http:', 'https:'].includes(target.protocol) ||
    !Number.isInteger(count) || count < 1 || count > 500 ||
    !Number.isInteger(concurrency) || concurrency < 1 || concurrency > 20) {
  throw new Error('Usage: node scripts/api-load-smoke.mjs [url] [requests:1-500] [concurrency:1-20]')
}
if (!['/api/health', '/api/ready'].includes(target.pathname) || target.search) {
  throw new Error('Only read-only /api/health and /api/ready probes are supported.')
}

let next = 0
let failures = 0
const timings = []
const started = performance.now()

async function worker() {
  while (next < count) {
    next += 1
    const before = performance.now()
    try {
      const response = await fetch(target, {
        redirect: 'error',
        signal: AbortSignal.timeout(10_000)
      })
      await response.arrayBuffer()
      if (!response.ok) failures += 1
    } catch {
      failures += 1
    }
    timings.push(performance.now() - before)
  }
}

await Promise.all(Array.from({ length: Math.min(count, concurrency) }, worker))
timings.sort((a, b) => a - b)
const percentile = fraction => timings[Math.ceil(count * fraction) - 1].toFixed(1)
const seconds = (performance.now() - started) / 1000

console.log(`Target: ${target.origin}${target.pathname}`)
console.log(`Requests: ${count}, concurrency: ${concurrency}, failures: ${failures}`)
console.log(`p50: ${percentile(0.5)} ms, p95: ${percentile(0.95)} ms, throughput: ${(count / seconds).toFixed(1)} req/s`)
if (failures > 0) process.exitCode = 1
