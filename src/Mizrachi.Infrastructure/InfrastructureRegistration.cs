using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
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
    /// <param name="isDevelopmentEnvironment">
    /// Whether the host is running in Development. It is passed in rather than read from an
    /// <c>IHostEnvironment</c> so that Infrastructure keeps no dependency on the hosting stack,
    /// and it defaults to <see langword="false"/> so that a caller who says nothing gets the
    /// stricter behaviour rather than the relaxed one.
    /// </param>
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        bool isDevelopmentEnvironment = false)
    {
        // JwtOptionsValidator rather than ValidateDataAnnotations: a missing signing key is the
        // one startup failure a reviewer cloning this repository will actually hit, and the
        // message has to tell them the key name and the command, not just "required" (NFR-1.4).
        services.AddSingleton<IValidateOptions<JwtOptions>, JwtOptionsValidator>();

        // Registered unconditionally and told the environment, rather than registered only in
        // Development: one wiring path, and the decision is visible in one place inside the
        // type itself. It is a no-op outside Development (NFR-1.5).
        services.AddSingleton<IPostConfigureOptions<JwtOptions>>(serviceProvider =>
            new EphemeralDevelopmentSigningKey(
                isDevelopmentEnvironment,
                serviceProvider.GetRequiredService<ILogger<EphemeralDevelopmentSigningKey>>()));

        services
            .AddOptions<JwtOptions>()
            .Bind(configuration.GetSection(JwtOptions.SectionName))
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

            case PersistenceOptions.Providers.JsonFile:
                AddJsonFile(services, configuration);
                break;

            default:
                throw new InvalidOperationException(
                    $"Persistence:Provider is '{provider ?? "(not set)"}'. Valid values are: " +
                    $"{PersistenceOptions.Providers.InMemory}, {PersistenceOptions.Providers.Sqlite}, " +
                    $"{PersistenceOptions.Providers.JsonFile}.");
        }
    }

    /// <remarks>
    /// WAL and a busy timeout are not tuning. SQLite's default journal serialises writers so
    /// aggressively that concurrent inserts surface as SQLITE_BUSY rather than as the unique
    /// constraint violation FR-1.8 is about; with these set, the losers of a race get the
    /// answer they should get.
    /// </remarks>
    private static void AddJsonFile(IServiceCollection services, IConfiguration configuration)
    {
        var fullPath = ResolveFilePath(configuration, PersistenceOptions.Providers.JsonFile);

        services.AddSingleton<JsonFileUserRepository>(_ => new JsonFileUserRepository(fullPath));
        services.AddSingleton<IUserRepository>(sp => sp.GetRequiredService<JsonFileUserRepository>());
        services.AddSingleton<IDatabaseInitializer>(sp => sp.GetRequiredService<JsonFileUserRepository>());
    }

    /// <remarks>
    /// The path comes from configuration, not from a request, but it is still resolved to a
    /// full path and confined to a real directory rather than being passed through verbatim
    /// (SEC-8.3).
    /// </remarks>
    private static string ResolveFilePath(IConfiguration configuration, string provider)
    {
        var filePath = configuration
            .GetSection(PersistenceOptions.SectionName)[nameof(PersistenceOptions.FilePath)];

        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new InvalidOperationException(
                $"Persistence:FilePath is required when Persistence:Provider is '{provider}'.");
        }

        if (filePath.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
        {
            throw new InvalidOperationException("Persistence:FilePath contains invalid path characters.");
        }

        var fullPath = Path.GetFullPath(filePath);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        return fullPath;
    }

    private static void AddSqlite(IServiceCollection services, IConfiguration configuration)
    {
        var fullPath = ResolveFilePath(configuration, PersistenceOptions.Providers.Sqlite);

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
