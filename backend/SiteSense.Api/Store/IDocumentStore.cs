namespace SiteSense.Api.Store;

public interface IDocumentStore
{
    Task<T?> GetAsync<T>(string tenantId, string id, CancellationToken ct) where T : class, IDocument;
    // doc.Version must be the stored version (0 for a new document), else ConcurrencyException.
    // On success sets Version to the stored version + 1 (1 on create).
    Task<T> SaveAsync<T>(string tenantId, T doc, CancellationToken ct) where T : class, IDocument;
    // Every document of the tenant, in no fixed order; empty when none. Never returns a half-written document.
    // shortcut: full folder scan, no index and no paging (SS-11, postponed).
    Task<IReadOnlyList<T>> ListAsync<T>(string tenantId, CancellationToken ct) where T : class, IDocument;
    // False when missing; ConcurrencyException when expectedVersion is not the stored version.
    Task<bool> DeleteAsync<T>(string tenantId, string id, long expectedVersion, CancellationToken ct) where T : class, IDocument;
}
