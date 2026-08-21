using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Mizrachi.Api.Errors;

namespace Mizrachi.Api;

/// <summary>
/// Rate limiting for the two anonymous endpoints (NFR-2.4, OQ-3).
/// </summary>
/// <remarks>
/// Partitioned by client address, not by username. Partitioning by username would let an
/// attacker who knows a name exhaust that account's allowance and lock its owner out — the
/// denial-of-service that REQUIREMENTS §4.6 deliberately avoided by choosing rate limiting over
/// account lockout. Per-address is weak against a distributed attack, which is why §4.5 and
/// §4.9 name multi-factor authentication and breached-password screening as the controls that
/// actually carry the weight.
/// </remarks>
public static class RateLimitPolicies
{
    public const string Authentication = "authentication";

    public const string Registration = "registration";

    public static IServiceCollection AddApiRateLimiting(this IServiceCollection services) =>
        services.AddRateLimiter(options =>
        {
            options.AddPolicy(Authentication, context => FixedWindowFor(context, permitLimit: 10));
            options.AddPolicy(Registration, context => FixedWindowFor(context, permitLimit: 5));

            options.OnRejected = async (context, cancellationToken) =>
            {
                context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                context.HttpContext.Response.Headers.RetryAfter = "60";
                context.HttpContext.Response.ContentType = "application/problem+json";

                var problem = ApiProblemDetails.Create(
                    context.HttpContext,
                    StatusCodes.Status429TooManyRequests,
                    "Too Many Requests",
                    "Too many attempts. Try again shortly.");

                await context.HttpContext.Response.WriteAsJsonAsync(problem, cancellationToken);
            };
        });

    private static RateLimitPartition<string> FixedWindowFor(HttpContext context, int permitLimit) =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = permitLimit,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst
            });
}
