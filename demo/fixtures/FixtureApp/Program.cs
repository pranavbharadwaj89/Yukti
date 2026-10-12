using FixtureApp;

// Manual/demo entry point: `dotnet run --project demo/fixtures/FixtureApp`
// binds a fixed, easy-to-remember port and writes to the real
// demo/fixtures/app.log. Tests never go through this file at all — they
// call FixtureAppHost.Build directly and host it in-process on port 0.
var app = FixtureAppHost.Build(urls: "http://127.0.0.1:5082");
Console.WriteLine("FixtureApp listening on http://127.0.0.1:5082 — try /login");
await app.RunAsync();
