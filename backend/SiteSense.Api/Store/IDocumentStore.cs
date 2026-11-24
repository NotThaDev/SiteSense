namespace SiteSense.Api.Store;

public interface IDocumentStore
{
    Task<T?> GetAsync<T>(string tenantId, string id, CancellationToken ct) where T : class, IDocument;
    // Creates or replaces the document and sets Version to the stored version + 1 (1 on create).
    Task<T> SaveAsync<T>(string tenantId, T doc, CancellationToken ct) where T : class, IDocument;
    Task<bool> DeleteAsync<T>(string tenantId, string id, CancellationToken ct) where T : class, IDocument;
}
