using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using SiteSense.Tests.Api.Auth;

namespace SiteSense.Tests.Api;

[Collection("Api")]
public sealed class ErrorResponseTests : IClassFixture<ApiFactory>
{
    private const string ProblemJson = "application/problem+json";

    private readonly HttpClient _client;

    public ErrorResponseTests(ApiFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Throw_UnhandledException_Returns500ProblemWithoutStackTrace()
    {
        // Arrange
        // Act
        var response = await _client.GetAsync("/probe/throw");
        var body = await response.Content.ReadAsStringAsync();

        // Assert
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal(ProblemJson, response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(500, GetInt(body, "status"));
        Assert.True(HasProperty(body, "title"));
        Assert.DoesNotContain("probe-secret-detail", body);
        Assert.DoesNotContain("InvalidOperationException", body);
        Assert.DoesNotContain("stackTrace", body);
    }

    [Fact]
    public async Task Echo_LatOutOfRange_Returns400WithFieldError()
    {
        // Arrange
        var payload = new { from = new { lat = 91.0, lon = 10.0 } };

        // Act
        var response = await _client.PostAsJsonAsync("/probe/echo", payload);
        var body = await response.Content.ReadAsStringAsync();

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(ProblemJson, response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(new[] { "From.Lat" }, ErrorKeys(body));
    }

    [Fact]
    public async Task Echo_MissingLon_Returns400WithFieldError()
    {
        // Arrange
        var content = new StringContent("{ \"from\": { \"lat\": 1 } }", Encoding.UTF8, "application/json");

        // Act
        var response = await _client.PostAsync("/probe/echo", content);
        var body = await response.Content.ReadAsStringAsync();

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(ProblemJson, response.Content.Headers.ContentType?.MediaType);
        Assert.True(HasErrorMessageContaining(body, "lon"));
    }

    [Fact]
    public async Task Echo_MalformedJson_Returns400Problem()
    {
        // Arrange
        var content = new StringContent("{ \"from\": ", Encoding.UTF8, "application/json");

        // Act
        var response = await _client.PostAsync("/probe/echo", content);
        var body = await response.Content.ReadAsStringAsync();

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(ProblemJson, response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(new[] { "$.from", "request" }, ErrorKeys(body));
    }

    [Fact]
    public async Task Echo_NullFrom_Returns400Problem()
    {
        // Arrange
        var content = new StringContent("{ \"from\": null }", Encoding.UTF8, "application/json");

        // Act
        var response = await _client.PostAsync("/probe/echo", content);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(ProblemJson, response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Echo_WrongContentType_Returns415Problem()
    {
        // Arrange
        var content = new StringContent("{ \"from\": { \"lat\": 1, \"lon\": 2 } }", Encoding.UTF8, "text/plain");

        // Act
        var response = await _client.PostAsync("/probe/echo", content);

        // Assert
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
        Assert.Equal(ProblemJson, response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Echo_GetInsteadOfPost_Returns405Problem()
    {
        // Arrange
        using var request = new HttpRequestMessage(HttpMethod.Get, "/probe/echo");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", TestTokens.Create());

        // Act
        var response = await _client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
        Assert.Equal(ProblemJson, response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Get_UnknownRoute_Returns404Problem()
    {
        // Arrange
        using var request = new HttpRequestMessage(HttpMethod.Get, "/nope");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", TestTokens.Create());

        // Act
        var response = await _client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(ProblemJson, response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(404, GetInt(body, "status"));
    }

    [Fact]
    public async Task Get_UnknownRouteWithJsonAccept_Returns404Problem()
    {
        // Arrange
        using var request = new HttpRequestMessage(HttpMethod.Get, "/nope");
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", TestTokens.Create());

        // Act
        var response = await _client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(ProblemJson, response.Content.Headers.ContentType?.MediaType);
    }

    private static string[] ErrorKeys(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("errors").EnumerateObject().Select(p => p.Name).OrderBy(n => n).ToArray();
    }

    private static bool HasProperty(string json, string name)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.TryGetProperty(name, out _);
    }

    private static int GetInt(string json, string name)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty(name).GetInt32();
    }

    private static bool HasErrorMessageContaining(string json, string text)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("errors").EnumerateObject()
            .SelectMany(p => p.Value.EnumerateArray())
            .Any(m => m.GetString()!.Contains(text, StringComparison.OrdinalIgnoreCase));
    }
}
