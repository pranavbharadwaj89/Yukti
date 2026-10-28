using Yukti.Domain.SharedKernel;

namespace Yukti.Domain.ApiTesting;

/// <summary>
/// API Testing Module Spec, API-T9: a tenant-scoped, reusable base configuration (base URL,
/// common headers, an auth spec, a default timeout) a flow step references by `templateId` and
/// can override any field of. "Versioned" here means a visible, incrementing counter on every
/// edit (the same shape a reader expects after seeing Flow's own Version) — not Flow's heavier
/// immutable-version-family branching (FamilyId + CreateNewVersion), which exists specifically
/// because a Flow's exact historical version must keep resolving for past FlowRuns. A
/// RequestTemplate has no such downstream reference; it's resolved fresh on every run (so
/// "changing the template updates every flow that uses it, with no flow edits" holds), and
/// mutating it in place with a version counter is proportionate to that, matching ApiCollection's
/// own "disposable, editable resource" shape more than Flow's.
///
/// Headers/Auth are the same loosely-typed, validated-at-use shapes ApiRequest's own
/// Headers/Assertions already are — a template's `auth` is exactly the `auth` object
/// `ApiModule.BuildAuth` already understands (API-T4/T5), never re-validated here.
/// </summary>
public sealed class RequestTemplate : AggregateRoot<RequestTemplateId>
{
    public TenantId TenantId { get; private set; }
    public string Name { get; private set; }
    public string? BaseUrl { get; private set; }
    public IReadOnlyDictionary<string, object?> Headers { get; private set; }
    public object? Auth { get; private set; }
    public int? DefaultTimeoutMs { get; private set; }
    public int Version { get; private set; }

    private RequestTemplate(RequestTemplateId id, TenantId tenantId, string name) : base(id)
    {
        TenantId = tenantId;
        Name = name;
        Headers = new Dictionary<string, object?>();
        Version = 1;
    }

    public static RequestTemplate Create(
        string name, TenantId tenantId, string? baseUrl,
        IReadOnlyDictionary<string, object?>? headers, object? auth, int? defaultTimeoutMs)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("RequestTemplate name cannot be empty.");

        return new RequestTemplate(RequestTemplateId.New(), tenantId, name)
        {
            BaseUrl = baseUrl,
            Headers = headers ?? new Dictionary<string, object?>(),
            Auth = auth,
            DefaultTimeoutMs = defaultTimeoutMs,
        };
    }

    public void Update(string name, string? baseUrl, IReadOnlyDictionary<string, object?>? headers, object? auth, int? defaultTimeoutMs)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("RequestTemplate name cannot be empty.");

        Name = name;
        BaseUrl = baseUrl;
        Headers = headers ?? new Dictionary<string, object?>();
        Auth = auth;
        DefaultTimeoutMs = defaultTimeoutMs;
        Version++;
    }
}
