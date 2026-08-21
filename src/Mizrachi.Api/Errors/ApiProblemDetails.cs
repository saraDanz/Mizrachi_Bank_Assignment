using Microsoft.AspNetCore.Mvc;

namespace Mizrachi.Api.Errors;

/// <summary>
/// One error shape for every failure (FR-4.2), carrying the correlation identifier that matches
/// the server log (FR-4.4).
/// </summary>
public static class ApiProblemDetails
{
    public const string CorrelationIdHeader = "X-Correlation-Id";

    public const string CorrelationIdExtension = "correlationId";

    /// <summary>
    /// The body returned for every failed validation, whatever the reason. It names no account
    /// and reflects nothing the caller submitted, so an unknown username and a wrong password
    /// are answered identically (FR-3.5).
    /// </summary>
    public static ProblemDetails Unauthorized(HttpContext context) =>
        Create(context, StatusCodes.Status401Unauthorized, "Unauthorized", "Invalid credentials.");

    public static ProblemDetails Forbidden(HttpContext context) =>
        Create(context, StatusCodes.Status403Forbidden, "Forbidden", "You may only act on your own account.");

    public static ProblemDetails NotFound(HttpContext context) =>
        Create(context, StatusCodes.Status404NotFound, "Not Found", "The account does not exist.");

    public static ProblemDetails Conflict(HttpContext context, string detail) =>
        Create(context, StatusCodes.Status409Conflict, "Conflict", detail);

    public static ProblemDetails Invalid(HttpContext context, string rule, string detail)
    {
        var problem = Create(context, StatusCodes.Status400BadRequest, "Bad Request", detail);
        problem.Extensions["rule"] = rule;
        return problem;
    }

    public static ProblemDetails Create(HttpContext context, int status, string title, string detail)
    {
        var problem = new ProblemDetails
        {
            Status = status,
            Title = title,
            Detail = detail,
            Instance = context.Request.Path
        };

        problem.Extensions[CorrelationIdExtension] = CorrelationId(context);

        return problem;
    }

    public static string CorrelationId(HttpContext context) =>
        context.Items.TryGetValue(CorrelationIdExtension, out var value) && value is string correlationId
            ? correlationId
            : context.TraceIdentifier;
}
