namespace SiteSense.Api.Store;

public interface IAppendLog
{
    // Writes every record to the file of its UTC hour and fsyncs before returning.
    Task AppendAsync<T>(string tenantId, string key, IReadOnlyList<T> records, CancellationToken ct) where T : class, ILogRecord;
    // Records with fromMs <= TimestampMs < toMs, ordered by TimestampMs (append order on ties). Only newline-terminated lines count; lines that do not parse are skipped.
    Task<IReadOnlyList<T>> ReadRangeAsync<T>(string tenantId, string key, long fromMs, long toMs, CancellationToken ct) where T : class, ILogRecord;
}
