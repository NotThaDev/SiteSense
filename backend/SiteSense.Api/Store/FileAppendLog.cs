using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;

namespace SiteSense.Api.Store;

public sealed class FileAppendLog : IAppendLog
{
    private const string HourFormat = "yyyy-MM-dd'T'HH";
    private const long HourMs = 3_600_000;

    // shortcut: one semaphore per device log is never removed; fine for thousands of devices, evict when counts grow
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();
    private readonly string _root;

    public FileAppendLog(string rootPath)
    {
        _root = Path.GetFullPath(rootPath);
        Directory.CreateDirectory(_root);
    }

    public async Task AppendAsync<T>(string tenantId, string key, IReadOnlyList<T> records, CancellationToken ct) where T : class, ILogRecord
    {
        StorePaths.CheckName(tenantId, nameof(tenantId));
        StorePaths.CheckName(key, nameof(key));
        if (records.Count == 0) return;

        // Throws for an out-of-range timestamp before anything is written.
        var byHour = records
            .Select(r => (Hour: DateTimeOffset.FromUnixTimeMilliseconds(r.TimestampMs).UtcDateTime.ToString(HourFormat, CultureInfo.InvariantCulture), Record: r))
            .GroupBy(x => x.Hour)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .ToList();

        var dir = DirOf<T>(tenantId, key);
        var gate = _locks.GetOrAdd(dir, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            foreach (var group in byHour)
            {
                ct.ThrowIfCancellationRequested();
                // shortcut: the directory is not fsynced after a new file is created; survives kill -9, not power loss
                Directory.CreateDirectory(dir);
                using var fs = new FileStream(Path.Combine(dir, group.Key + ".jsonl"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete);
                var buffer = new List<byte>();
                if (fs.Length > 0)
                {
                    fs.Seek(-1, SeekOrigin.End);
                    // A crash can leave a torn last line; end it so the next record starts on its own line.
                    if (fs.ReadByte() != '\n') buffer.Add((byte)'\n');
                }
                foreach (var (_, record) in group)
                {
                    buffer.AddRange(JsonSerializer.SerializeToUtf8Bytes(record, StorePaths.Json));
                    buffer.Add((byte)'\n');
                }
                fs.Seek(0, SeekOrigin.End);
                fs.Write(buffer.ToArray());
                fs.Flush(flushToDisk: true);
            }
        }
        finally
        {
            gate.Release();
        }
    }

    public Task<IReadOnlyList<T>> ReadRangeAsync<T>(string tenantId, string key, long fromMs, long toMs, CancellationToken ct) where T : class, ILogRecord
    {
        StorePaths.CheckName(tenantId, nameof(tenantId));
        StorePaths.CheckName(key, nameof(key));
        var result = new List<T>();
        var dir = DirOf<T>(tenantId, key);
        if (fromMs < toMs && Directory.Exists(dir))
        {
            foreach (var path in Directory.EnumerateFiles(dir, "*.jsonl").OrderBy(p => p, StringComparer.Ordinal))
            {
                ct.ThrowIfCancellationRequested();
                if (!DateTime.TryParseExact(Path.GetFileNameWithoutExtension(path), HourFormat, CultureInfo.InvariantCulture,
                        DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var hour))
                    continue;
                var startMs = new DateTimeOffset(hour, TimeSpan.Zero).ToUnixTimeMilliseconds();
                if (startMs >= toMs || startMs + HourMs <= fromMs) continue;
                ReadFile(path, fromMs, toMs, result);
            }
        }
        // OrderBy is stable, so ties keep append order.
        return Task.FromResult<IReadOnlyList<T>>(result.OrderBy(r => r.TimestampMs).ToList());
    }

    private static void ReadFile<T>(string path, long fromMs, long toMs, List<T> result) where T : class, ILogRecord
    {
        byte[] bytes;
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            bytes = new byte[fs.Length];
            fs.ReadExactly(bytes);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return;
        }
        // Anything after the last newline is torn or still being written.
        var end = Array.LastIndexOf(bytes, (byte)'\n');
        var start = 0;
        while (start <= end)
        {
            var nl = Array.IndexOf(bytes, (byte)'\n', start, end - start + 1);
            var line = bytes.AsSpan(start, nl - start);
            start = nl + 1;
            if (line.IsEmpty) continue;
            try
            {
                var record = JsonSerializer.Deserialize<T>(line, StorePaths.Json);
                if (record is not null && record.TimestampMs >= fromMs && record.TimestampMs < toMs)
                    result.Add(record);
            }
            catch (JsonException)
            {
            }
        }
    }

    private string DirOf<T>(string tenantId, string key) =>
        Path.Combine(_root, StorePaths.FileName(tenantId), typeof(T).Name, StorePaths.FileName(key));
}
