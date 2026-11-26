using System.Diagnostics;
using System.Text.Json;
using SiteSense.Api.Models;
using SiteSense.Api.Store;
using Xunit.Abstractions;

namespace SiteSense.Tests.Api.Store;

public sealed class FileAppendLogTests(ITestOutputHelper output) : IDisposable
{
    private const long T0 = 1793268000000; // aligned to a UTC hour boundary
    private const long Hour = 3_600_000;
    private const string Tenant = "tenant-a";
    private const string Key = "dev-1";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "sitesense-tests", Guid.NewGuid().ToString("N"));

    private static Position P(long ts, double lat = 45.5, string device = Key) => new(device, ts, lat, 9.2, 4.5);

    private Task Append(IReadOnlyList<Position> batch, string tenant = Tenant, string key = Key, FileAppendLog? log = null) =>
        (log ?? new FileAppendLog(_root)).AppendAsync(tenant, key, batch, CancellationToken.None);

    private Task<IReadOnlyList<Position>> Read(long from, long to, string tenant = Tenant, string key = Key, FileAppendLog? log = null) =>
        (log ?? new FileAppendLog(_root)).ReadRangeAsync<Position>(tenant, key, from, to, CancellationToken.None);

    private string[] Files() => Directory.GetFiles(_root, "*.jsonl", SearchOption.AllDirectories);

    [Fact]
    public async Task Append_ThenReadRange_ReturnsRecordsSortedByTimestamp()
    {
        await Append([P(T0 + 3000), P(T0 + 1000), P(T0 + 2000)]);

        var read = await Read(T0, T0 + Hour);

        Assert.Equal([T0 + 1000, T0 + 2000, T0 + 3000], read.Select(r => r.TimestampMs));
    }

    [Fact]
    public async Task ReadRange_ExcludesRecordsOutsideRange()
    {
        await Append([P(T0 + 999), P(T0 + 1000), P(T0 + 1999), P(T0 + 2000)]);

        var read = await Read(T0 + 1000, T0 + 2000);

        Assert.Equal([T0 + 1000, T0 + 1999], read.Select(r => r.TimestampMs));
    }

    [Fact]
    public async Task Append_RecordsInTwoUtcHours_WritesTwoFilesAndReadsAcrossTheHour()
    {
        await Append([P(T0 + Hour - 1), P(T0 + Hour)]);

        var read = await Read(T0, T0 + 2 * Hour);

        Assert.Equal(2, Files().Length);
        Assert.Equal([T0 + Hour - 1, T0 + Hour], read.Select(r => r.TimestampMs));
    }

    [Fact]
    public async Task ReadRange_TornLastLine_SkipsItAndReturnsEarlierRecords()
    {
        await Append([P(T0 + 1000), P(T0 + 2000), P(T0 + 3000)]);
        Tear(Assert.Single(Files()));

        var read = await Read(T0, T0 + Hour);

        Assert.Equal([T0 + 1000, T0 + 2000], read.Select(r => r.TimestampMs));
    }

    [Fact]
    public async Task ReadRange_UnterminatedLastLineThatParses_IsIgnored()
    {
        await Append([P(T0 + 1000)]);
        var json = JsonSerializer.Serialize(P(T0 + 2000), new JsonSerializerOptions(JsonSerializerDefaults.Web));
        await File.AppendAllTextAsync(Assert.Single(Files()), json);

        var read = await Read(T0, T0 + Hour);

        Assert.Equal([T0 + 1000], read.Select(r => r.TimestampMs));
    }

    [Fact]
    public async Task Append_AfterTornLastLine_KeepsOldAndNewRecords()
    {
        await Append([P(T0 + 1000), P(T0 + 2000), P(T0 + 3000)]);
        Tear(Assert.Single(Files()));

        await Append([P(T0 + 4000)]);
        var read = await Read(T0, T0 + Hour);

        Assert.Equal([T0 + 1000, T0 + 2000, T0 + 4000], read.Select(r => r.TimestampMs));
    }

    [Fact]
    public async Task ReadRange_OtherTenant_ReturnsNothing()
    {
        await Append([P(T0 + 1000)]);

        var read = await Read(T0, T0 + Hour, tenant: "tenant-b");

        Assert.Empty(read);
    }

    [Fact]
    public async Task ReadRange_KeysDifferOnlyByCase_AreSeparate()
    {
        await Append([P(T0 + 1000, device: "dev-1")], key: "dev-1");
        await Append([P(T0 + 2000, device: "DEV-1")], key: "DEV-1");

        var lower = await Read(T0, T0 + Hour, key: "dev-1");
        var upper = await Read(T0, T0 + Hour, key: "DEV-1");

        Assert.Equal([T0 + 1000], lower.Select(r => r.TimestampMs));
        Assert.Equal([T0 + 2000], upper.Select(r => r.TimestampMs));
    }

    [Theory]
    [InlineData("../x", "dev-1")]
    [InlineData("tenant-a", "../x")]
    [InlineData("tenant-a", "")]
    public async Task Append_InvalidTenantOrKey_Throws(string tenant, string key)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Append([P(T0)], tenant, key));
    }

    [Fact]
    public async Task Append_EmptyBatch_WritesNothing()
    {
        await Append([]);

        Assert.False(Directory.Exists(Path.Combine(_root, Tenant, nameof(Position))));
    }

    [Fact]
    public void Append_DeviceIdNotKey_Throws()
    {
        Assert.Throws<ArgumentException>(() => { _ = Position.AppendAsync(Tenant, Key, [P(T0, device: "other")], CancellationToken.None); });
    }

    [Fact]
    public async Task Append_TimestampOutOfRange_Throws()
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => Append([P(T0), P(long.MaxValue)]));

        Assert.Empty(Files());
    }

    [Fact]
    public async Task ReadRange_FromNotBeforeTo_ReturnsEmpty()
    {
        await Append([P(T0 + 1000)]);

        var read = await Read(T0 + 1000, T0 + 1000);

        Assert.Empty(read);
    }

    [Fact]
    public async Task ReadRange_NoFile_ReturnsEmpty()
    {
        var read = await Read(T0, T0 + Hour);

        Assert.Empty(read);
    }

    [Fact]
    public async Task ReadRange_EqualTimestamps_KeepsAppendOrder()
    {
        await Append([P(T0 + 1000, lat: 1), P(T0 + 1000, lat: 2)]);
        await Append([P(T0 + 1000, lat: 3), P(T0 + 1000, lat: 4)]);

        var read = await Read(T0, T0 + Hour);

        Assert.Equal([1.0, 2.0, 3.0, 4.0], read.Select(r => r.Lat));
    }

    [Fact]
    public async Task Position_RoundTrip_KeepsAllFields()
    {
        var p = new Position(Key, T0 + 1234, 45.123456, 9.654321, 3.75);

        await Append([p]);
        var read = await Read(T0, T0 + Hour);
        var raw = await File.ReadAllTextAsync(Assert.Single(Files()));

        Assert.Equal(p, Assert.Single(read));
        Assert.Contains("\"deviceId\"", raw);
        Assert.Contains("\"timestampMs\"", raw);
        Assert.Contains("\"accuracyM\"", raw);
    }

    [Theory]
    [InlineData(20, 3000)]
    [InlineData(600, 100)]
    public async Task Append_SixtyThousandRecords_AllReadBack(int batches, int batchSize)
    {
        var log = new FileAppendLog(_root);
        for (var b = 0; b < batches; b++)
            await Append(Enumerable.Range(b * batchSize, batchSize).Select(i => P(T0 + i * 10L)).ToList(), log: log);

        var read = await Read(T0, T0 + Hour, log: log);

        Assert.Equal(60_000, read.Count);
        Assert.Equal(Enumerable.Range(0, 60_000).Select(i => T0 + i * 10L), read.Select(r => r.TimestampMs));
    }

    [Fact]
    public async Task ReadRange_OneHourAtOnePerSecond_PrintsElapsed()
    {
        var log = new FileAppendLog(_root);
        await Append(Enumerable.Range(0, 7200).Select(i => P(T0 + i * 1000L)).ToList(), log: log);

        var sw = Stopwatch.StartNew();
        var read = await Read(T0 + Hour / 2, T0 + Hour / 2 + Hour, log: log);
        sw.Stop();

        output.WriteLine($"ReadRange one hour (3,600 of 7,200 records): {sw.ElapsedMilliseconds} ms");
        Assert.Equal(3600, read.Count);
    }

    [Fact]
    public async Task Append_ParallelWritersOnOneFile_EveryLineIntact()
    {
        const int writers = 8, batches = 200, perBatch = 5;
        var log = new FileAppendLog(_root);
        var done = false;

        var reader = Task.Run(async () =>
        {
            do
            {
                foreach (var r in await Read(T0, T0 + Hour, log: log))
                {
                    Assert.Equal(Key, r.DeviceId);
                    Assert.Equal(4.5, r.AccuracyM);
                }
            } while (!Volatile.Read(ref done));
        });
        var tasks = Enumerable.Range(0, writers).Select(w => Task.Run(async () =>
        {
            for (var b = 0; b < batches; b++)
                await Append(Enumerable.Range(0, perBatch).Select(i => P(T0 + ((w * batches + b) * perBatch + i))).ToList(), log: log);
        })).ToList();

        await Task.WhenAll(tasks);
        Volatile.Write(ref done, true);
        await reader;
        var read = await Read(T0, T0 + Hour, log: log);

        Assert.Equal(writers * batches * perBatch, read.Count);
        Assert.Equal(Enumerable.Range(0, 8000).Select(i => T0 + i), read.Select(r => r.TimestampMs));
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var lines = await File.ReadAllLinesAsync(Assert.Single(Files()));
        Assert.Equal(8000, lines.Length);
        Assert.All(lines, l => Assert.NotNull(JsonSerializer.Deserialize<Position>(l, options)));
    }

    [Fact]
    public async Task AppendNewAsync_SameBatchTwice_StoresOnce()
    {
        var log = new FileAppendLog(_root);
        Position[] batch = [P(T0 + 1000), P(T0 + 2000), P(T0 + 3000)];

        var first = await log.AppendNewAsync(Tenant, Key, batch, CancellationToken.None);
        var second = await log.AppendNewAsync(Tenant, Key, batch, CancellationToken.None);

        var read = await Read(T0, T0 + Hour, log: log);
        Assert.Equal(3, first);
        Assert.Equal(0, second);
        Assert.Equal(3, read.Count);
    }

    [Fact]
    public async Task AppendNewAsync_RepeatedTimestampInBatch_KeepsFirst()
    {
        Position[] batch = [P(T0 + 1000, lat: 45.1), P(T0 + 1000, lat: 45.2)];

        var written = await new FileAppendLog(_root).AppendNewAsync(Tenant, Key, batch, CancellationToken.None);

        var read = await Read(T0, T0 + Hour);
        Assert.Equal(1, written);
        Assert.Equal(45.1, Assert.Single(read).Lat);
    }

    [Fact]
    public async Task AppendNewAsync_BatchAcrossHours_SkipsOnlyStored()
    {
        await Append([P(T0 + 1000)]);
        Position[] batch = [P(T0 + 1000), P(T0 + Hour + 1000)];

        var written = await new FileAppendLog(_root).AppendNewAsync(Tenant, Key, batch, CancellationToken.None);

        var read = await Read(T0, T0 + 2 * Hour);
        Assert.Equal(1, written);
        Assert.Equal(2, read.Count);
    }

    [Fact]
    public async Task AppendNewAsync_HourBoundary_SplitsFiles()
    {
        Position[] batch = [P(T0 + Hour - 1), P(T0 + Hour)];

        var written = await new FileAppendLog(_root).AppendNewAsync(Tenant, Key, batch, CancellationToken.None);

        Assert.Equal(2, Files().Length);
        Assert.Equal(2, written);
    }

    [Fact]
    public async Task AppendNewAsync_AfterTornLine_RewritesTornRecord()
    {
        var log = new FileAppendLog(_root);
        Position[] batch = [P(T0 + 1000), P(T0 + 2000)];
        await log.AppendNewAsync(Tenant, Key, batch, CancellationToken.None);
        CutTail(Assert.Single(Files()), 10);

        var written = await log.AppendNewAsync(Tenant, Key, batch, CancellationToken.None);

        var read = await Read(T0, T0 + Hour, log: log);
        Assert.Equal(1, written);
        Assert.Equal([T0 + 1000, T0 + 2000], read.Select(r => r.TimestampMs));
    }

    [Fact]
    public async Task AppendNewAsync_LastLineWithoutNewline_CountsAsStoredAndEndsIt()
    {
        var log = new FileAppendLog(_root);
        Position[] batch = [P(T0 + 1000), P(T0 + 2000)];
        await log.AppendNewAsync(Tenant, Key, batch, CancellationToken.None);
        CutTail(Assert.Single(Files()), 1);

        var written = await log.AppendNewAsync(Tenant, Key, batch, CancellationToken.None);

        var read = await Read(T0, T0 + Hour, log: log);
        Assert.Equal(0, written);
        Assert.Equal([T0 + 1000, T0 + 2000], read.Select(r => r.TimestampMs));
    }

    [Fact]
    public async Task AppendNewAsync_ConcurrentIdenticalBatches_StoresOnce()
    {
        var log = new FileAppendLog(_root);
        var batch = Enumerable.Range(0, 100).Select(i => P(T0 + i)).ToList();

        var results = await Task.WhenAll(Enumerable.Range(0, 8)
            .Select(_ => Task.Run(() => log.AppendNewAsync(Tenant, Key, batch, CancellationToken.None))));

        var read = await Read(T0, T0 + Hour, log: log);
        Assert.Equal(100, results.Sum());
        Assert.Equal(100, read.Count);
        Assert.Equal(100, read.Select(r => r.TimestampMs).Distinct().Count());
    }

    // Removes the last n bytes of the file.
    private static void CutTail(string path, int n)
    {
        var all = File.ReadAllBytes(path);
        File.WriteAllBytes(path, all[..^n]);
    }

    // Cuts the last line of the file in half.
    private static void Tear(string path)
    {
        var text = File.ReadAllText(path);
        var lastStart = text.TrimEnd('\n').LastIndexOf('\n') + 1;
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Write);
        fs.SetLength(lastStart + (text.Length - lastStart) / 2);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
