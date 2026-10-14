using Xunit;

namespace Yukti.Infrastructure.Tests;

/// <summary>
/// Serializes every test that attaches a MeterListener to the shared,
/// process-wide static OrchestrationTelemetry.Meter and asserts an exact
/// measurement count — see OpenAiProviderTests' doc comment for the real
/// failure this fixes. Add any future test with the same shape here too.
/// </summary>
[CollectionDefinition(Name)]
public sealed class AiProviderTelemetryCollection
{
    public const string Name = "AiProviderTelemetry";
}
