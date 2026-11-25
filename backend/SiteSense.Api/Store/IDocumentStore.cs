namespace SiteSense.Api.Store;

public interface IDocumentStore
{
    Task<T?> GetAsync<T>(string tenantId, string id, CancellationToken ct) where T : class, IDocument;
    // doc.Version must be the stored version (0 for a new document), else ConcurrencyException.
    // On success sets Version to the stored version + 1 (1 on create).
    Task<T> SaveAsync<T>(string tenantId, T doc, CancellationToken ct) where T : class, IDocument;
    // False when missing; ConcurrencyException when expectedVersion is not the stored version.
    Task<bool> DeleteAsync<T>(string tenantId, string id, long expectedVersion, CancellationToken ct) where T : class, IDocument;
}
