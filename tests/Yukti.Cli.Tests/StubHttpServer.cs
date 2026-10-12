using System.Net;
using System.Text;
using System.Text.Json;

namespace Yukti.Cli.Tests;

/// <summary>
/// An in-process HttpListener on a random free port serving the demo
/// flow's two routes (POST /items, GET /items/:id, GET /items) — so the
/// integration tests never touch the real internet, per the spec. Disposed
/// per test; each instance gets its own in-memory item store.
/// </summary>
public sealed class StubHttpServer : IDisposable
{
    private readonly HttpListener _listener;
    private readonly Dictionary<string, (string Id, string Name)> _items = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _loop;
    private int _flakyCallCount;

    public string BaseUrl { get; }

    public StubHttpServer()
    {
        var port = GetFreePort();
        BaseUrl = $"http://127.0.0.1:{port}";
        _listener = new HttpListener();
        _listener.Prefixes.Add(BaseUrl + "/");
        _listener.Start();
        _loop = Task.Run(AcceptLoop);
    }

    private static int GetFreePort()
    {
        using var socket = new System.Net.Sockets.Socket(System.Net.Sockets.AddressFamily.InterNetwork, System.Net.Sockets.SocketType.Stream, System.Net.Sockets.ProtocolType.Tcp);
        socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        return ((IPEndPoint)socket.LocalEndPoint!).Port;
    }

    private async Task AcceptLoop()
    {
        while (!_cts.IsCancellationRequested)
        {
            HttpListenerContext ctx;
            try
            {
                ctx = await _listener.GetContextAsync().WaitAsync(_cts.Token);
            }
            catch (Exception) when (_cts.IsCancellationRequested)
            {
                return;
            }
            _ = HandleAsync(ctx);
        }
    }

    private async Task HandleAsync(HttpListenerContext ctx)
    {
        try
        {
            var path = ctx.Request.Url!.AbsolutePath;

            if (ctx.Request.HttpMethod == "POST" && path == "/items")
            {
                using var reader = new StreamReader(ctx.Request.InputStream);
                var body = await reader.ReadToEndAsync();
                var parsed = JsonDocument.Parse(string.IsNullOrEmpty(body) ? "{}" : body);
                var name = parsed.RootElement.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                var id = Guid.NewGuid().ToString();
                _items[id] = (id, name);
                await WriteJson(ctx, 201, new { id, name });
                return;
            }

            if (ctx.Request.HttpMethod == "GET" && path == "/items")
            {
                await WriteJson(ctx, 200, _items.Values.Select(i => new { id = i.Id, name = i.Name }).ToList());
                return;
            }

            if (ctx.Request.HttpMethod == "GET" && path == "/flaky")
            {
                // Fails the first call, succeeds every call after — the
                // same shape a genuinely flaky real-world dependency has.
                // Exercises the engine's real default retry policy
                // (MaxAttempts: 2) through the real ApiModule, rather than
                // needing a test-only module the CLI's fixed process
                // composition root has no way to register.
                var callNumber = Interlocked.Increment(ref _flakyCallCount);
                await WriteJson(ctx, callNumber == 1 ? 500 : 200, new { attempt = callNumber });
                return;
            }

            if (ctx.Request.HttpMethod == "GET" && path == "/slow")
            {
                await Task.Delay(TimeSpan.FromSeconds(5));
                await WriteJson(ctx, 200, new { ok = true });
                return;
            }

            if (ctx.Request.HttpMethod == "GET" && path.StartsWith("/items/"))
            {
                var id = path["/items/".Length..];
                if (_items.TryGetValue(id, out var item))
                {
                    await WriteJson(ctx, 200, new { id = item.Id, name = item.Name });
                    return;
                }
                ctx.Response.StatusCode = 404;
                ctx.Response.Close();
                return;
            }

            ctx.Response.StatusCode = 404;
            ctx.Response.Close();
        }
        catch (Exception)
        {
            try { ctx.Response.StatusCode = 500; ctx.Response.Close(); } catch { /* connection already gone */ }
        }
    }

    private static async Task WriteJson(HttpListenerContext ctx, int status, object body)
    {
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(body));
        ctx.Response.StatusCode = status;
        ctx.Response.ContentType = "application/json";
        ctx.Response.ContentLength64 = bytes.Length;
        await ctx.Response.OutputStream.WriteAsync(bytes);
        ctx.Response.Close();
    }

    public void Dispose()
    {
        _cts.Cancel();
        _listener.Stop();
        _listener.Close();
        try { _loop.Wait(TimeSpan.FromSeconds(2)); } catch { /* best-effort shutdown */ }
        _cts.Dispose();
    }
}
