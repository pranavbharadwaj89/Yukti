using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FixtureApp;
using Microsoft.AspNetCore.Builder;
using Xunit;

namespace Yukti.Orchestration.Tests;

/// <summary>FixtureApp's /oauth/token + /items endpoints: the backend for
/// demo/api_oauth_chain.json, the API Testing Module gate's own acceptance
/// flow. Proves the fixture itself, independent of the ApiModule that will
/// drive it.</summary>
public sealed class FixtureAppOAuthItemsTests : IAsyncLifetime
{
    private WebApplication _app = null!;
    private string _logPath = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        _logPath = Path.Combine(Path.GetTempPath(), $"fixtureapp-oauth-test-{Guid.NewGuid():N}.log");
        _app = FixtureAppHost.Build(logPath: _logPath);
        await _app.StartAsync();
        _client = new HttpClient { BaseAddress = new Uri(_app.Urls.First()) };
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _app.DisposeAsync();
        if (File.Exists(_logPath)) File.Delete(_logPath);
    }

    private async Task<string> FetchToken()
    {
        var response = await _client.PostAsync("/oauth/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = "demo-client",
            ["client_secret"] = "dummy-demo-secret",
        }));
        response.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("access_token").GetString()!;
    }

    [Fact]
    public async Task Valid_client_credentials_issue_a_bearer_token()
    {
        var response = await _client.PostAsync("/oauth/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = "demo-client",
            ["client_secret"] = "dummy-demo-secret",
        }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.False(string.IsNullOrEmpty(doc.RootElement.GetProperty("access_token").GetString()));
        Assert.Equal(3600, doc.RootElement.GetProperty("expires_in").GetInt32());
    }

    [Fact]
    public async Task Wrong_client_secret_is_rejected()
    {
        var response = await _client.PostAsync("/oauth/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = "demo-client",
            ["client_secret"] = "wrong",
        }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Creating_an_item_without_a_token_is_rejected()
    {
        var response = await _client.PostAsJsonAsync("/items", new { name = "widget" });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task A_valid_token_creates_and_then_fetches_the_item_back()
    {
        var token = await FetchToken();
        using var createRequest = new HttpRequestMessage(HttpMethod.Post, "/items")
        {
            Content = JsonContent.Create(new { name = "widget" }),
        };
        createRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var createResponse = await _client.SendAsync(createRequest);
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var created = await createResponse.Content.ReadFromJsonAsync<FixtureItem>();

        using var getRequest = new HttpRequestMessage(HttpMethod.Get, $"/items/{created!.Id}");
        getRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var getResponse = await _client.SendAsync(getRequest);
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        var fetched = await getResponse.Content.ReadFromJsonAsync<FixtureItem>();
        Assert.Equal("widget", fetched!.Name);
        Assert.Equal(created.Id, fetched.Id);
    }
}
