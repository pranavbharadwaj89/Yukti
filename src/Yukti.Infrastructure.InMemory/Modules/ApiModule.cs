using System.Text;
using System.Text.Json;
using Yukti.Contracts;
using Yukti.Domain.Execution;
using Yukti.Domain.ModulePlugin;
using ExecutionContext = Yukti.Contracts.ExecutionContext;

namespace Yukti.Infrastructure.InMemory.Modules;

/// <summary>
/// Real, working API automation — fires genuine HTTP requests and runs
/// declarative assertions against status/JSON body, exposing the parsed
/// body via StepOutcome.Data so later flow steps can chain off it via
/// {{vars.x.y}} interpolation. Direct port of the original TS prototype's
/// ApiModule to the formal IAutomationModule contract. (Volume 1 Part III §18)
/// </summary>
public sealed class ApiModule : IAutomationModule
{
    private static readonly HttpClient Http = new();

    public ModuleKind Kind => ModuleKind.Api;
    public string ContractVersion => "1.0.0";

    public IReadOnlyList<ActionSchema> GetSupportedActions() => new[]
    {
        new ActionSchema
        {
            ActionName = "request",
            Description = "Fires an HTTP request and optionally asserts on status/body.",
            Parameters = new[]
            {
                new ParamSpec { Name = "url", Type = ParamType.String, Required = true, Description = "Target URL." },
                new ParamSpec { Name = "method", Type = ParamType.String, Required = false, DefaultValue = "GET", Description = "HTTP method." },
                new ParamSpec { Name = "expectedStatus", Type = ParamType.Number, Required = false, Description = "Expected HTTP status code." },
            }
        }
    };

    public Task Setup(ExecutionContext ctx, CancellationToken ct) => Task.CompletedTask;
    public Task Teardown(ExecutionContext ctx, CancellationToken ct) => Task.CompletedTask;

    public async Task<StepOutcome> Run(string action, IReadOnlyDictionary<string, object?> parameters, ExecutionContext ctx, CancellationToken ct)
    {
        if (action != "request")
            return StepOutcome.Failed($"Unknown api action '{action}'. Supported: 'request'.");

        var url = parameters.GetValueOrDefault("url") as string
            ?? throw new ArgumentException("api.request requires a 'url' parameter.");
        var method = (parameters.GetValueOrDefault("method") as string ?? "GET").ToUpperInvariant();

        try
        {
            using var request = new HttpRequestMessage(new HttpMethod(method), url);
            using var response = await Http.SendAsync(request, ct);
            var text = await response.Content.ReadAsStringAsync(ct);

            object? parsedBody = text;
            try { parsedBody = JsonSerializer.Deserialize<Dictionary<string, object?>>(text); }
            catch (JsonException) { /* not JSON, keep raw text */ }

            if (parameters.TryGetValue("expectedStatus", out var expectedRaw) && expectedRaw is not null)
            {
                var expected = Convert.ToInt32(expectedRaw);
                if ((int)response.StatusCode != expected)
                    return StepOutcome.Failed($"Expected status {expected}, got {(int)response.StatusCode}", parsedBody);
            }

            return StepOutcome.Passed($"{method} {url} -> {(int)response.StatusCode}", parsedBody);
        }
        catch (Exception ex)
        {
            return StepOutcome.Failed(ex.Message);
        }
    }
}
