using Microsoft.AspNetCore.Authorization;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Mizrachi.Api.Swagger;

/// <summary>
/// Attaches the bearer requirement to the operations that actually enforce it.
/// </summary>
/// <remarks>
/// Deliberately an operation filter rather than a global <c>AddSecurityRequirement</c>. A blanket
/// requirement puts a padlock on create and validate too, which are anonymous by requirement
/// (FR-1.10, FR-3.7) — and a document that claims those need a token contradicts the API and
/// misleads the reviewer at the first endpoint they try.
///
/// The decision is read from endpoint metadata rather than from attributes on the method, so a
/// controller-level <c>[Authorize]</c> with a method-level <c>[AllowAnonymous]</c> resolves the
/// same way the authorization middleware resolves it.
/// </remarks>
internal sealed class BearerSecurityOperationFilter : IOperationFilter
{
    internal const string SchemeId = "Bearer";

    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        var metadata = context.ApiDescription.ActionDescriptor.EndpointMetadata;

        if (metadata.OfType<IAllowAnonymous>().Any())
        {
            return;
        }

        if (!metadata.OfType<IAuthorizeData>().Any())
        {
            return;
        }

        operation.Security =
        [
            new OpenApiSecurityRequirement
            {
                [new OpenApiSecurityScheme
                {
                    Reference = new OpenApiReference
                    {
                        Type = ReferenceType.SecurityScheme,
                        Id = SchemeId
                    }
                }] = Array.Empty<string>()
            }
        ];
    }
}
