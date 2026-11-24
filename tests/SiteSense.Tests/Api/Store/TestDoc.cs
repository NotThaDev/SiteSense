using SiteSense.Api.Store;

namespace SiteSense.Tests.Api.Store;

public sealed class TestDoc : IDocument
{
    public required string Id { get; init; }
    public required string TenantId { get; init; }
    public long Version { get; set; }
    public string Name { get; init; } = "";
}
