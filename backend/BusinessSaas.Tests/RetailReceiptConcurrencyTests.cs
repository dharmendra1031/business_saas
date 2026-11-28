using System.Diagnostics;
using System.Text.Json;
using BusinessSaas.Api.Data;
using BusinessSaas.Api.Reports;
using Xunit;

namespace BusinessSaas.Tests;

public sealed class RetailReceiptConcurrencyTests
{
    [Fact]
    [Trait("Renderer", "ConcurrentWindows")]
    public async Task Concurrent_static_receipts_have_independent_data_and_measure_baseline()
    {
        if (!OperatingSystem.IsWindows()) return;

        RetailReceiptRenderer.WarmUp();
        var output = Environment.GetEnvironmentVariable("RDLC_TEST_OUTPUT");
        if (!string.IsNullOrWhiteSpace(output)) Directory.CreateDirectory(output);

        // Small, bounded CI pilot; larger bursts belong on an isolated staging host.
        foreach (var count in new[] { 1, 10, 25 })
        {
            var data = Enumerable.Range(1, count).Select(i =>
            {
                var business = new Business
                {
                    Id = i, Name = $"Benchmark Shop {i:D3}", ReceiptShowLogo = false
                };
                var sale = new RetailCashSale
                {
                    Id = i, BusinessId = i, CurrencyCode = "INR",
                    Total = 5m * i, Tendered = 5m * i,
                    PostedAtUtc = new DateTime(2026, 11, 24, 10, 0, 0, DateTimeKind.Utc),
                    Lines = Enumerable.Range(1, 5).Select(n => new RetailCashSaleLine
                    {
                        Id = n, ProductName = $"Benchmark Item {i:D3}-{n}",
                        Quantity = 1m, UnitPrice = i, LineTotal = i
                    }).ToList()
                };
                return RetailReceiptReportData.FromCashSale(sale, business, TimeZoneInfo.Utc);
            }).ToArray();
            var timings = new double[count];
            var pdfs = new byte[count][];
            using var ready = new CountdownEvent(count);
            using var start = new ManualResetEventSlim(false);
            using var process = Process.GetCurrentProcess();
            var workers = Enumerable.Range(0, count).Select(index =>
                Task.Factory.StartNew(() =>
                {
                    ready.Signal();
                    start.Wait();
                    var began = Stopwatch.GetTimestamp();
                    pdfs[index] = RetailReceiptRenderer.Render(data[index],
                        index % 2 == 0 ? "Thermal80" : "A4");
                    timings[index] = Stopwatch.GetElapsedTime(began).TotalMilliseconds;
                }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default))
                .ToArray();

            var allReady = ready.Wait(TimeSpan.FromSeconds(30));
            process.Refresh();
            var cpuBefore = process.TotalProcessorTime;
            var memoryBefore = process.WorkingSet64;
            var allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
            var beganBatch = Stopwatch.GetTimestamp();
            start.Set();
            await Task.WhenAll(workers);
            var elapsed = Stopwatch.GetElapsedTime(beganBatch);
            process.Refresh();
            var cpuMs = (process.TotalProcessorTime - cpuBefore).TotalMilliseconds;
            var allocatedBytes = GC.GetTotalAllocatedBytes(precise: true) - allocatedBefore;
            Assert.True(allReady, "Concurrent workers did not reach the start gate.");
            Assert.All(pdfs, pdf =>
            {
                Assert.True(pdf.AsSpan().StartsWith("%PDF-"u8));
                Assert.True(pdf.Length > 1000);
            });

            // Write files only after the timed batch; Python checks actual extracted content.
            if (!string.IsNullOrWhiteSpace(output))
            {
                for (var i = 0; i < count; i++)
                    File.WriteAllBytes(Path.Combine(output, $"{count:D3}-{i + 1:D3}.pdf"), pdfs[i]);
                Array.Sort(timings);
                double Percentile(double fraction) => timings[(int)Math.Ceiling(count * fraction) - 1];
                File.WriteAllText(Path.Combine(output, $"{count:D3}.json"),
                    JsonSerializer.Serialize(new
                    {
                        count, averageMs = timings.Average(), p50Ms = Percentile(.50),
                        p95Ms = Percentile(.95), p99Ms = Percentile(.99),
                        elapsedMs = elapsed.TotalMilliseconds, throughput = count / elapsed.TotalSeconds,
                        cpuMs, memoryBeforeBytes = memoryBefore,
                        memoryAfterBytes = process.WorkingSet64,
                        processPeakWorkingSetBytes = process.PeakWorkingSet64,
                        allocatedBytes, processorCount = Environment.ProcessorCount,
                        failures = 0
                    }));
            }
        }
    }
}
