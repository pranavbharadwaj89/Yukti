using System.Reflection;
using Yukti.Domain.Auditing;

namespace Yukti.Application.Auditing;

/// <summary>
/// FR-AUDIT-02: reflects over a command's public properties to build audit
/// metadata, redacting any property marked [SensitiveValue] before an
/// AuditEntry is ever constructed — the raw value never reaches the
/// metadata dictionary, let alone the audit store.
///
/// D6-T3a: [SensitiveValue] is all-or-nothing per property — the right tool
/// for a single scalar like a password field, but AddFlowStepCommand.Params
/// is a whole dictionary where only some entries (a header named
/// Authorization, say) are ever sensitive; blanket-redacting the property
/// would also hide every harmless param. Confirmed this was a real, live
/// gap before fixing it: no [SensitiveValue] on Params, so the raw
/// dictionary — headers included — was reaching the audit store untouched.
/// Any dictionary-shaped property (not just Params by name, so a future
/// command with the same shape gets this for free) goes through the one
/// shared redactor instead.
/// </summary>
public static class AuditMetadataBuilder
{
    private const string RedactedPlaceholder = "***REDACTED***";

    public static IReadOnlyDictionary<string, object?> Capture<TCommand>(TCommand command)
    {
        var metadata = new Dictionary<string, object?>();
        foreach (var property in typeof(TCommand).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.GetCustomAttribute<SensitiveValueAttribute>() is not null)
            {
                metadata[property.Name] = RedactedPlaceholder;
                continue;
            }

            var value = property.GetValue(command);
            metadata[property.Name] = value is IReadOnlyDictionary<string, object?> dict
                ? Yukti.Domain.Auditing.SecretRedactor.Redact(dict)
                : value;
        }
        return metadata;
    }
}
