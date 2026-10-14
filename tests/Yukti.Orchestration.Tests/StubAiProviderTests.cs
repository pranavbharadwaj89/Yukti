using Xunit;
using Yukti.Application.Ai;
using Yukti.Infrastructure.InMemory.Ai;

namespace Yukti.Orchestration.Tests;

/// <summary>D5-T1: the fixture-driven IAiProvider every other AI test (and, later, every Analyst/Coder test) runs against instead of the network.</summary>
public sealed class StubAiProviderTests
{
    [Fact]
    public async Task Returns_the_fixture_registered_for_the_requests_capability()
    {
        var provider = new StubAiProvider();
        provider.SetFixture("analyst", @"{""summary"":""checks login""}");
        provider.SetFixture("coder", @"{""steps"":[]}");

        var response = await provider.GenerateStructuredAsync(
            new StructuredGenerationRequest("analyst", "sys", "user", "{}"), CancellationToken.None);

        Assert.Equal(@"{""summary"":""checks login""}", response.RawText);
        Assert.Equal("checks login", response.Parsed!["summary"]!.GetValue<string>());
    }

    [Fact]
    public async Task A_capability_with_no_registered_fixture_raises_AiUnavailableException()
    {
        var provider = new StubAiProvider();

        var ex = await Assert.ThrowsAsync<AiUnavailableException>(
            () => provider.GenerateStructuredAsync(new StructuredGenerationRequest("coder", "sys", "user", "{}"), CancellationToken.None));

        Assert.Contains("coder", ex.Message);
    }

    [Fact]
    public async Task A_fixture_that_isnt_valid_JSON_returns_null_Parsed_without_throwing()
    {
        var provider = new StubAiProvider();
        provider.SetFixture("coder", "not json");

        var response = await provider.GenerateStructuredAsync(
            new StructuredGenerationRequest("coder", "sys", "user", "{}"), CancellationToken.None);

        Assert.Equal("not json", response.RawText);
        Assert.Null(response.Parsed);
    }

    [Fact]
    public void Id_is_stub_never_confused_with_a_real_provider()
    {
        Assert.Equal("stub", new StubAiProvider().Id);
    }
}
