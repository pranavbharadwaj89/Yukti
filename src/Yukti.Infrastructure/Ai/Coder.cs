using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Yukti.Application.Ai;
using Yukti.Domain.FlowAuthoring;
using Yukti.Domain.ModulePlugin;
using Yukti.Domain.SharedKernel;
using Yukti.Orchestration;

namespace Yukti.Infrastructure.Ai;

/// <summary>
/// D5-T5: the concrete <see cref="ICoder"/> — <see cref="ParsedIntent"/> → a Draft <see
/// cref="Flow"/>. Builds the token-economy spec's §R2 context (full <see cref="ActionSchema"/>,
/// but only for the candidate modules <see cref="ParsedIntent.CandidateModuleKinds"/> names, never
/// the whole registry — the difference from Analyst's own, deliberately thinner context), calls
/// <see cref="IAiPlanningEngine"/> for capability <c>"coder"</c>, and validates the model's JSON by
/// actually running <see cref="Flow.Publish"/> — Day 2's own validator, reused verbatim, never a
/// second copy of its rules. Because <see cref="Flow.Publish"/> mutates a Flow's
/// <see cref="FlowStatus"/> to <see cref="FlowStatus.Published"/> on success, validation runs
/// against a throwaway instance; the Flow actually returned to the caller is built fresh and never
/// published, so "always Status = Draft" is structural, not a flag this class remembers to set.
/// </summary>
public sealed class Coder : ICoder
{
    public const string Capability = "coder";
    public const string PromptVersion = "v1";

    private readonly IAiPlanningEngine _engine;
    private readonly IModuleRegistry _registry;
    private readonly IModuleActionResolver _resolver;

    public Coder(IAiPlanningEngine engine, IModuleRegistry registry, IModuleActionResolver resolver)
    {
        _engine = engine;
        _registry = registry;
        _resolver = resolver;
    }

    public async Task<Flow> GenerateAsync(ParsedIntent intent, TenantId tenantId, UserId createdBy, CancellationToken ct)
    {
        var context = BuildCandidateModuleContext(intent.CandidateModuleKinds);
        var description = BuildDescription(intent);

        var result = await _engine.PlanAsync(
            new AiPlanningRequest<Flow>(Capability, PromptVersion, description, context,
                (node, innerCt) => ValidateAsync(node, intent, tenantId, createdBy, innerCt)),
            ct);

        return result.Output;
    }

    /// <summary>
    /// The user's original description alone isn't enough context for Coder (it never saw the
    /// registry), so the description sent downstream folds in Analyst's own findings — the
    /// assertions it extracted, restated as plain English for the model to act on. Still goes to
    /// the provider as user-role content, never into the system prompt template (D5-T3's own
    /// separation).
    /// </summary>
    private static string BuildDescription(ParsedIntent intent) =>
        intent.Assertions.Count == 0
            ? "Build a flow using the candidate module(s) listed in the context."
            : "Build a flow that checks: " + string.Join("; ", intent.Assertions);

    /// <summary>§R2: full <see cref="ActionSchema"/> (every <see cref="ParamSpec"/>), but only for
    /// the candidate modules — never the whole registry. Minified, stable key order, same as
    /// Analyst's own context assembly.</summary>
    private string BuildCandidateModuleContext(IReadOnlyList<string> candidateModuleKinds)
    {
        var summary = candidateModuleKinds
            .OrderBy(k => k, StringComparer.Ordinal)
            .Select(kind => _registry.Resolve(ModuleKind.Custom(kind)))
            .Where(module => module is not null)
            .Select(module => new
            {
                kind = module!.Kind.Value,
                actions = module.GetSupportedActions()
                    .OrderBy(a => a.ActionName, StringComparer.Ordinal)
                    .Select(a => new
                    {
                        name = a.ActionName,
                        description = a.Description ?? "",
                        parameters = a.Parameters
                            .OrderBy(p => p.Name, StringComparer.Ordinal)
                            .Select(p => new
                            {
                                name = p.Name,
                                type = p.Type.ToString(),
                                required = p.Required,
                                description = p.Description ?? "",
                            })
                            .ToArray(),
                    })
                    .ToArray(),
            })
            .ToArray();

        return JsonSerializer.Serialize(summary);
    }

