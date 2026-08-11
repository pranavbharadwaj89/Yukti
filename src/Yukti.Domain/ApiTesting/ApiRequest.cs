using Yukti.Domain.SharedKernel;

namespace Yukti.Domain.ApiTesting;

/// <summary>
/// A saved, reusable API request within an ApiCollection — Explorer's unit
/// of persistence. Headers/QueryParams/Body/Assertions are deliberately
/// loosely-typed (same convention as FlowAuthoring.FlowStep.Params):
/// ApiRequest never validates the assert array's shape — that happens for
/// free at send time via AssertionParamMapper inside ApiModule.Run, the
/// same "don't validate until Publish/Run" deferral FlowStep already
/// relies on for its own Params.
/// </summary>
public sealed class ApiRequest : Entity<ApiRequestId>
{
    public string Name { get; private set; }
    public string Method { get; private set; }
    public string Url { get; private set; }
    public IReadOnlyDictionary<string, object?> Headers { get; private set; }
    public IReadOnlyDictionary<string, object?> QueryParams { get; private set; }
    public object? Body { get; private set; }
    public object? Assertions { get; private set; }
    public int Order { get; private set; }

    public ApiRequest(
        ApiRequestId id,
        string name,
        string method,
        string url,
        IReadOnlyDictionary<string, object?> headers,
        IReadOnlyDictionary<string, object?> queryParams,
        object? body,
        object? assertions,
        int order) : base(id)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("ApiRequest name cannot be empty.");
        if (string.IsNullOrWhiteSpace(url))
            throw new DomainException("ApiRequest url cannot be empty.");

        Name = name;
        Method = method;
        Url = url;
        Headers = headers;
        QueryParams = queryParams;
        Body = body;
        Assertions = assertions;
        Order = order;
    }

    internal void Update(
        string name,
        string method,
        string url,
        IReadOnlyDictionary<string, object?> headers,
        IReadOnlyDictionary<string, object?> queryParams,
        object? body,
        object? assertions)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("ApiRequest name cannot be empty.");
        if (string.IsNullOrWhiteSpace(url))
            throw new DomainException("ApiRequest url cannot be empty.");

        Name = name;
        Method = method;
        Url = url;
        Headers = headers;
        QueryParams = queryParams;
        Body = body;
        Assertions = assertions;
    }
}
