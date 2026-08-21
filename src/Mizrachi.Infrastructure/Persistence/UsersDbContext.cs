using Microsoft.EntityFrameworkCore;
using Mizrachi.Domain;

namespace Mizrachi.Infrastructure.Persistence;

/// <summary>
/// EF Core context for the SQLite store. Internal: no other project sees an EF type.
/// </summary>
internal sealed class UsersDbContext : DbContext
{
    public UsersDbContext(DbContextOptions<UsersDbContext> options) : base(options)
    {
    }

    public DbSet<User> Users => Set<User>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var user = modelBuilder.Entity<User>();

        user.ToTable("Users");
        user.HasKey(entity => entity.UserId);

        // The entity has no public constructor and no setters: it is created through
        // User.Create, which enforces its invariants. EF is told to write the backing fields
        // directly rather than demanding a mutable shape that would weaken the type.
        user.Property(entity => entity.UserId).ValueGeneratedNever();
        user.Property(entity => entity.UserName).HasMaxLength(UserNamePolicy.MaxLength).IsRequired();
        user.Property(entity => entity.UserPassword).IsRequired();

        // Uniqueness lives here, in the datastore, so a concurrent duplicate insert is refused
        // by the database rather than by a prior lookup in application code (FR-1.8).
        // COLLATE NOCASE makes the constraint case-insensitive (FR-1.5); it folds ASCII only,
        // which is exactly the rule UserNameComparer applies in the other stores.
        user.Property(entity => entity.UserName).UseCollation("NOCASE");
        user.HasIndex(entity => entity.UserName).IsUnique();
    }
}
