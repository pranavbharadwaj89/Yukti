using System.Net;
using System.Net.Http.Json;
using FixtureApp;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Xunit;

namespace Yukti.Orchestration.Tests;

/// <summary>D3-T1: FixtureApp's own contract, independent of any Yukti
/// module — a believable third-party app's login → orders → API chain,
/// with every request logged. WebModule/LogsModule proofs (D3-T2/D3-T3)
/// build on top of this; this file only proves the fixture itself.</summary>
public sealed class FixtureAppTests : IAsyncLifetime
{
    private WebApplication _app = null!;
    private string _logPath = null!;
    public string BaseUrl { get; private set; } = null!;
    public HttpClient Client { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        _logPath = Path.Combine(Path.GetTempPath(), $"fixtureapp-test-{Guid.NewGuid():N}.log");
        _app = FixtureAppHost.Build(logPath: _logPath);
        await _app.StartAsync();
        BaseUrl = _app.Urls.First();
        Client = new HttpClient { BaseAddress = new Uri(BaseUrl) };
    }

    public async Task DisposeAsync()
    {
        Client.Dispose();
        await _app.DisposeAsync();
        if (File.Exists(_logPath)) File.Delete(_logPath);
    }

    [Fact]
    public async Task Starts_on_a_real_random_port_with_no_fixed_assignment()
    {
        Assert.NotEqual(":0", new Uri(BaseUrl).Port.ToString());
        Assert.True(new Uri(BaseUrl).Port > 0);
    }

    [Fact]
    public async Task Login_page_has_the_required_testids()
    {
        var html = await Client.GetStringAsync("/login");
        Assert.Contains("data-testid=\"username\"", html);
        Assert.Contains("data-testid=\"password\"", html);
        Assert.Contains("data-testid=\"login-submit\"", html);
    }

    [Fact]
    public async Task Posting_login_redirects_to_orders_with_a_real_order_id_visible()
    {
        using var noRedirectClient = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { BaseAddress = new Uri(BaseUrl) };
        var loginResponse = await noRedirectClient.PostAsync("/login", new FormUrlEncodedContent(new Dictionary<string, string>()));

        Assert.Equal(HttpStatusCode.Redirect, loginResponse.StatusCode);
        Assert.Equal("/orders", loginResponse.Headers.Location!.OriginalString);

        var cookie = loginResponse.Headers.GetValues("Set-Cookie").First();
        using var ordersRequest = new HttpRequestMessage(HttpMethod.Get, "/orders");
        ordersRequest.Headers.Add("Cookie", cookie.Split(';')[0]);
        using var ordersClient = new HttpClient { BaseAddress = new Uri(BaseUrl) };
        var ordersHtml = await (await ordersClient.SendAsync(ordersRequest)).Content.ReadAsStringAsync();

        Assert.Contains("data-testid=\"order-id\"", ordersHtml);
    }

    [Fact]
    public async Task Orders_api_returns_confirmed_status_for_any_id()
    {
        var response = await Client.GetFromJsonAsync<OrderDto>("/api/orders/abc123");
        Assert.Equal("abc123", response!.Id);
        Assert.Equal("confirmed", response.Status);
    }

    [Fact]
    public async Task Every_request_is_logged_with_timestamp_level_and_order_id()
    {
        await Client.GetAsync("/api/orders/log-check-1");
        await Task.Delay(50); // the logging middleware writes after the response is sent
        var logText = await File.ReadAllTextAsync(_logPath);

        Assert.Contains("orderId=log-check-1", logText);
        Assert.Contains(" INFO ", logText);
        Assert.Matches(@"\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}", logText);
    }

    [Fact]
    public async Task A_fail_query_param_logs_an_ERROR_line_for_that_order()
    {
        await Client.GetAsync("/api/orders/log-check-2?fail=1");
        await Task.Delay(50);
        var logText = await File.ReadAllTextAsync(_logPath);

        var errorLine = Assert.Single(logText.Split('\n', StringSplitOptions.RemoveEmptyEntries), l => l.Contains("log-check-2"));
        Assert.Contains(" ERROR ", errorLine);
    }

    private sealed record OrderDto(string Id, string Status);
}
