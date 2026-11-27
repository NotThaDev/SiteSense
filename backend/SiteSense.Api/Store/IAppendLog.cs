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
    // Deletes, for every key of T in the tenant, each hour file whose hour ends at or before beforeMs (retention, PRV-04).
    // One File.Delete per file under the key's lock, so a file is whole or gone. Returns how many files were deleted.
    Task<int> DeleteBeforeAsync<T>(string tenantId, long beforeMs, CancellationToken ct) where T : class, ILogRecord;
}
