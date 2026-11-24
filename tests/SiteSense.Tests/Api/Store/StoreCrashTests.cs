using System.Diagnostics;
using System.Text.Json;
using SiteSense.Api.Store;
using SiteSense.StoreCrashWriter;

namespace SiteSense.Tests.Api.Store;

public sealed class StoreCrashTests : IDisposable
{
    private const int Seed = 42;
    private const int Runs = 10;
    private const int PayloadLength = 256 * 1024;

    private readonly string _root = Path.Combine(Path.GetTempPath(), "sitesense-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Save_KilledMidWrite_LeavesNoCorruptDocument()
    {
        var rng = new Random(Seed);
        var dotnet = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet";

        for (var run = 1; run <= Runs; run++)
        {
            var killAfter = rng.Next(1, 60);
            var psi = new ProcessStartInfo(dotnet) { RedirectStandardOutput = true, UseShellExecute = false };
            psi.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "SiteSense.StoreCrashWriter.dll"));
            psi.ArgumentList.Add(_root);

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
                    Assert.Fail($"Crash writer timed out. Seed {Seed}, run {run}, save {n}.");
                    return;
                }
                if (line is null)
                {
                    Assert.Fail($"Crash writer exited early. Seed {Seed}, run {run}, save {n}.");
                    return;
                }
            }
            proc.Kill(entireProcessTree: true);
            await proc.WaitForExitAsync(CancellationToken.None);
        }

        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var files = Directory.GetFiles(_root, "*.json", SearchOption.AllDirectories);
        Assert.NotEmpty(files);
        foreach (var file in files)
        {
            var doc = JsonSerializer.Deserialize<CrashDoc>(await File.ReadAllTextAsync(file, CancellationToken.None), options);
            Assert.NotNull(doc);
            Assert.Equal(doc.Counter, doc.Version);
            Assert.Equal(PayloadLength, doc.Payload.Length);
        }

        var store = new FileDocumentStore(_root);
        Assert.Empty(Directory.GetFiles(_root, "*.tmp", SearchOption.AllDirectories));
        for (var i = 0; i < 5; i++)
        {
            var path = Path.Combine(_root, "tenant-a", nameof(CrashDoc), $"doc-{i}.json");
            if (!File.Exists(path)) continue;
            var doc = await store.GetAsync<CrashDoc>("tenant-a", $"doc-{i}", CancellationToken.None);
            Assert.NotNull(doc);
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
