namespace SiteSense.Api.Diagnostics;

// Correlation id (NF-03): read from or added to X-Correlation-Id, and put in the log scope of the request.
public static class Correlation
{
    public const string HeaderName = "X-Correlation-Id";
    public const string ScopeKey = "CorrelationId";
    private const int MaxLength = 64;

    public static string New() => Guid.NewGuid().ToString("N");

    // Reused outside HTTP (ingest worker, SignalR publisher) with the id carried by Correlated<T>.
    public static IDisposable? BeginScope(ILogger logger, string correlationId) =>
        logger.BeginScope(new Dictionary<string, object> { [ScopeKey] = correlationId });

    public static async Task Middleware(HttpContext context, RequestDelegate next)
    {
        string? incoming = context.Request.Headers[HeaderName];
        var id = IsValid(incoming) ? incoming! : New();
        context.Response.Headers[HeaderName] = id;

        // Scopes are shared by every logger of the factory, so all lines of the request get the id.
        var logger = context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(Correlation));
        using (BeginScope(logger, id))
        {
            await next(context);
        }
    }

    // Trust boundary: a client value goes into every log line, so only short, plain ids are accepted.
    private static bool IsValid(string? value) =>
        value is { Length: > 0 and <= MaxLength }
        && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.');
}
