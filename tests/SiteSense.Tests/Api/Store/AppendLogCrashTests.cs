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

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
