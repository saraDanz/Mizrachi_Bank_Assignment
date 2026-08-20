using Mizrachi.Api.Errors;

namespace Mizrachi.Api.Middleware;

/// <summary>
/// Gives every request an identifier, echoes it on the response, and puts it in the log scope,
/// so a caller holding a response can be matched to the server-side entries (FR-4.4).
/// </summary>
public sealed class CorrelationIdMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<CorrelationIdMiddleware> _logger;

    public CorrelationIdMiddleware(RequestDelegate next, ILogger<CorrelationIdMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = ReadOrCreate(context);

        context.Items[ApiProblemDetails.CorrelationIdExtension] = correlationId;

        context.Response.OnStarting(() =>
        {
            context.Response.Headers[ApiProblemDetails.CorrelationIdHeader] = correlationId;
            return Task.CompletedTask;
        });

        using (_logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = correlationId }))
        {
            await _next(context);
        }
    }

    /// <remarks>
    /// A caller-supplied identifier is accepted so a client can trace a request across systems,
    /// but it is length-capped and stripped of anything outside a conservative set: it reaches
    /// the logs, and an unbounded or control-character-bearing value would be a way to forge
    /// log structure.
    /// </remarks>
    private static string ReadOrCreate(HttpContext context)
    {
        var supplied = context.Request.Headers[ApiProblemDetails.CorrelationIdHeader].ToString();

        if (string.IsNullOrWhiteSpace(supplied) || supplied.Length > 64)
        {
            return Guid.NewGuid().ToString("N");
        }

        foreach (var character in supplied)
        {
            var permitted = character is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9'
                or '-' or '_';

            if (!permitted)
            {
                return Guid.NewGuid().ToString("N");
            }
        }

        return supplied;
    }
}
