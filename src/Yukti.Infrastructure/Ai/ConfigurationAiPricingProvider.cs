using System.Globalization;
using Microsoft.Extensions.Configuration;
using Yukti.Application.Ai;

namespace Yukti.Infrastructure.Ai;

/// <summary>
/// D5-T9/§R5: <c>Ai:Pricing:&lt;provider&gt;:{InputPer1M,OutputPer1M}</c> — plain indexer reads with
/// manual parsing, the same style <see cref="ConfigurationAiCapabilityOptionsProvider"/> already
/// uses (no <c>Microsoft.Extensions.Configuration.Binder</c> reference in this project; adding one
/// needs an ADR + owner approval, CLAUDE.md §4, not a one-off import for two numbers).
/// </summary>
public sealed class ConfigurationAiPricingProvider : IAiPricingProvider
{
    private readonly IConfiguration _config;

    public ConfigurationAiPricingProvider(IConfiguration config) => _config = config;

    public AiPricing GetFor(string provider)
    {
        var prefix = $"Ai:Pricing:{provider}:";
        var input = ReadDecimal(prefix + "InputPer1M");
        var output = ReadDecimal(prefix + "OutputPer1M");
        return input is null && output is null ? AiPricing.Zero : new AiPricing(input ?? 0m, output ?? 0m);
    }

    private decimal? ReadDecimal(string key) =>
        decimal.TryParse(_config[key], NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : null;
}
