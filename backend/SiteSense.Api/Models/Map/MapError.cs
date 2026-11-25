namespace SiteSense.Api.Models.Map;

// ElementId is null for errors about the whole file (e.g. perimeter missing).
public sealed record MapError(string? ElementId, string Reason);
