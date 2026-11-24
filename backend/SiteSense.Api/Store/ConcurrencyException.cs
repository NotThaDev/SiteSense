namespace SiteSense.Api.Store;

// A save or delete was based on a version that is no longer the stored one (JDB-03).
public sealed class ConcurrencyException(string message) : Exception(message);
