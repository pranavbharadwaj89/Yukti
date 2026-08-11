using Yukti.Domain.SharedKernel;

namespace Yukti.Domain.ApiTesting;

/// <summary>
/// Aggregate root of the API Testing context — a named, durable, reusable
/// group of saved ApiRequests (Postman's "Collection"). Distinct from
/// FlowAuthoring.Flow deliberately: a Flow is an executable, versioned,
/// publish-then-run pipeline; an ApiCollection is just organized storage
/// for request definitions an author edits freely and sends ad hoc — there
/// is no publish step and no version history, matching Explorer's actual
/// UX (edit, save, send — never "publish this collection").
/// </summary>
public sealed class ApiCollection : AggregateRoot<ApiCollectionId>
{
    public TenantId TenantId { get; private set; }
    public string Name { get; private set; }
    public string? Description { get; private set; }

    private readonly List<ApiRequest> _requests = new();
    public IReadOnlyList<ApiRequest> Requests => _requests.AsReadOnly();

    private ApiCollection(ApiCollectionId id, string name, string? description, TenantId tenantId) : base(id)
    {
        Name = name;
        Description = description;
        TenantId = tenantId;
    }

    public static ApiCollection Create(string name, string? description, TenantId tenantId)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("ApiCollection name cannot be empty.");
        return new ApiCollection(ApiCollectionId.New(), name, description, tenantId);
    }

    public void Rename(string name, string? description)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("ApiCollection name cannot be empty.");
        Name = name;
        Description = description;
    }

    public ApiRequest AddRequest(
        string name,
        string method,
        string url,
        IReadOnlyDictionary<string, object?> headers,
        IReadOnlyDictionary<string, object?> queryParams,
        object? body,
        object? assertions)
    {
        var request = new ApiRequest(ApiRequestId.New(), name, method, url, headers, queryParams, body, assertions, _requests.Count);
        _requests.Add(request);
        return request;
    }

    public void UpdateRequest(
        ApiRequestId requestId,
        string name,
        string method,
        string url,
        IReadOnlyDictionary<string, object?> headers,
        IReadOnlyDictionary<string, object?> queryParams,
        object? body,
        object? assertions)
    {
        var request = _requests.FirstOrDefault(r => r.Id == requestId)
            ?? throw new DomainException($"Request {requestId} not found in collection {Id}.");
        request.Update(name, method, url, headers, queryParams, body, assertions);
    }

    public void RemoveRequest(ApiRequestId requestId)
    {
        var request = _requests.FirstOrDefault(r => r.Id == requestId);
        if (request is not null)
            _requests.Remove(request);
    }
}
