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

    public Task AppendAsync<T>(string tenantId, string key, IReadOnlyList<T> records, CancellationToken ct) where T : class, ILogRecord =>
        AppendCoreAsync(tenantId, key, records, skipStored: false, ct);

    public Task<int> AppendNewAsync<T>(string tenantId, string key, IReadOnlyList<T> records, CancellationToken ct) where T : class, ILogRecord =>
        AppendCoreAsync(tenantId, key, records, skipStored: true, ct);

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

    // shortcut: reads the whole newest file; fine for bounded files, add a tail read when files grow
    public Task<T?> ReadLastAsync<T>(string tenantId, string key, CancellationToken ct) where T : class, ILogRecord
    {
        StorePaths.CheckName(tenantId, nameof(tenantId));
        StorePaths.CheckName(key, nameof(key));
        var dir = DirOf<T>(tenantId, key);
        if (!Directory.Exists(dir)) return Task.FromResult<T?>(null);
        foreach (var path in Directory.EnumerateFiles(dir, "*.jsonl").OrderByDescending(p => p, StringComparer.Ordinal))
        {
            ct.ThrowIfCancellationRequested();
            if (!DateTime.TryParseExact(Path.GetFileNameWithoutExtension(path), HourFormat, CultureInfo.InvariantCulture,
                    DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out _))
                continue;
            if (ReadLastRecord<T>(path) is { } last) return Task.FromResult<T?>(last);
        }
        return Task.FromResult<T?>(null);
    }

    private async Task<int> AppendCoreAsync<T>(string tenantId, string key, IReadOnlyList<T> records, bool skipStored, CancellationToken ct) where T : class, ILogRecord
    {
        StorePaths.CheckName(tenantId, nameof(tenantId));
        StorePaths.CheckName(key, nameof(key));
        if (records.Count == 0) return 0;

        // Throws for an out-of-range timestamp before anything is written.
        var byHour = records
            .Select(r => (Hour: DateTimeOffset.FromUnixTimeMilliseconds(r.TimestampMs).UtcDateTime.ToString(HourFormat, CultureInfo.InvariantCulture), Record: r))
            .GroupBy(x => x.Hour)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .ToList();

        var dir = DirOf<T>(tenantId, key);
        var gate = _locks.GetOrAdd(dir, _ => new SemaphoreSlim(1, 1));
        var written = 0;
        await gate.WaitAsync(ct);
        try
        {
            foreach (var group in byHour)
            {
                ct.ThrowIfCancellationRequested();
                // shortcut: the directory is not fsynced after a new file is created; survives kill -9, not power loss
                Directory.CreateDirectory(dir);
                using var fs = new FileStream(Path.Combine(dir, group.Key + ".jsonl"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete);
                var batch = group.Select(x => x.Record).ToList();
                if (skipStored)
                {
                    var stored = new HashSet<long>();
                    var unterminated = false;
                    if (fs.Length > 0)
                    {
                        var existing = new byte[fs.Length];
                        fs.Seek(0, SeekOrigin.Begin);
                        fs.ReadExactly(existing);
                        ParseLines<T>(existing, r => stored.Add(r.TimestampMs));
                        // A whole record cut before its newline becomes a record once the newline below ends it, so it counts as stored.
                        var tail = existing.AsSpan(Array.LastIndexOf(existing, (byte)'\n') + 1);
                        unterminated = !tail.IsEmpty;
                        if (unterminated && TryParse<T>(tail) is { } last) stored.Add(last.TimestampMs);
                    }
                    // Also drops repeats inside this batch: the first one wins.
                    batch = batch.Where(r => stored.Add(r.TimestampMs)).ToList();
                    // An unterminated tail still gets its newline, or a whole record there would stay unreadable.
                    if (batch.Count == 0 && !unterminated) continue;
                }
                var buffer = new List<byte>();
                if (fs.Length > 0)
                {
                    fs.Seek(-1, SeekOrigin.End);
                    // A crash can leave a torn last line; end it so the next record starts on its own line.
                    if (fs.ReadByte() != '\n') buffer.Add((byte)'\n');
                }
                foreach (var record in batch)
                {
                    buffer.AddRange(JsonSerializer.SerializeToUtf8Bytes(record, StorePaths.Json));
                    buffer.Add((byte)'\n');
                }
                fs.Seek(0, SeekOrigin.End);
                fs.Write(buffer.ToArray());
                fs.Flush(flushToDisk: true);
                written += batch.Count;
            }
        }
        finally
        {
            gate.Release();
        }
        return written;
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
        ParseLines<T>(bytes, r =>
        {
            if (r.TimestampMs >= fromMs && r.TimestampMs < toMs) result.Add(r);
        });
    }

    // Null for a missing file, an empty file or one with no record.
    private static T? ReadLastRecord<T>(string path) where T : class, ILogRecord
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
            return null;
        }
        var tail = bytes.AsSpan(Array.LastIndexOf(bytes, (byte)'\n') + 1);
        // A whole record cut before its newline counts, because the next append ends it with '\n'.
        if (!tail.IsEmpty && TryParse<T>(tail) is { } torn) return torn;
        T? last = null;
        ParseLines<T>(bytes, r => last = r);
        return last;
    }

    private static void ParseLines<T>(byte[] bytes, Action<T> onRecord) where T : class, ILogRecord
    {
        // Anything after the last newline is torn or still being written.
        var end = Array.LastIndexOf(bytes, (byte)'\n');
        var start = 0;
        while (start <= end)
        {
            var nl = Array.IndexOf(bytes, (byte)'\n', start, end - start + 1);
            var line = bytes.AsSpan(start, nl - start);
            start = nl + 1;
            if (!line.IsEmpty && TryParse<T>(line) is { } record) onRecord(record);
        }
    }

    // Null for a line that does not parse.
    private static T? TryParse<T>(ReadOnlySpan<byte> line) where T : class
    {
        try
        {
            return JsonSerializer.Deserialize<T>(line, StorePaths.Json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private string DirOf<T>(string tenantId, string key) =>
        Path.Combine(_root, StorePaths.FileName(tenantId), typeof(T).Name, StorePaths.FileName(key));
}
