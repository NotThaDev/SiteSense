using System.Diagnostics;
using SiteSense.Api.Models;
using SiteSense.Api.Store;
using SiteSense.StoreCrashWriter;

namespace SiteSense.Tests.Api.Store;

public sealed class AppendLogCrashTests : IDisposable
{
    private const int Seed = 42;
    private const int Runs = 10;
    private const long RunSpan = 10_000_000; // ms of counter space per run, keeps runs apart

    private readonly string _root = Path.Combine(Path.GetTempPath(), "sitesense-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Append_KilledMidWrite_LosesNoAcknowledgedRecord()
    {
        var rng = new Random(Seed);
        var dotnet = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet";
        var acknowledged = new List<(long Start, long Last)>();

        for (var run = 1; run <= Runs; run++)
        {
            var killAfter = rng.Next(1, 60);
            var start = run * RunSpan;
            var psi = new ProcessStartInfo(dotnet) { RedirectStandardOutput = true, UseShellExecute = false };
            psi.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "SiteSense.StoreCrashWriter.dll"));
            psi.ArgumentList.Add(_root);
            psi.ArgumentList.Add("append");
            psi.ArgumentList.Add(start.ToString());

            using var proc = Process.Start(psi)!;
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            long last = start;
            for (var n = 1; n <= killAfter; n++)
            {
                string? line;
                try
                {
                    line = await proc.StandardOutput.ReadLineAsync(cts.Token);
                }
                catch (OperationCanceledException)
                {
                    proc.Kill(entireProcessTree: true);
                    Assert.Fail($"Crash writer timed out. Seed {Seed}, run {run}, batch {n}.");
                    return;
                }
                if (line is null)
                {
                    Assert.Fail($"Crash writer exited early. Seed {Seed}, run {run}, batch {n}.");
                    return;
                }
                last = long.Parse(line);
            }
            proc.Kill(entireProcessTree: true);
            await proc.WaitForExitAsync(CancellationToken.None);
            acknowledged.Add((start, last));
        }

        var log = new FileAppendLog(_root);
        var read = await log.ReadRangeAsync<Position>("tenant-a", "dev-1", 0, long.MaxValue / 2, CancellationToken.None);
        var present = read.Select(r => r.TimestampMs).ToHashSet();
        foreach (var (start, last) in acknowledged)
            for (var c = start + 1; c <= last; c++)
                Assert.True(present.Contains(CrashWriter.T0 + c), $"Acknowledged record lost. Seed {Seed}, counter {c}.");
        Assert.All(read, r =>
        {
            Assert.Equal("dev-1", r.DeviceId);
            Assert.Equal(4.5, r.AccuracyM);
        });
    }

    [Fact]
    public async Task AppendNew_KilledAndResent_StoresEachTimestampOnce()
    {
        var rng = new Random(Seed);
        var dotnet = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet";
        var nextBatch = 0L;
        var lastAcknowledged = -1L;

        for (var run = 1; run <= Runs; run++)
        {
            var killAfter = rng.Next(1, 8);
            var psi = new ProcessStartInfo(dotnet) { RedirectStandardOutput = true, UseShellExecute = false };
            psi.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "SiteSense.StoreCrashWriter.dll"));
            psi.ArgumentList.Add(_root);
            psi.ArgumentList.Add("unique");
            psi.ArgumentList.Add(nextBatch.ToString());

            using var proc = Process.Start(psi)!;
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            for (var n = 1; n <= killAfter; n++)
            {
                string? line;
                try
                {
                    line = await proc.StandardOutput.ReadLineAsync(cts.Token);
                }
                catch (OperationCanceledException)
                {
                    proc.Kill(entireProcessTree: true);
                    Assert.Fail($"Crash writer timed out. Seed {Seed}, run {run}, batch {n}.");
                    return;
                }
                if (line is null)
                {
                    Assert.Fail($"Crash writer exited early. Seed {Seed}, run {run}, batch {n}.");
                    return;
                }
                lastAcknowledged = long.Parse(line);
            }
            proc.Kill(entireProcessTree: true);
            await proc.WaitForExitAsync(CancellationToken.None);
            // The next run resends the batch after the last acknowledged one, which may be half written.
            nextBatch = lastAcknowledged + 1;
        }

        var log = new FileAppendLog(_root);
        var read = await log.ReadRangeAsync<Position>("tenant-a", "dev-1", 0, long.MaxValue / 2, CancellationToken.None);
        Assert.True(read.Count == read.Select(r => r.TimestampMs).Distinct().Count(), $"Duplicate timestamp. Seed {Seed}.");
        var present = read.Select(r => r.TimestampMs).ToHashSet();
        for (long c = 1; c <= lastAcknowledged * 250 + 500; c++)
            Assert.True(present.Contains(CrashWriter.T0 + c * 10_000), $"Acknowledged record lost. Seed {Seed}, counter {c}.");
    }

    [Fact]
    public async Task DeleteBefore_KilledMidRun_KeepsRecentAndRerunFinishes()
    {
        const long hour = 3_600_000;
        const int devices = 50;
        var t0 = CrashWriter.T0;
        var cutoff = t0 + 24 * hour;
        var end = t0 + 48 * hour;
        var log = new FileAppendLog(_root);
        for (var d = 0; d < devices; d++)
        {
            var device = $"dev-{d}";
            var batch = Enumerable.Range(0, 48).Select(i => new Position(device, t0 + i * hour + 1, 45.5, 9.2, 4.5)).ToList();
            await log.AppendAsync("tenant-a", device, batch, CancellationToken.None);
        }

        var rng = new Random(Seed);
        var dotnet = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet";
        for (var run = 1; run <= 5; run++)
        {
            var psi = new ProcessStartInfo(dotnet) { RedirectStandardOutput = true, UseShellExecute = false };
            psi.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "SiteSense.StoreCrashWriter.dll"));
            psi.ArgumentList.Add(_root);
            psi.ArgumentList.Add("purge");
            psi.ArgumentList.Add(cutoff.ToString());

            using var proc = Process.Start(psi)!;
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            Assert.Equal("ready", await proc.StandardOutput.ReadLineAsync(cts.Token));
            await Task.Delay(rng.Next(0, 6));
            try
            {
                proc.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
                // The child already exited before the kill; the checks below still apply.
            }
            await proc.WaitForExitAsync(CancellationToken.None);

            for (var d = 0; d < devices; d++)
            {
                var device = $"dev-{d}";
                var recent = await log.ReadRangeAsync<Position>("tenant-a", device, cutoff, end, CancellationToken.None);
                Assert.Equal(24, recent.Count);
                var all = await log.ReadRangeAsync<Position>("tenant-a", device, t0, end, CancellationToken.None);
                Assert.InRange(all.Count, 24, 48);
            }
        }

        await log.DeleteBeforeAsync<Position>("tenant-a", cutoff, CancellationToken.None);

        for (var d = 0; d < devices; d++)
        {
            var device = $"dev-{d}";
            Assert.Empty(await log.ReadRangeAsync<Position>("tenant-a", device, t0, cutoff, CancellationToken.None));
            Assert.Equal(24, (await log.ReadRangeAsync<Position>("tenant-a", device, cutoff, end, CancellationToken.None)).Count);
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
