using SiteSense.Api.Store;

namespace SiteSense.Api.Helpers;

// Set once in Program.cs; tests replace both (Q67).
public static class SiteSenseContext
{
    public static IDocumentStore Store { get; set; } = null!;
    public static TimeProvider Time { get; set; } = TimeProvider.System;
}
