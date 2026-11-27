using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace SiteSense.Simulator;

public enum SendResult { Accepted, Retry, Dropped }

public sealed class ApiClient(HttpClient http, SimulatorOptions options, TimeProvider time)
{
    // Refresh the cached gateway token this long before it expires.
    private static readonly TimeSpan TokenMargin = TimeSpan.FromSeconds(30);

    private string? _token;
    private DateTimeOffset _expiresAt;

    public static string BatchJson(string deviceId, IReadOnlyList<Fix> fixes) =>
        JsonSerializer.Serialize(new
        {
            deviceId,
            positions = fixes.Select(f => new { timestampMs = f.TimestampMs, lat = f.Lat, lon = f.Lon, accuracyM = f.AccuracyM }),
        });

    public async Task<SendResult> SendBatchAsync(string deviceId, IReadOnlyList<Fix> fixes, CancellationToken ct)
    {
        var json = BatchJson(deviceId, fixes);
        var url = $"{options.Api}/api/v1/positions";
        try
        {
            var token = await GetGatewayTokenAsync(false, ct);
            using var first = await SendRequestAsync(HttpMethod.Post, url, json, "application/json", token, ct);
            if (first.StatusCode != HttpStatusCode.Unauthorized) return await ClassifyAsync(deviceId, first, ct);

            token = await GetGatewayTokenAsync(true, ct);
            using var second = await SendRequestAsync(HttpMethod.Post, url, json, "application/json", token, ct);
            return await ClassifyAsync(deviceId, second, ct);
        }
        catch (Exception e) when (e is HttpRequestException || (e is OperationCanceledException && !ct.IsCancellationRequested))
        {
            // Network errors and HttpClient timeouts are transient; a caller-initiated cancel is not.
            return SendResult.Retry;
        }
    }

    public async Task SetupAsync(string siteJson, string mapGeoJson, IEnumerable<string> deviceIds, CancellationToken ct)
    {
        var (adminToken, _) = await RequestTokenAsync(new Dictionary<string, string>
        {
            ["grant_type"] = "password",
            ["client_id"] = options.AdminClient,
            ["username"] = options.AdminUser,
            ["password"] = options.AdminPassword,
        }, ct);

        using var site = JsonDocument.Parse(siteJson);
        var siteId = site.RootElement.TryGetProperty("siteId", out var idElement) ? idElement.GetString() : null;
        if (string.IsNullOrEmpty(siteId)) throw new InvalidOperationException("Site file has no siteId.");
        var siteUrl = $"{options.Api}/api/v1/sites/{Uri.EscapeDataString(siteId)}";

        using (var create = await SendRequestAsync(HttpMethod.Post, $"{options.Api}/api/v1/sites", siteJson, "application/json", adminToken, ct))
        {
            if (create.StatusCode == HttpStatusCode.Created)
            {
                using var map = await SendRequestAsync(HttpMethod.Put, $"{siteUrl}/map", mapGeoJson, "application/geo+json", adminToken, ct);
                if (!map.IsSuccessStatusCode) throw new InvalidOperationException($"Map upload failed: {(int)map.StatusCode}");
            }
            else if (create.StatusCode == HttpStatusCode.Conflict)
            {
                Console.WriteLine($"Site {siteId} exists, map upload skipped");
            }
            else
            {
                throw new InvalidOperationException($"Site create failed: {(int)create.StatusCode}");
            }
        }

        var count = 0;
        foreach (var id in deviceIds)
        {
            using var device = await SendRequestAsync(HttpMethod.Post, $"{siteUrl}/devices", JsonSerializer.Serialize(new { deviceId = id }), "application/json", adminToken, ct);
            if (device.StatusCode != HttpStatusCode.Created && device.StatusCode != HttpStatusCode.Conflict)
                throw new InvalidOperationException($"Device create failed: {(int)device.StatusCode}");
            count++;
        }
        Console.WriteLine($"Setup done: site {siteId}, {count} devices");
    }

    private async Task<string> GetGatewayTokenAsync(bool refresh, CancellationToken ct)
    {
        if (!refresh && _token is not null && time.GetUtcNow() < _expiresAt) return _token;

        var (token, expiresIn) = await RequestTokenAsync(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = options.Client,
            ["client_secret"] = options.Secret,
        }, ct);
        _token = token;
        _expiresAt = time.GetUtcNow().AddSeconds(expiresIn - TokenMargin.TotalSeconds);
        return token;
    }

    private async Task<(string Token, double ExpiresIn)> RequestTokenAsync(IEnumerable<KeyValuePair<string, string>> fields, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, options.TokenUrl) { Content = new FormUrlEncodedContent(fields) };
        using var response = await http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode) throw new HttpRequestException($"Token request failed: {(int)response.StatusCode}");

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        var root = doc.RootElement;
        var token = root.GetProperty("access_token").GetString()
            ?? throw new HttpRequestException("Token response has no access_token.");
        return (token, root.GetProperty("expires_in").GetDouble());
    }

    private async Task<HttpResponseMessage> SendRequestAsync(HttpMethod method, string url, string body, string contentType, string token, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, url) { Content = new StringContent(body, Encoding.UTF8, contentType) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await http.SendAsync(request, ct);
    }

    private static async Task<SendResult> ClassifyAsync(string deviceId, HttpResponseMessage response, CancellationToken ct)
    {
        var status = response.StatusCode;
        if (status == HttpStatusCode.Accepted)
        {
            // A 202 can still store nothing (unregistered device, no map): make that visible.
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            var r = body.RootElement;
            int Count(string name) => r.TryGetProperty(name, out var v) ? v.GetInt32() : 0;
            if (Count("stored") + Count("duplicates") < Count("received"))
                Console.WriteLine($"Batch for {deviceId}: {Count("received")} received, {Count("stored")} stored, {Count("outsidePerimeter")} outside the perimeter");
            return SendResult.Accepted;
        }
        if ((int)status >= 500) return SendResult.Retry;
        Console.WriteLine($"Batch for {deviceId} rejected: {(int)status}");
        return SendResult.Dropped;
    }
}