    private async Task<AiValidationOutcome<Flow>> ValidateAsync(
        JsonNode? node, ParsedIntent intent, TenantId tenantId, UserId createdBy, CancellationToken ct)
    {
        if (node is null)
        {
            return AiValidationOutcome<Flow>.Invalid(["Response was not valid JSON."]);
        }

        var name = node["name"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(name))
        {
            return AiValidationOutcome<Flow>.Invalid(["Response must have a non-empty \"name\" string."]);
        }

        if (node["steps"] is not JsonArray stepsNode || stepsNode.Count == 0)
        {
            return AiValidationOutcome<Flow>.Invalid(["Response must have a non-empty \"steps\" array."]);
        }

        var candidateSet = new HashSet<string>(intent.CandidateModuleKinds, StringComparer.Ordinal);
        var parsedSteps = new List<ParsedStep>();
        foreach (var stepNode in stepsNode)
        {
            if (stepNode is null)
            {
                return AiValidationOutcome<Flow>.Invalid(["Each entry in \"steps\" must be a JSON object."]);
            }

            var stepName = stepNode["name"]?.GetValue<string>();
            var module = stepNode["module"]?.GetValue<string>();
            var action = stepNode["action"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(stepName) || string.IsNullOrWhiteSpace(module) || string.IsNullOrWhiteSpace(action))
            {
                return AiValidationOutcome<Flow>.Invalid(["Each step must have non-empty \"name\", \"module\", and \"action\" fields."]);
            }

            if (!candidateSet.Contains(module))
            {
                return AiValidationOutcome<Flow>.Invalid(
                    [$"Step '{stepName}' uses module '{module}', which is not one of the candidate modules ({string.Join(", ", intent.CandidateModuleKinds)})."]);
            }

            var parameters = stepNode["params"] is JsonObject paramsObj
                ? paramsObj.ToDictionary(p => p.Key, p => JsonNodeHelpers.ToPlainValue(p.Value))
                : new Dictionary<string, object?>();
            var saveAs = stepNode["saveAs"]?.GetValue<string>();
            var when = stepNode["when"]?.GetValue<string>();
            parsedSteps.Add(new ParsedStep(stepName, module, action, parameters, saveAs, when));
        }

        var declaredVars = JsonNodeHelpers.ReadStringArray(node, "declaredVariables") ?? [];

        try
        {
            // Day 2's own publish validator, reused verbatim — run against a throwaway instance
            // since Publish mutates Status on success (see this class's own doc comment).
            var validationFlow = BuildFlow(name, declaredVars, parsedSteps, tenantId, createdBy);
            var publishResult = await validationFlow.Publish(_resolver, ct);
            if (!publishResult.Succeeded)
            {
                return AiValidationOutcome<Flow>.Invalid(
                    publishResult.Errors.Select(e => $"{e.Field} [{e.Code}]: {e.Message}").ToList());
            }
        }
        catch (DomainException ex)
        {
            return AiValidationOutcome<Flow>.Invalid([ex.Message]);
        }

        var resultFlow = BuildFlow(name, declaredVars, parsedSteps, tenantId, createdBy);
        return AiValidationOutcome<Flow>.Valid(resultFlow);
    }

    private static Flow BuildFlow(string name, List<string> declaredVars, List<ParsedStep> steps, TenantId tenantId, UserId createdBy)
    {
        var flow = Flow.CreateDraft(name, description: null, tenantId, createdBy);
        foreach (var v in declaredVars)
        {
            flow.DeclareVariable(v);
        }
        foreach (var s in steps)
        {
            flow.AddStep(s.Name, ModuleKind.Custom(s.Module), s.Action, s.Params, s.SaveAs, s.When);
        }
        return flow;
    }

    private sealed record ParsedStep(string Name, string Module, string Action, IReadOnlyDictionary<string, object?> Params, string? SaveAs, string? When);
}
