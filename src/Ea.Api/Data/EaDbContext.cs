using Ea.Api.Persons;
using Ea.Api.Systems;
using Ea.Api.Teams;
using Microsoft.EntityFrameworkCore;

namespace Ea.Api.Data;

public sealed class EaDbContext(DbContextOptions<EaDbContext> options) : DbContext(options)
{
    /// <summary>Unik-indeks for navn inden for samme forælder (topniveau tæller som én forælder).</summary>
    public const string SystemNameIndex = "ux_systems_parent_name";

    public DbSet<SystemEntity> Systems => Set<SystemEntity>();

    public DbSet<SystemRoleAssignment> SystemRoles => Set<SystemRoleAssignment>();

    public DbSet<Person> Persons => Set<Person>();

    public DbSet<Team> Teams => Set<Team>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Eget schema, så databasen kan deles på en fælles DTU-server.
        modelBuilder.HasDefaultSchema("ea");

        modelBuilder.Entity<SystemEntity>(e =>
        {
            e.ToTable("systems");
            e.HasKey(s => s.Id);
            e.Property(s => s.Id).ValueGeneratedNever();
            e.Property(s => s.Name).HasMaxLength(SystemRules.NameMaxLength).IsRequired();
            e.Property(s => s.NameNormalized).HasMaxLength(SystemRules.NameMaxLength).IsRequired();
            e.Property(s => s.Aliases).IsRequired();
            e.Property(s => s.Description).HasMaxLength(SystemRules.DescriptionMaxLength);
            // Enums som tekst: nye værdier kræver ingen migrering, og data kan læses direkte.
            e.Property(s => s.Type).HasConversion<string>().HasMaxLength(40);
            e.Property(s => s.LifecycleStatus).HasConversion<string>().HasMaxLength(40);
            e.Property(s => s.LastConfirmedByOid).HasMaxLength(100);
            e.Property(s => s.LastConfirmedByName).HasMaxLength(200);
            e.Property(s => s.Version).IsRowVersion();

            e.HasIndex(s => new { s.ParentSystemId, s.NameNormalized })
                .IsUnique()
                .AreNullsDistinct(false)
                .HasDatabaseName(SystemNameIndex);

            e.HasOne(s => s.ManagingTeam)
                .WithMany()
                .HasForeignKey(s => s.ManagingTeamId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(s => s.ParentSystem)
                .WithMany(s => s.Modules)
                .HasForeignKey(s => s.ParentSystemId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasMany(s => s.Roles)
                .WithOne()
                .HasForeignKey(r => r.SystemId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<SystemRoleAssignment>(e =>
        {
            e.ToTable("system_roles");
            e.HasKey(r => new { r.SystemId, r.Role, r.PersonId });
            e.Property(r => r.Role).HasConversion<string>().HasMaxLength(40);
            e.HasOne(r => r.Person)
                .WithMany()
                .HasForeignKey(r => r.PersonId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(r => r.PersonId);
        });

        modelBuilder.Entity<Person>(e =>
        {
            e.ToTable("persons");
            e.HasKey(p => p.Id);
            e.Property(p => p.Id).ValueGeneratedNever();
            e.Property(p => p.DisplayName).HasMaxLength(200).IsRequired();
            e.Property(p => p.Email).HasMaxLength(320);
            e.Property(p => p.Department).HasMaxLength(200);
            e.Property(p => p.EntraObjectId).HasMaxLength(100);
            e.HasIndex(p => p.EntraObjectId).IsUnique();
        });

        modelBuilder.Entity<Team>(e =>
        {
            e.ToTable("teams");
            e.HasKey(t => t.Id);
            e.Property(t => t.Id).ValueGeneratedNever();
            e.Property(t => t.Name).HasMaxLength(200).IsRequired();
            e.HasIndex(t => t.Name).IsUnique();
            e.HasData(Team.Seed());
        });
    }
}
