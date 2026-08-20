using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Mizrachi.Application.Abstractions;
using Mizrachi.Application.UseCases;
using Mizrachi.Domain;
using Mizrachi.Infrastructure.Persistence;
using Mizrachi.Infrastructure.Security;
using Mizrachi.Infrastructure.Time;

namespace Mizrachi.Infrastructure;

/// <summary>
/// The composition root. This is the only place a persistence provider is named, and the only
/// place the API project touches the Infrastructure assembly.
/// </summary>
public static class InfrastructureRegistration
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services
            .AddOptions<JwtOptions>()
            .Bind(configuration.GetSection(JwtOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services
            .AddOptions<PersistenceOptions>()
            .Bind(configuration.GetSection(PersistenceOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddSingleton<IPasswordHasher, AspNetPasswordHasher>();
        services.AddSingleton<IPasswordDenyList, EmbeddedPasswordDenyList>();
        services.AddSingleton<ITokenIssuer, JwtTokenIssuer>();
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IIdGenerator, GuidIdGenerator>();
        services.AddSingleton<ISecurityEventLog, LoggingSecurityEventLog>();

        services.AddSingleton<PasswordPolicy>();
        services.AddSingleton<UserNamePolicy>();

        services.AddSingleton<CreateUserService>();
        services.AddSingleton<ValidateUserService>();
        services.AddSingleton<DeleteUserService>();

        AddPersistence(services, configuration);

        return services;
    }

    /// <remarks>
    /// The provider is read and checked here, while the host is being built, so an unrecognised
    /// value stops startup with a message naming the valid ones — rather than surfacing as a
    /// missing-service error on the first request (NFR-1.4).
    /// </remarks>
    private static void AddPersistence(IServiceCollection services, IConfiguration configuration)
    {
        var provider = configuration
            .GetSection(PersistenceOptions.SectionName)[nameof(PersistenceOptions.Provider)];

        switch (provider)
        {
            case PersistenceOptions.Providers.InMemory:
                services.AddSingleton<IUserRepository, InMemoryUserRepository>();
                break;

            default:
                throw new InvalidOperationException(
                    $"Persistence:Provider is '{provider ?? "(not set)"}'. " +
                    $"Valid values are: {PersistenceOptions.Providers.InMemory}.");
        }
    }
}
