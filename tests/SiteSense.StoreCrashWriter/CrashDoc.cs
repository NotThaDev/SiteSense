using SiteSense.Api.Store;

namespace SiteSense.StoreCrashWriter;

public sealed class CrashDoc : IDocument
{
    public required string Id { get; init; }
    public required string TenantId { get; init; }
    public long Version { get; set; }
    public long Counter { get; init; }
    public string Payload { get; init; } = "";
}
