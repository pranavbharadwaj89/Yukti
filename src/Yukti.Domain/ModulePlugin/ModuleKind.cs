namespace Yukti.Domain.ModulePlugin;

/// <summary>
/// Deliberately not a closed C# enum. A closed enum would hardcode the six
/// built-in kinds at compile time and structurally prevent marketplace
/// modules from ever being first-class ModuleKind values — directly
/// violating Architecture Principle 20.3 (every module treated identically).
/// The named static instances give built-in modules ergonomic, typo-proof
/// references; Custom() keeps the type open for extension.
/// (Volume 1 Part II §11.2)
/// </summary>
public readonly record struct ModuleKind
{
    public string Value { get; }
    private ModuleKind(string value) => Value = value;

    public static readonly ModuleKind Api = new("api");
    public static readonly ModuleKind Web = new("web");
    public static readonly ModuleKind Mobile = new("mobile");
    public static readonly ModuleKind DesktopUi = new("ui");
    public static readonly ModuleKind Logs = new("logs");
    public static readonly ModuleKind Ai = new("ai");

    public static ModuleKind Custom(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new SharedKernel.DomainException("ModuleKind value cannot be empty.");
        if (value != value.ToLowerInvariant())
            throw new SharedKernel.DomainException($"Invalid ModuleKind '{value}': must be lowercase.");
        if (value.Any(char.IsWhiteSpace))
            throw new SharedKernel.DomainException($"Invalid ModuleKind '{value}': must not contain whitespace.");
        return new ModuleKind(value);
    }

    public override string ToString() => Value;
}
