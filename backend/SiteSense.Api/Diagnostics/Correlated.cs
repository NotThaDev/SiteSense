namespace SiteSense.Api.Diagnostics;

// Carries the correlation id with a payload out of the HTTP request (ingest queue items, realtime events).
public readonly record struct Correlated<T>(string CorrelationId, T Payload);
