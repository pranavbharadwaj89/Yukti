using System.Text.Json;
using Yukti.Application.FlowAuthoring;
using Yukti.Domain.FlowAuthoring;
using Yukti.Domain.ModulePlugin;

namespace Yukti.Cli;

/// <summary>Exit-2 territory: a problem with the flow file itself, never the flow's own logic.</summary>
public sealed class InvalidFlowFileException : Exception
{
    public InvalidFlowFileException(string message) : base(message) { }
    public InvalidFlowFileException(string message, Exception inner) : base(message, inner) { }
}

public static class FlowLoader
{
    private static readonly Dictionary<string, ModuleKind> KnownKinds = new(StringComparer.OrdinalIgnoreCase)
    {
        ["api"] = ModuleKind.Api,
        ["web"] = ModuleKind.Web,
        ["mobile"] = ModuleKind.Mobile,
        ["ui"] = ModuleKind.DesktopUi,
        ["logs"] = ModuleKind.Logs,
        ["ai"] = ModuleKind.Ai,
    };

    private static ModuleKind ParseModuleKind(string value) =>
        KnownKinds.TryGetValue(value, out var known) ? known : ModuleKind.Custom(value.ToLowerInvariant());

    /// <summary>
    /// Reads the flow file, builds a Draft Flow through the same
    /// CreateFlowCommand/AddFlowStepCommand handlers the real API uses, then
    /// publishes it through the same Flow.Publish/validator D2-T5 proved —
    /// so "validate with the same publish rules before running" is
    /// structural, not a separate check this file reimplements.
    /// </summary>
    public static async Task<Flow> LoadAndPublish(string path, CliHost host, CancellationToken ct)
    {
        if (!File.Exists(path))
            throw new InvalidFlowFileException($"Flow file not found: {path}");

        string text;
        try
        {
            text = await File.ReadAllTextAsync(path, ct);
        }
        catch (IOException ex)
        {
            throw new InvalidFlowFileException($"Could not read flow file '{path}': {ex.Message}", ex);
        }

        FlowFileDto dto;
        try
        {
            dto = JsonSerializer.Deserialize<FlowFileDto>(text, FlowFileJson.Options)
                ?? throw new InvalidFlowFileException($"Flow file '{path}' is empty or deserialized to null.");
        }
        catch (JsonException ex)
        {
            throw new InvalidFlowFileException($"Flow file '{path}' is not valid JSON or has an unrecognized field: {ex.Message}", ex);
        }

        if (string.IsNullOrWhiteSpace(dto.Name))
            throw new InvalidFlowFileException($"Flow file '{path}' is missing a required 'name' field.");
        if (dto.Steps.Count == 0)
            throw new InvalidFlowFileException($"Flow file '{path}' has no steps.");

        var flowId = await host.CreateFlow.Handle(
            new CreateFlowCommand(dto.Name, dto.Description, host.TenantId, host.UserId), ct);

        for (var i = 0; i < dto.Steps.Count; i++)
        {
            var step = dto.Steps[i];
            if (string.IsNullOrWhiteSpace(step.Module))
                throw new InvalidFlowFileException($"Flow file '{path}': steps[{i}] is missing a required 'module' field.");
            if (string.IsNullOrWhiteSpace(step.Action))
                throw new InvalidFlowFileException($"Flow file '{path}': steps[{i}] is missing a required 'action' field.");

            await host.AddStep.Handle(new AddFlowStepCommand(
                flowId, string.IsNullOrWhiteSpace(step.Name) ? $"Step {i + 1}" : step.Name,
                ParseModuleKind(step.Module), step.Action, FlowFileJson.NormalizeParams(step.Params),
                step.SaveAs, step.When, host.UserId), ct);
        }

        if (dto.ContinueOnFailure || dto.DeclaredVariables.Count > 0)
        {
            // Neither CreateFlowCommand nor a dedicated command exists for
            // these two, so both are set directly on the aggregate rather
            // than through a command handler — same direct-mutation-then-
            // commit pattern Yukti.Host's own seeding already uses (no
            // AuditableCommandHandler wraps it, so the commit here is this
            // call's own responsibility).
            var flow = await host.Flows.GetById(flowId, ct) ?? throw new InvalidOperationException("Flow vanished immediately after creation.");
            if (dto.ContinueOnFailure)
                flow.SetContinueOnFailure(true);
            foreach (var name in dto.DeclaredVariables)
                flow.DeclareVariable(name);
            await host.Flows.Save(flow, ct);
            await using var uow = host.UowFactory.Create();
            await uow.Commit(ct);
        }

        var publishResult = await host.Publish.Handle(new PublishFlowCommand(flowId, host.UserId), ct);
        if (!publishResult.Succeeded)
        {
            var details = string.Join(Environment.NewLine, publishResult.Errors.Select(e => $"  [{e.Code}] {e.Field}: {e.Message}"));
            throw new InvalidFlowFileException($"Flow '{dto.Name}' failed publish validation:{Environment.NewLine}{details}");
        }

        return await host.Flows.GetById(flowId, ct) ?? throw new InvalidOperationException("Flow vanished after publish.");
    }
}
