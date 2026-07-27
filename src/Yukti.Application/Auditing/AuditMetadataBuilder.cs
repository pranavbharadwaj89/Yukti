using System.Reflection;
using Yukti.Domain.Auditing;

namespace Yukti.Application.Auditing;

/// <summary>
/// FR-AUDIT-02: reflects over a command's public properties to build audit
/// metadata, redacting any property marked [SensitiveValue] before an
/// AuditEntry is ever constructed — the raw value never reaches the
/// metadata dictionary, let alone the audit store.
/// </summary>
public static class AuditMetadataBuilder
{
    private const string RedactedPlaceholder = "***REDACTED***";

    public static IReadOnlyDictionary<string, object?> Capture<TCommand>(TCommand command)
    {
        var metadata = new Dictionary<string, object?>();
        foreach (var property in typeof(TCommand).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            metadata[property.Name] = property.GetCustomAttribute<SensitiveValueAttribute>() is not null
                ? RedactedPlaceholder
                : property.GetValue(command);
        }
        return metadata;
    }
}
