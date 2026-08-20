using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
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

            case PersistenceOptions.Providers.Sqlite:
                AddSqlite(services, configuration);
                break;

            default:
                throw new InvalidOperationException(
                    $"Persistence:Provider is '{provider ?? "(not set)"}'. Valid values are: " +
                    $"{PersistenceOptions.Providers.InMemory}, {PersistenceOptions.Providers.Sqlite}.");
        }
    }

    /// <remarks>
    /// WAL and a busy timeout are not tuning. SQLite's default journal serialises writers so
    /// aggressively that concurrent inserts surface as SQLITE_BUSY rather than as the unique
    /// constraint violation FR-1.8 is about; with these set, the losers of a race get the
    /// answer they should get.
    /// </remarks>
    private static void AddSqlite(IServiceCollection services, IConfiguration configuration)
    {
        var filePath = configuration
            .GetSection(PersistenceOptions.SectionName)[nameof(PersistenceOptions.FilePath)];

        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new InvalidOperationException(
                $"Persistence:FilePath is required when Persistence:Provider is " +
                $"'{PersistenceOptions.Providers.Sqlite}'.");
        }

        var fullPath = Path.GetFullPath(filePath);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = fullPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            Pooling = true,
            DefaultTimeout = 30
        }.ToString();

        services.AddDbContextFactory<UsersDbContext>(options =>
            options.UseSqlite(connectionString, sqlite => sqlite.CommandTimeout(30)));

        services.AddSingleton<IUserRepository, SqliteUserRepository>();
        services.AddSingleton<IDatabaseInitializer, SqliteDatabaseInitializer>();
    }
}
