using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;

namespace FixtureApp;

/// <summary>
/// Builds (but does not start) the fixture app. Callers control
/// StartAsync/the bound port — tests host this in-process on port 0 (no
/// subprocess, no apphost/runtime resolution involved at all, since it
/// runs inside the already-running test process); demo/manual use goes
/// through Program.cs's thin entry point instead.
/// </summary>
public static class FixtureAppHost
{
    public const string DefaultLogPath = "demo/fixtures/app.log";

    public static WebApplication Build(string? logPath = null, string? urls = null)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders(); // this app's own log noise isn't the point — app.log is
        builder.WebHost.UseUrls(urls ?? "http://127.0.0.1:0");

        builder.Services.AddDistributedMemoryCache();
        builder.Services.AddSession(options =>
        {
            options.Cookie.Name = "fixtureapp.session";
            options.IdleTimeout = TimeSpan.FromMinutes(30);
        });
        builder.Services.AddSingleton(new LogSink(logPath ?? DefaultLogPath));
        builder.Services.AddSingleton<TokenStore>();
        builder.Services.AddSingleton<ItemStore>();

        var app = builder.Build();
        app.UseSession();

        // Every request gets exactly one app.log line, written in OnStarting —
        // after the endpoint has set the status code and order id, but BEFORE
        // the client receives any byte of the response. So by the time a
        // caller has the response, its log line is already on disk; a Logs
        // step that runs right after an API step can't race it. The order id
        // is whatever the endpoint recorded in HttpContext.Items ("-" if none).
        app.Use(async (context, next) =>
        {
            context.Response.OnStarting(() =>
            {
                var sink = context.RequestServices.GetRequiredService<LogSink>();
                var orderId = context.Items.TryGetValue("orderId", out var id) ? id as string ?? "-" : "-";
                var forcedFailure = context.Request.Query.ContainsKey("fail");
                var level = forcedFailure ? "ERROR" : "INFO";
                var detail = forcedFailure
                    ? "simulated failure requested via ?fail=1"
                    : $"-> {context.Response.StatusCode}";
                sink.Write($"{DateTimeOffset.UtcNow:O} {level} orderId={orderId} {context.Request.Method} {context.Request.Path}{context.Request.QueryString} {detail}");
                return Task.CompletedTask;
            });

            await next();
        });

        app.MapGet("/login", () => Results.Content(LoginPageHtml, "text/html"));

        app.MapPost("/login", async (HttpContext context) =>
        {
            // A fixture doesn't need real credential checking — the point is
            // the flow shape (fill, submit, redirect, land on a page with a
            // real server-assigned id), not authentication itself.
            var orderId = Guid.NewGuid().ToString("N")[..12];
            context.Items["orderId"] = orderId;
            context.Session.SetString("orderId", orderId);
            await context.Session.CommitAsync();
            return Results.Redirect("/orders");
        });

        app.MapGet("/orders", (HttpContext context) =>
        {
            var orderId = context.Session.GetString("orderId");
            context.Items["orderId"] = orderId ?? "-";
            if (orderId is null)
                return Results.Redirect("/login");
            return Results.Content(OrdersPageHtml(orderId), "text/html");
        });

        app.MapGet("/api/orders/{id}", (HttpContext context, string id) =>
        {
            context.Items["orderId"] = id;
            return Results.Json(new { id, status = "confirmed" });
        });

        // Web UI Testing Module Spec, WEB-T8's own "scanned for secrets before storage" test --
        // sets real cookies (Set-Cookie, no JavaScript/executeScript needed) including one
        // literally named "password", so a real WebModule.saveStorageState run has something
        // genuine to redact. Additive only; no existing route touched.
        app.MapGet("/set-test-cookies", (HttpContext context) =>
        {
            context.Response.Cookies.Append("password", "plaintext-secret");
            context.Response.Cookies.Append("authToken", "keep-this-session-token");
            return Results.Content("cookies set", "text/plain");
        });

