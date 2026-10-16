using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Yukti.Application.Ai;
using Yukti.Orchestration;

namespace Yukti.Infrastructure.Ai;

/// <summary>
/// D5-T4: the concrete <see cref="IAnalyst"/> — builds the minimal registry context (token-economy
/// spec §R2: module kind, action names, one-line description, no <c>ParamSpec</c> detail, no
/// examples, minified JSON with a stable key order so prompt hashes/caching work later), calls
/// <see cref="IAiPlanningEngine"/> for capability <c>"analyst"</c>, and validates the model's JSON
/// against the real registry — a hallucinated module kind, or a response naming neither a
/// candidate module nor an ambiguity, both fail validation and feed the planning engine's own
/// retry (D5-T3), never pass through as a guess.
/// </summary>
public sealed class Analyst : IAnalyst
{
    public const string Capability = "analyst";
    public const string PromptVersion = "v1";

    private readonly IAiPlanningEngine _engine;
    private readonly IModuleRegistry _registry;

    public Analyst(IAiPlanningEngine engine, IModuleRegistry registry)
    {
        _engine = engine;
        _registry = registry;
    }

    public async Task<ParsedIntent> AnalyzeAsync(string description, CancellationToken ct)
    {
        var registeredKinds = _registry.All.Select(m => m.Kind.Value).ToHashSet(StringComparer.Ordinal);
        var context = BuildRegistryContext(_registry);

        var result = await _engine.PlanAsync(
            new AiPlanningRequest<ParsedIntent>(Capability, PromptVersion, description, context,
                (node, _) => Task.FromResult(Validate(node, registeredKinds))),
            ct);

        return result.Output;
    }

    /// <summary>§R2: kind + action name + one-line description only — no <see
    /// cref="Yukti.Domain.ModulePlugin.ParamSpec"/> fields, no examples. Sorted by kind then
    /// action name so the same registry always serialises to the same bytes (stable key order).</summary>
    private static string BuildRegistryContext(IModuleRegistry registry)
    {
        var summary = registry.All
            .OrderBy(m => m.Kind.Value, StringComparer.Ordinal)
            .Select(m => new
            {
                kind = m.Kind.Value,
                actions = m.GetSupportedActions()
                    .OrderBy(a => a.ActionName, StringComparer.Ordinal)
                    .Select(a => new { name = a.ActionName, description = a.Description ?? "" })
                    .ToArray(),
            })
            .ToArray();

        return JsonSerializer.Serialize(summary);
    }

    private static AiValidationOutcome<ParsedIntent> Validate(JsonNode? node, IReadOnlySet<string> registeredKinds)
    {
        if (node is null)
        {
            return AiValidationOutcome<ParsedIntent>.Invalid(["Response was not valid JSON."]);
        }

        var candidateKinds = JsonNodeHelpers.ReadStringArray(node, "candidateModuleKinds");
        var assertions = JsonNodeHelpers.ReadStringArray(node, "assertions");
        var ambiguities = JsonNodeHelpers.ReadStringArray(node, "ambiguities");

        if (candidateKinds is null || assertions is null || ambiguities is null)
        {
            return AiValidationOutcome<ParsedIntent>.Invalid(
                ["Response must have \"candidateModuleKinds\", \"assertions\", and \"ambiguities\" as JSON arrays of strings."]);
        }

        var errors = new List<string>();

        var unknownKinds = candidateKinds.Where(k => !registeredKinds.Contains(k)).ToList();
        if (unknownKinds.Count > 0)
        {
            errors.Add($"candidateModuleKinds named module(s) that aren't registered: {string.Join(", ", unknownKinds)}. " +
                $"Registered kinds are: {string.Join(", ", registeredKinds.OrderBy(k => k, StringComparer.Ordinal))}.");
        }

        if (candidateKinds.Count == 0 && ambiguities.Count == 0)
        {
            errors.Add("Response named no candidate modules and no ambiguity — never guess silently. " +
                "Either name the module(s) this request needs, or explain why it's out of scope or ambiguous.");
        }

        return errors.Count > 0
            ? AiValidationOutcome<ParsedIntent>.Invalid(errors)
            : AiValidationOutcome<ParsedIntent>.Valid(new ParsedIntent(candidateKinds, assertions, ambiguities));
    }
}
