namespace Yukti.Application.Ai;

/// <summary>
/// D5-T2: raised by every <see cref="IAiAssistantService"/> method when
/// `Ai:Enabled` is false — mapped to a 503 RFC 7807 response by the API's
/// global exception middleware. Distinct from <see cref="AiUnavailableException"/>
/// (a real provider outage/timeout): this one means AI was deliberately
/// turned off for this deployment, not that something went wrong.
/// </summary>
public sealed class AiDisabledException : Exception
{
    public AiDisabledException() : base("AI generation is disabled for this deployment.") { }
}
