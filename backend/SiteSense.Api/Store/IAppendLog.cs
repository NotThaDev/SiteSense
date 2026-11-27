namespace SiteSense.Api.Store;

public interface IAppendLog
{
    // Writes every record to the file of its UTC hour and fsyncs before returning.
    Task AppendAsync<T>(string tenantId, string key, IReadOnlyList<T> records, CancellationToken ct) where T : class, ILogRecord;
    // Like AppendAsync, but skips records whose TimestampMs is already stored under this key, or repeated earlier in the batch (first wins).
    // Check and write run under the key's lock, so concurrent retries write each timestamp once. Returns how many records were written.
    Task<int> AppendNewAsync<T>(string tenantId, string key, IReadOnlyList<T> records, CancellationToken ct) where T : class, ILogRecord;
    // Records with fromMs <= TimestampMs < toMs, ordered by TimestampMs (append order on ties). Only newline-terminated lines count; lines that do not parse are skipped.
    Task<IReadOnlyList<T>> ReadRangeAsync<T>(string tenantId, string key, long fromMs, long toMs, CancellationToken ct) where T : class, ILogRecord;
    // The record stored last (newest hour file, last line), or null when the key has none. Same line rules as ReadRangeAsync.
    Task<T?> ReadLastAsync<T>(string tenantId, string key, CancellationToken ct) where T : class, ILogRecord;
}
