using System.Globalization;
using Microsoft.Extensions.Configuration;
using Yukti.Application.Ai;

namespace Yukti.Infrastructure.Ai;

/// <summary>
/// Token-economy spec §R3/R4: reads <c>Ai:Capabilities:&lt;capability&gt;:{MaxInputChars,MaxOutputTokens,
/// Temperature}</c> and <c>Ai:MaxValidationRetries</c> from the real configuration tree (appsettings,
/// environment, Vault-backed providers — whatever's already wired into <see cref="IConfiguration"/>),
/// falling back to <see cref="AiCapabilityOptions.Default"/> / 1 retry for anything unconfigured. Plain
/// indexer reads with manual parsing, the same style <see cref="GeminiProvider"/>/<see cref="FilePromptLoader"/>
/// already use — this project has no <c>Microsoft.Extensions.Configuration.Binder</c> reference, and
/// adding one needs an ADR + owner approval (CLAUDE.md §4), not a one-off import for this.
/// </summary>
public sealed class ConfigurationAiCapabilityOptionsProvider : IAiCapabilityOptionsProvider
{
    private const int DefaultMaxValidationRetries = 1;

    private readonly IConfiguration _config;

    public ConfigurationAiCapabilityOptionsProvider(IConfiguration config) => _config = config;

    public AiCapabilityOptions GetFor(string capability)
    {
        var prefix = $"Ai:Capabilities:{capability}:";
        return new AiCapabilityOptions(
            MaxInputChars: ReadInt(prefix + "MaxInputChars", AiCapabilityOptions.Default.MaxInputChars),
            MaxOutputTokens: ReadInt(prefix + "MaxOutputTokens", AiCapabilityOptions.Default.MaxOutputTokens),
            Temperature: ReadDouble(prefix + "Temperature", AiCapabilityOptions.Default.Temperature));
    }

    public int MaxValidationRetries => ReadInt("Ai:MaxValidationRetries", DefaultMaxValidationRetries);

    private int ReadInt(string key, int fallback) =>
        int.TryParse(_config[key], NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : fallback;

    private double ReadDouble(string key, double fallback) =>
        double.TryParse(_config[key], NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : fallback;
}
