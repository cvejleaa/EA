using Ea.Api.Capabilities;
using Ea.Api.Integrations;
using Ea.Api.Persons;
using Ea.Api.Systems;
using Ea.Api.Teams;
using Microsoft.EntityFrameworkCore;

namespace Ea.Api.Data;

public sealed class EaDbContext(DbContextOptions<EaDbContext> options) : DbContext(options)
{
    /// <summary>Unik-indeks for navn inden for samme forælder (topniveau tæller som én forælder).</summary>
    public const string SystemNameIndex = "ux_systems_parent_name";

    /// <summary>Dublet-nøglen for integrationer: samme ender, type, platform og navn (NULL tæller som en værdi).</summary>
    public const string IntegrationKeyIndex = "ux_integrations_natural_key";

    public const string DataObjectNameIndex = "ux_data_objects_name";

    /// <summary>Kapabilitetens kode er unik uden hensyn til store/små bogstaver (importens nøgle).</summary>
    public const string CapabilityCodeIndex = "ux_capabilities_code";

    public DbSet<SystemEntity> Systems => Set<SystemEntity>();

    public DbSet<SystemRoleAssignment> SystemRoles => Set<SystemRoleAssignment>();

    public DbSet<Person> Persons => Set<Person>();

    public DbSet<Team> Teams => Set<Team>();

    public DbSet<Integration> Integrations => Set<Integration>();

    public DbSet<DataObject> DataObjects => Set<DataObject>();

    public DbSet<Capability> Capabilities => Set<Capability>();

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

        modelBuilder.Entity<Integration>(e =>
        {
            e.ToTable("integrations", t => t.HasCheckConstraint(
                "ck_integrations_not_self", "source_system_id <> target_system_id"));
            e.HasKey(i => i.Id);
            e.Property(i => i.Id).ValueGeneratedNever();
            e.Property(i => i.Type).HasConversion<string>().HasMaxLength(40);
            e.Property(i => i.Name).HasMaxLength(IntegrationRules.NameMaxLength);
            e.Property(i => i.NameNormalized).HasMaxLength(IntegrationRules.NameMaxLength);
            e.Property(i => i.Description).HasMaxLength(IntegrationRules.DescriptionMaxLength);
            e.Property(i => i.Version).IsRowVersion();

            e.HasIndex(i => new { i.SourceSystemId, i.TargetSystemId, i.Type, i.ViaPlatformId, i.NameNormalized })
                .IsUnique()
                .AreNullsDistinct(false)
                .HasDatabaseName(IntegrationKeyIndex);
            e.HasIndex(i => i.TargetSystemId);
            e.HasIndex(i => i.ViaPlatformId);

            // Restrict: et system, der indgår i integrationer, kan ikke slettes (SystemRules.DeleteBlockedReason).
            e.HasOne(i => i.SourceSystem).WithMany().HasForeignKey(i => i.SourceSystemId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(i => i.TargetSystem).WithMany().HasForeignKey(i => i.TargetSystemId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(i => i.ViaPlatform).WithMany().HasForeignKey(i => i.ViaPlatformId).OnDelete(DeleteBehavior.Restrict);

            e.HasMany(i => i.DataObjects)
                .WithOne()
                .HasForeignKey(d => d.IntegrationId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<DataObject>(e =>
        {
            e.ToTable("data_objects");
            e.HasKey(d => d.Id);
            e.Property(d => d.Id).ValueGeneratedNever();
            e.Property(d => d.Name).HasMaxLength(IntegrationRules.NameMaxLength).IsRequired();
            e.Property(d => d.NameNormalized).HasMaxLength(IntegrationRules.NameMaxLength).IsRequired();
            e.HasIndex(d => d.NameNormalized).IsUnique().HasDatabaseName(DataObjectNameIndex);
        });

        modelBuilder.Entity<IntegrationDataObject>(e =>
        {
            e.ToTable("integration_data_objects");
            e.HasKey(d => new { d.IntegrationId, d.DataObjectId });
            e.HasOne(d => d.DataObject).WithMany().HasForeignKey(d => d.DataObjectId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(d => d.DataObjectId);
        });

        modelBuilder.Entity<Capability>(e =>
        {
            e.ToTable("capabilities");
            e.HasKey(c => c.Id);
            e.Property(c => c.Id).ValueGeneratedNever();
            e.Property(c => c.Code).HasMaxLength(CapabilityRules.CodeMaxLength).IsRequired();
            e.Property(c => c.CodeNormalized).HasMaxLength(CapabilityRules.CodeMaxLength).IsRequired();
            e.Property(c => c.Name).HasMaxLength(CapabilityRules.NameMaxLength).IsRequired();
            e.Property(c => c.Description).HasMaxLength(CapabilityRules.DescriptionMaxLength);
            e.HasIndex(c => c.CodeNormalized).IsUnique().HasDatabaseName(CapabilityCodeIndex);

            // Restrict: importen sletter børn før forældre; databasen afviser et hul i træet.
            e.HasOne<Capability>().WithMany().HasForeignKey(c => c.ParentId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(c => c.ParentId);
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
