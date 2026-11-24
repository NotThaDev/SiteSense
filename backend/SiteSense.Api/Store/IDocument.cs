namespace SiteSense.Api.Store;

public interface IDocument
{
    string Id { get; }
    string TenantId { get; }
    // Set by the store on every save.
    long Version { get; set; }
}
