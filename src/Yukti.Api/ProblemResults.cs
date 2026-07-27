using Microsoft.AspNetCore.Mvc;

namespace Yukti.Api;

/// <summary>
/// FR-API-03: every error response follows RFC 7807 with a correlationId
/// tying back to the full internal trace. correlationId is
/// HttpContext.TraceIdentifier — ASP.NET Core already generates one per
/// request and threads it through its own internal logging, so this reuses
/// that instead of minting a second, redundant identifier.
/// </summary>
public static class ProblemResults
{
    public static IResult NotFound(HttpContext context, string detail) =>
        Build(context, StatusCodes.Status404NotFound, "Not Found", detail);

    public static IResult BadRequest(HttpContext context, string detail) =>
        Build(context, StatusCodes.Status400BadRequest, "Bad Request", detail);

    public static IResult Unauthorized(HttpContext context, string detail) =>
        Build(context, StatusCodes.Status401Unauthorized, "Unauthorized", detail);

    public static IResult Forbidden(HttpContext context, string detail) =>
        Build(context, StatusCodes.Status403Forbidden, "Forbidden", detail);

    private static IResult Build(HttpContext context, int status, string title, string detail)
    {
        var problem = new ProblemDetails { Status = status, Title = title, Detail = detail };
        problem.Extensions["correlationId"] = context.TraceIdentifier;
        return Results.Json(problem, statusCode: status, contentType: "application/problem+json");
    }

    /// <summary>Used by the global exception-handling middleware, which has no minimal-API IResult pipeline to return into.</summary>
    public static async Task WriteAsync(HttpContext context, int status, string title, string detail, CancellationToken ct)
    {
        var problem = new ProblemDetails { Status = status, Title = title, Detail = detail };
        problem.Extensions["correlationId"] = context.TraceIdentifier;
        context.Response.StatusCode = status;
        context.Response.ContentType = "application/problem+json";
        await context.Response.WriteAsJsonAsync(problem, ct);
    }
}