        // The demo/api_oauth_chain.json gate flow's own backend: an OAuth2
        // client-credentials token endpoint plus a tiny items resource, so
        // that flow can fetch a token, create with a JSON body, extract the
        // id, and fetch it back -- without reaching out to a real OAuth
        // provider or a non-demo dependency.
        app.MapPost("/oauth/token", async (HttpContext context, TokenStore tokens) =>
        {
            var form = await context.Request.ReadFormAsync();
            if (form["client_id"] != DemoClientId || form["client_secret"] != DemoClientSecret)
                return Results.Json(new { error = "invalid_client", error_description = "unknown client_id or client_secret" }, statusCode: 400);

            var token = tokens.Issue();
            return Results.Json(new { access_token = token, token_type = "Bearer", expires_in = 3600 });
        });

        app.MapPost("/items", async (HttpContext context, ItemStore items, TokenStore tokens) =>
        {
            if (!tokens.IsValid(BearerToken(context)))
                return Results.Json(new { error = "invalid_token" }, statusCode: 401);

            var body = await JsonDocument.ParseAsync(context.Request.Body);
            if (!body.RootElement.TryGetProperty("name", out var nameEl) || nameEl.GetString() is not string { Length: > 0 } name)
                return Results.Json(new { error = "'name' is required" }, statusCode: 400);

            return Results.Json(items.Create(name), statusCode: 201);
        });

        app.MapGet("/items/{id}", (HttpContext context, string id, ItemStore items, TokenStore tokens) =>
        {
            if (!tokens.IsValid(BearerToken(context)))
                return Results.Json(new { error = "invalid_token" }, statusCode: 401);

            var item = items.Get(id);
            return item is null ? Results.NotFound() : Results.Json(item);
        });

        return app;
    }

    private const string DemoClientId = "demo-client";
    private const string DemoClientSecret = "dummy-demo-secret";

    private static string? BearerToken(HttpContext context)
    {
        var header = context.Request.Headers.Authorization.ToString();
        return header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? header["Bearer ".Length..] : null;
    }

    private const string LoginPageHtml = """
        <!doctype html>
        <html>
        <body>
          <h1>Sign in</h1>
          <form method="post" action="/login">
            <input data-testid="username" name="username" type="text" />
            <input data-testid="password" name="password" type="password" />
            <select data-testid="login-region" name="region">
              <option value="">Choose a region</option>
              <option value="us">United States</option>
              <option value="eu">Europe</option>
            </select>
            <button data-testid="login-submit" type="submit">Sign in</button>
          </form>
        </body>
        </html>
        """;

    private static string OrdersPageHtml(string orderId) => $"""
        <!doctype html>
        <html>
        <body>
          <h1>Your order</h1>
          <p data-testid="order-id">{orderId}</p>
          <p data-testid="order-status">pending</p>
          <p data-testid="hidden-field" style="display:none">internal-only</p>
        </body>
        </html>
        """;
}

/// <summary>Thread-safe append-only writer for app.log — a test fixture's
/// request volume never justifies anything fancier than a lock.</summary>
public sealed class LogSink
{
    private readonly object _gate = new();
    private readonly string _path;

    public LogSink(string path)
    {
        _path = path;
        var dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (dir is not null) Directory.CreateDirectory(dir);
    }

    public void Write(string line)
    {
        lock (_gate)
        {
            File.AppendAllText(_path, line + Environment.NewLine, Encoding.UTF8);
        }
    }
}

/// <summary>Tokens issued by POST /oauth/token for the demo/api_oauth_chain.json
/// gate flow. No expiry here -- ApiModule's own OAuth cache already proves
/// expiry/reuse behavior (API-T5); this fixture only needs to tell a real
/// issued token apart from a missing/forged one.</summary>
public sealed class TokenStore
{
    private readonly ConcurrentDictionary<string, bool> _issued = new();

    public string Issue()
    {
        var token = Guid.NewGuid().ToString("N");
        _issued[token] = true;
        return token;
    }

    public bool IsValid(string? token) => token is not null && _issued.ContainsKey(token);
}

/// <summary>Backs POST/GET /items for the demo/api_oauth_chain.json gate flow.</summary>
public sealed class ItemStore
{
    private readonly ConcurrentDictionary<string, FixtureItem> _items = new();

    public FixtureItem Create(string name)
    {
        var item = new FixtureItem(Guid.NewGuid().ToString("N"), name, DateTimeOffset.UtcNow.ToString("O"));
        _items[item.Id] = item;
        return item;
    }

    public FixtureItem? Get(string id) => _items.GetValueOrDefault(id);
}

public sealed record FixtureItem(string Id, string Name, string CreatedAt);
