using System.Text;

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

        return app;
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
