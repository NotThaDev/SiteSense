using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SiteSense.Api.Diagnostics;

namespace SiteSense.Tests.Api;

public class CorrelationTests
{
    [Fact]
    public async Task Request_WithCorrelationHeader_EchoesHeader()
    {
        var response = await SendAsync("req-42.a_b");

        Assert.Equal("req-42.a_b", ResponseId(response));
    }

    [Fact]
    public async Task Request_WithoutCorrelationHeader_ReturnsGeneratedId()
    {
        var response = await SendAsync(null);

        Assert.Matches("^[0-9a-f]{32}$", ResponseId(response));
    }

    [Fact]
    public async Task Request_WithTooLongCorrelationHeader_ReturnsNewId()
    {
        var response = await SendAsync(new string('a', 65));

        Assert.Matches("^[0-9a-f]{32}$", ResponseId(response));
    }

    [Fact]
    public async Task Middleware_DuringRequest_PutsIdInLogScope()
    {
        var capture = new ScopeCapture();
        using var services = new ServiceCollection().AddLogging(b => b.AddProvider(capture)).BuildServiceProvider();
        var context = new DefaultHttpContext { RequestServices = services };
        context.Request.Headers[Correlation.HeaderName] = "req-42";

        await Correlation.Middleware(context, c =>
        {
            c.RequestServices.GetRequiredService<ILogger<CorrelationTests>>().LogInformation("Inside the request");
            return Task.CompletedTask;
        });

        Assert.Equal(["req-42"], capture.Ids);
    }

    private static async Task<HttpResponseMessage> SendAsync(string? correlationId)
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/any-path");
        if (correlationId is not null)
        {
            request.Headers.TryAddWithoutValidation(Correlation.HeaderName, correlationId);
        }
        return await client.SendAsync(request);
    }

    private static string ResponseId(HttpResponseMessage response) =>
        Assert.Single(response.Headers.GetValues(Correlation.HeaderName));

    // Records the correlation id found in the scope of each log line.
    private sealed class ScopeCapture : ILoggerProvider, ISupportExternalScope, ILogger
    {
        private IExternalScopeProvider? _scopes;

        public List<object> Ids { get; } = [];

        public void SetScopeProvider(IExternalScopeProvider scopeProvider) => _scopes = scopeProvider;

        public ILogger CreateLogger(string categoryName) => this;

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => _scopes?.Push(state);

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            _scopes?.ForEachScope((scope, ids) =>
            {
                if (scope is IEnumerable<KeyValuePair<string, object>> pairs)
                {
                    ids.AddRange(pairs.Where(p => p.Key == Correlation.ScopeKey).Select(p => p.Value));
                }
            }, Ids);

        public void Dispose()
        {
        }
    }
}
