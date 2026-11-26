using System.Collections.Concurrent;
using System.Text.Json;

namespace SiteSense.Api.Store;

public sealed class FileDocumentStore : IDocumentStore
{
    // shortcut: one semaphore per document path is never removed; fine for thousands of documents, evict when counts grow
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();
    private readonly string _root;

    public FileDocumentStore(string rootPath)
    {
        _root = Path.GetFullPath(rootPath);
        Directory.CreateDirectory(_root);
        // Orphans of a crash mid-write; never read as documents.
        foreach (var tmp in Directory.GetFiles(_root, "*.tmp", SearchOption.AllDirectories))
            File.Delete(tmp);
    }

    public async Task<T?> GetAsync<T>(string tenantId, string id, CancellationToken ct) where T : class, IDocument
    {
        StorePaths.CheckName(tenantId, nameof(tenantId));
        StorePaths.CheckName(id, nameof(id));
        var path = PathOf<T>(tenantId, id);
        try
        {
            // Rename is atomic, so a reader sees the old or the new file, never a partial one.
            await using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 4096, FileOptions.Asynchronous);
            var doc = await JsonSerializer.DeserializeAsync<T>(fs, StorePaths.Json, ct);
            if (doc is null || doc.TenantId != tenantId)
                throw new InvalidOperationException($"Document file '{path}' is missing or belongs to another tenant.");
            return doc;
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return null;
        }
    }

    public async Task<IReadOnlyList<T>> ListAsync<T>(string tenantId, CancellationToken ct) where T : class, IDocument
    {
        StorePaths.CheckName(tenantId, nameof(tenantId));
        var dir = Path.GetDirectoryName(PathOf<T>(tenantId, "x"))!;
        if (!Directory.Exists(dir)) return [];
        var docs = new List<T>();
        // "*.json" skips the "<id>.json.tmp" files of in-flight writes; rename is atomic, so a complete file is read.
        foreach (var path in Directory.EnumerateFiles(dir, "*.json"))
        {
            try
            {
                await using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 4096, FileOptions.Asynchronous);
                var doc = await JsonSerializer.DeserializeAsync<T>(fs, StorePaths.Json, ct);
                if (doc is null || doc.TenantId != tenantId)
                    throw new InvalidOperationException($"Document file '{path}' is missing or belongs to another tenant.");
                docs.Add(doc);
            }
            catch (FileNotFoundException)
            {
                // Deleted between the scan and the read.
            }
        }
        return docs;
    }

    public async Task<T> SaveAsync<T>(string tenantId, T doc, CancellationToken ct) where T : class, IDocument
    {
        StorePaths.CheckName(tenantId, nameof(tenantId));
        StorePaths.CheckName(doc.Id, nameof(doc.Id));
        if (doc.TenantId != tenantId)
            throw new ArgumentException("Document TenantId does not match the tenant it is saved under.", nameof(doc));

        var path = PathOf<T>(tenantId, doc.Id);
        var gate = _locks.GetOrAdd(path, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            var existing = await GetAsync<T>(tenantId, doc.Id, ct);
            var stored = existing?.Version ?? 0;
            if (doc.Version != stored)
                throw new ConcurrencyException($"{typeof(T).Name} '{doc.Id}' is at version {stored}, not {doc.Version}.");
            var callerVersion = doc.Version;
            doc.Version = stored + 1;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                var tmp = path + ".tmp";
                await using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous))
                {
                    await JsonSerializer.SerializeAsync(fs, doc, StorePaths.Json, ct);
                    // shortcut: the directory is not fsynced after rename; survives a process kill, not a power loss
                    fs.Flush(flushToDisk: true);
                }
                File.Move(tmp, path, overwrite: true);
            }
            catch
            {
                doc.Version = callerVersion;
                throw;
            }
            return doc;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<bool> DeleteAsync<T>(string tenantId, string id, long expectedVersion, CancellationToken ct) where T : class, IDocument
    {
        StorePaths.CheckName(tenantId, nameof(tenantId));
        StorePaths.CheckName(id, nameof(id));
        var path = PathOf<T>(tenantId, id);
        var gate = _locks.GetOrAdd(path, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            var existing = await GetAsync<T>(tenantId, id, ct);
            if (existing is null) return false;
            if (existing.Version != expectedVersion)
                throw new ConcurrencyException($"{typeof(T).Name} '{id}' is at version {existing.Version}, not {expectedVersion}.");
            File.Delete(path);
            return true;
        }
        finally
        {
            gate.Release();
        }
    }

    // Same rule for callers that must reject a bad id before it reaches the store (e.g. tenant_id in a token).
    public static bool IsValidName(string value) => StorePaths.IsValidName(value);

    private string PathOf<T>(string tenantId, string id) =>
        Path.Combine(_root, StorePaths.FileName(tenantId), typeof(T).Name, StorePaths.FileName(id) + ".json");
}
