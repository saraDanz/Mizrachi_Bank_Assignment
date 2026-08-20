using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Mizrachi.Api.Errors;
using Mizrachi.Api.Middleware;
using Mizrachi.Api.Swagger;
using Mizrachi.Infrastructure;
using Mizrachi.Infrastructure.Persistence;
using Mizrachi.Infrastructure.Security;

namespace Mizrachi.Api
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            builder.Services.AddControllers();

            // Model-binding failures otherwise emit ValidationProblemDetails, a second error
            // shape alongside ours. One shape for every failure (FR-4.2).
            builder.Services.Configure<ApiBehaviorOptions>(options =>
                options.InvalidModelStateResponseFactory = context => new BadRequestObjectResult(
                    ApiProblemDetails.Invalid(
                        context.HttpContext,
                        "request_invalid",
                        "The request is not valid.")));

            builder.Services.AddApiRateLimiting();

            // The only call into Infrastructure. Which store backs the API is decided by
            // configuration inside here, never by a code change (NFR-1.3). The environment is
            // passed because one behaviour turns on it: a missing signing key is generated in
            // memory in Development and fails startup everywhere else (NFR-1.5).
            builder.Services.AddInfrastructure(
                builder.Configuration,
                builder.Environment.IsDevelopment());

            AddAuthentication(builder);

            // Registered only in Development, so the document and its UI are absent outside it
            // rather than merely unreachable (NFR-2.7).
            if (builder.Environment.IsDevelopment())
            {
                builder.Services.AddEndpointsApiExplorer();
                builder.Services.AddSwaggerGen(swagger =>
                {
                    // Http/bearer rather than ApiKey, so the Authorize box takes the raw token and
                    // Swagger adds the "Bearer " prefix itself. No default, no example value: a
                    // pre-filled token in a committed file is a committed credential (NFR-2.6).
                    swagger.AddSecurityDefinition(
                        BearerSecurityOperationFilter.SchemeId,
                        new OpenApiSecurityScheme
                        {
                            Name = "Authorization",
                            Type = SecuritySchemeType.Http,
                            Scheme = "bearer",
                            BearerFormat = "JWT",
                            In = ParameterLocation.Header,
                            Description =
                                "Paste the token from POST /api/users/validate. Nothing is stored "
                                + "or pre-filled, and the token is only accepted by endpoints that "
                                + "require it."
                        });

                    // Per-operation, not global: create and validate are anonymous by requirement
                    // and must not show a padlock.
                    swagger.OperationFilter<BearerSecurityOperationFilter>();
                });
            }

            var app = builder.Build();

            // A file-backed provider prepares its store before the first request rather than on
            // it (NFR-1.4). The interface is provider-agnostic, so no EF type is named here.
            using (var scope = app.Services.CreateScope())
            {
                var initializer = scope.ServiceProvider.GetService<IDatabaseInitializer>();
                initializer?.InitializeAsync(CancellationToken.None).GetAwaiter().GetResult();
            }

            // Registered and mapped only in Development, so the interactive documentation is
            // absent outside it rather than merely unreachable (NFR-2.7).
            if (app.Environment.IsDevelopment())
            {
                app.UseSwagger();
                app.UseSwaggerUI();
            }

            // First in the pipeline, so even a failure inside another middleware is answered in
            // the one error shape and carries a correlation id.
            app.UseMiddleware<ExceptionHandlingMiddleware>();
            app.UseMiddleware<CorrelationIdMiddleware>();

            if (!app.Environment.IsDevelopment())
            {
                app.UseHsts();
            }

            app.UseHttpsRedirection();

            app.UseRateLimiter();

            app.UseAuthentication();
            app.UseAuthorization();

            app.MapControllers();

            app.Run();
        }

        /// <remarks>
        /// Every validation flag is on and the algorithm is pinned, so a token signed with
        /// <c>alg: none</c>, or with the wrong key, issuer or audience, is rejected rather than
        /// trusted. <c>MapInboundClaims</c> is off so the subject stays <c>sub</c> instead of
        /// being renamed — the delete endpoint reads that claim to decide ownership.
        /// </remarks>
        private static void AddAuthentication(WebApplicationBuilder builder)
        {
            var requireHttpsMetadata = !builder.Environment.IsDevelopment();

            builder.Services
                .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
                .AddJwtBearer();

            // JwtOptions is resolved from the container when the bearer options are built, not
            // read out of configuration here. Reading it at registration time would capture
            // whatever configuration existed at that moment and ignore any source added later,
            // and it would bypass the validation that makes a missing signing key a startup
            // failure rather than a malformed key at first request (NFR-1.4, NFR-2.6).
            builder.Services
                .AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
                .Configure<IOptions<JwtOptions>>((bearer, jwtOptions) =>
                {
                    var jwt = jwtOptions.Value;

                    bearer.MapInboundClaims = false;
                    bearer.RequireHttpsMetadata = requireHttpsMetadata;

                    bearer.TokenValidationParameters = new TokenValidationParameters
                    {
                        ValidateIssuer = true,
                        ValidateAudience = true,
                        ValidateLifetime = true,
                        ValidateIssuerSigningKey = true,
                        ValidIssuer = jwt.Issuer,
                        ValidAudience = jwt.Audience,
                        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),
                        ValidAlgorithms = new[] { SecurityAlgorithms.HmacSha256 },
                        ClockSkew = TimeSpan.FromSeconds(30)
                    };
                });

            builder.Services.AddAuthorization();
        }
    }
}
