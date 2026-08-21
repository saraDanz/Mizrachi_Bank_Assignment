using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Mizrachi.Api.Errors;

namespace Mizrachi.Api.Middleware;

/// <summary>
/// Converts an unhandled exception into the one error shape (FR-4.2).
/// </summary>
/// <remarks>
/// Outside Development the response carries no exception type, message, stack frame or
/// datastore detail — only a generic statement and the correlation id, which is what ties the
/// caller's report to the full detail in the server log (FR-4.3). The exception itself is
/// logged, never returned.
/// </remarks>
public sealed class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;
    private readonly IHostEnvironment _environment;

    public ExceptionHandlingMiddleware(
        RequestDelegate next,
        ILogger<ExceptionHandlingMiddleware> logger,
        IHostEnvironment environment)
    {
        _next = next;
        _logger = logger;
        _environment = environment;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception exception)
        {
            var correlationId = ApiProblemDetails.CorrelationId(context);

            // The request body is not logged: it holds credentials on two of three endpoints
            // (NFR-2.3).
            _logger.LogError(
                exception,
                "Unhandled exception. CorrelationId={CorrelationId} Path={Path}",
                correlationId,
                context.Request.Path);

            if (context.Response.HasStarted)
            {
                throw;
            }

            var problem = ApiProblemDetails.Create(
                context,
                StatusCodes.Status500InternalServerError,
                "Internal Server Error",
                _environment.IsDevelopment()
                    ? exception.ToString()
                    : "An unexpected error occurred. Quote the correlation id when reporting it.");

            context.Response.Clear();
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            context.Response.ContentType = "application/problem+json";
            context.Response.Headers[ApiProblemDetails.CorrelationIdHeader] = correlationId;

            await context.Response.WriteAsync(JsonSerializer.Serialize(problem, SerializerOptions));
        }
    }

    private static readonly JsonSerializerOptions SerializerOptions =
        new(JsonSerializerDefaults.Web);
}
