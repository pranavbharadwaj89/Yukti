using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Xunit;
using Yukti.Application.Ai;
using Yukti.Infrastructure.Ai;

namespace Yukti.Infrastructure.Tests;

/// <summary>Token-economy spec §R4: per-capability caps come from configuration, with <see cref="AiCapabilityOptions.Default"/> as the only fallback literal anywhere.</summary>
public sealed class AiCapabilityOptionsTests
{
    private static ConfigurationAiCapabilityOptionsProvider BuildProvider(Dictionary<string, string?> values) =>
        new(new ConfigurationBuilder().AddInMemoryCollection(values).Build());

    [Fact]
    public void Reads_a_configured_capabilitys_caps_from_Ai_Capabilities_section()
    {
        var provider = BuildProvider(new()
        {
            ["Ai:Capabilities:Analyst:MaxInputChars"] = "6000",
            ["Ai:Capabilities:Analyst:MaxOutputTokens"] = "1024",
            ["Ai:Capabilities:Analyst:Temperature"] = "0",
        });

        var options = provider.GetFor("Analyst");

        Assert.Equal(6000, options.MaxInputChars);
        Assert.Equal(1024, options.MaxOutputTokens);
        Assert.Equal(0, options.Temperature);
    }

    [Fact]
    public void Falls_back_to_Default_for_a_capability_with_no_configured_section()
    {
        var provider = BuildProvider(new());

        var options = provider.GetFor("some-future-capability");

        Assert.Equal(AiCapabilityOptions.Default, options);
    }

    [Fact]
    public void Falls_back_to_1_retry_when_Ai_MaxValidationRetries_is_unconfigured()
    {
        var provider = BuildProvider(new());
        Assert.Equal(1, provider.MaxValidationRetries);
    }

    [Fact]
    public void Reads_a_configured_MaxValidationRetries()
    {
        var provider = BuildProvider(new() { ["Ai:MaxValidationRetries"] = "3" });
        Assert.Equal(3, provider.MaxValidationRetries);
    }

    [Fact]
    public void No_production_source_outside_AiCapabilityOptions_assigns_a_MaxOutputTokens_literal()
    {
        // §R4: "No MaxOutputTokens literal outside configuration" — AiCapabilityOptions.Default
        // is the one allowed fallback number; everything else must read it through
        // IAiCapabilityOptionsProvider, never hardcode its own.
        var srcDir = Path.Combine(FindRepoRoot(), "src");
        var allowedFile = Path.Combine(srcDir, "Yukti.Application", "Ai", "AiCapabilityOptions.cs");
        var pattern = new Regex(@"MaxOutputTokens\s*[:=]\s*\d", RegexOptions.IgnoreCase);

        var offending = Directory.EnumerateFiles(srcDir, "*.cs", SearchOption.AllDirectories)
            .Where(f => !string.Equals(f, allowedFile, StringComparison.OrdinalIgnoreCase))
            .Where(f => pattern.IsMatch(File.ReadAllText(f)))
            .ToList();

        Assert.Empty(offending);
    }

    private static string FindRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "Yukti.sln")))
            dir = Path.GetDirectoryName(dir);
        return dir ?? throw new InvalidOperationException("Could not locate repo root (Yukti.sln) from " + AppContext.BaseDirectory);
    }
}
