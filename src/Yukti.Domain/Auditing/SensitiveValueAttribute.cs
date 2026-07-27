namespace Yukti.Domain.Auditing;

/// <summary>
/// FR-AUDIT-02 (Volume 1 Part IV §27.4): marks a command property whose
/// value must never appear in derived audit metadata — credentials,
/// tokens, passwords. AuditMetadataBuilder (Yukti.Application.Auditing)
/// redacts any property carrying this attribute before an AuditEntry is
/// ever constructed, so the raw value never reaches the audit store at all.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class SensitiveValueAttribute : Attribute
{
}
