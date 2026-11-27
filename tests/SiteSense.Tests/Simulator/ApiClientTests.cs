using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SiteSense.Simulator;

namespace SiteSense.Tests.Simulator;

public sealed class ApiClientTests
{
    private const string TokenJson = "{\"access_token\":\"t1\",\"expires_in\":300}";
    private const string SiteJson = "{\"siteId\":\"site-1\",\"name\":\"Test\"}";

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses;

        public RecordingHandler(params HttpResponseMessage[] responses) => _responses = new Queue<HttpResponseMessage>(responses);

        public List<(HttpMethod Method, string Path, string Body)> Requests { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(ct);
            Requests.Add((request.Method, request.RequestUri!.AbsolutePath, body));
            return _responses.Dequeue();
        }
    }

    private static HttpResponseMessage Token() =>
        new(HttpStatusCode.OK) { Content = new StringContent(TokenJson, Encoding.UTF8, "application/json") };

    private static HttpResponseMessage Status(HttpStatusCode code) => new(code);

    // The 202 body the API returns for a one-position batch.
    private static HttpResponseMessage Accepted(int stored = 1) => new(HttpStatusCode.Accepted)
    {
        Content = new StringContent($"{{\"received\":1,\"stored\":{stored},\"duplicates\":0,\"outsidePerimeter\":0}}"),
    };

    private static ApiClient Client(RecordingHandler handler) =>
        new(new HttpClient(handler), new SimulatorOptions(), TimeProvider.System);

    private static readonly Fix OneFix = new(1000, 37.52, 15.07, 4.5);

    [Fact]
    public void BatchJson_OneFix_UsesContractFieldNames()
    {
        using var doc = JsonDocument.Parse(ApiClient.BatchJson("sim-001", new[] { OneFix }));
        var root = doc.RootElement;
        var position = root.GetProperty("positions")[0];

        Assert.Equal(new[] { "deviceId", "positions" }, root.EnumerateObject().Select(p => p.Name).Order().ToArray());
        Assert.Equal(new[] { "accuracyM", "lat", "lon", "timestampMs" }, position.EnumerateObject().Select(p => p.Name).Order().ToArray());
        Assert.Equal(1000, position.GetProperty("timestampMs").GetInt64());
    }

    [Fact]
    public async Task SendBatchAsync_401ThenAccepted_RefreshesTokenOnce()
    {
        var handler = new RecordingHandler(Token(), Status(HttpStatusCode.Unauthorized), Token(), Accepted());

        var result = await Client(handler).SendBatchAsync("sim-001", new[] { OneFix }, CancellationToken.None);

        Assert.Equal(SendResult.Accepted, result);
        Assert.Equal(2, handler.Requests.Count(r => r.Path.EndsWith("/token")));
    }

    [Fact]
    public async Task SendBatchAsync_ServerError_Retry()
    {
        var handler = new RecordingHandler(Token(), Status(HttpStatusCode.InternalServerError));

        var result = await Client(handler).SendBatchAsync("sim-001", new[] { OneFix }, CancellationToken.None);

        Assert.Equal(SendResult.Retry, result);
    }

    [Fact]
    public async Task SendBatchAsync_BadRequest_Dropped()
    {
        var handler = new RecordingHandler(Token(), Status(HttpStatusCode.BadRequest));

        var result = await Client(handler).SendBatchAsync("sim-001", new[] { OneFix }, CancellationToken.None);

        Assert.Equal(SendResult.Dropped, result);
    }

    [Fact]
    public async Task SendBatchAsync_SecondCall_ReusesToken()
    {
        var handler = new RecordingHandler(Token(), Accepted(), Accepted());
        var client = Client(handler);

        await client.SendBatchAsync("sim-001", new[] { OneFix }, CancellationToken.None);
        await client.SendBatchAsync("sim-001", new[] { OneFix }, CancellationToken.None);

        Assert.Equal(1, handler.Requests.Count(r => r.Path.EndsWith("/token")));
    }

    [Fact]
    public async Task SetupAsync_SiteExists_SkipsMap()
    {
        var handler = new RecordingHandler(Token(), Status(HttpStatusCode.Conflict), Status(HttpStatusCode.Created));

        await Client(handler).SetupAsync(SiteJson, "{}", new[] { "sim-001" }, CancellationToken.None);

        Assert.DoesNotContain(handler.Requests, r => r.Method == HttpMethod.Put);
    }

    [Fact]
    public async Task SendBatchAsync_AcceptedButNotStored_StillAccepted()
    {
        var handler = new RecordingHandler(Token(), Accepted(stored: 0));

        var result = await Client(handler).SendBatchAsync("sim-001", new[] { OneFix }, CancellationToken.None);

        Assert.Equal(SendResult.Accepted, result);
    }
}
