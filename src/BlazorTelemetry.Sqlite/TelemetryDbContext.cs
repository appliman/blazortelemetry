using BlazorTelemetry.Core;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace BlazorTelemetry.Sqlite;

public sealed partial class TelemetryDbContext(DbContextOptions<TelemetryDbContext> options) : DbContext(options), IDataProtectionKeyContext
{
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();
    public IQueryable<TelemetryItem> TelemetryItems => QueryTelemetry();
    public DbSet<IngestionApplication> IngestionApplications => Set<IngestionApplication>();
    public DbSet<AlertRule> AlertRules => Set<AlertRule>();
    public DbSet<Incident> Incidents => Set<Incident>();
    public DbSet<DashboardDefinition> Dashboards => Set<DashboardDefinition>();
    public DbSet<NotificationDelivery> NotificationDeliveries => Set<NotificationDelivery>();

    public DbSet<TelemetryUser> Users => Set<TelemetryUser>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<DataProtectionKey>(entity =>
        {
            entity.ToTable("DataProtectionKeys");
            entity.HasKey(key => key.Id);
        });

        modelBuilder.Entity<TelemetryUser>(entity =>
        {
            entity.HasKey(user => user.Id);
            entity.Property(user => user.Identifier).UseCollation("NOCASE").IsRequired().HasMaxLength(256);
            entity.HasIndex(user => user.Identifier).IsUnique();
            entity.Property(user => user.SecurityVersion).IsConcurrencyToken();
            entity.Property(user => user.ActivatedUtc).HasConversion<long?>();
        });

        ConfigureTelemetry(modelBuilder);

        modelBuilder.Entity<IngestionApplication>(entity =>
        {
            entity.HasKey(application => application.Id);
            entity.HasIndex(application => application.KeyHash).IsUnique();
            entity.HasIndex(application => application.Name);
            entity.Property(application => application.CreatedUtc).HasConversion<long>();
            entity.Property(application => application.ExpiresUtc).HasConversion<long?>();
            entity.Property(application => application.RevokedUtc).HasConversion<long?>();
            entity.Property(application => application.LastSeenUtc).HasConversion<long?>();
        });

        modelBuilder.Entity<AlertRule>().HasKey(rule => rule.Id);
        modelBuilder.Entity<AlertRule>().HasIndex(rule => rule.Name);
        modelBuilder.Entity<AlertRule>().Property(rule => rule.SilencedUntilUtc).HasConversion<long?>();

        modelBuilder.Entity<Incident>(entity =>
        {
            entity.HasKey(incident => incident.Id);
            entity.Property(incident => incident.StartedUtc).HasConversion<long>();
            entity.Property(incident => incident.LastEvaluatedUtc).HasConversion<long>();
            entity.Property(incident => incident.ResolvedUtc).HasConversion<long?>();
            entity.Property(incident => incident.AcknowledgedUtc).HasConversion<long?>();
            entity.HasIndex(incident => new { incident.AlertRuleId, incident.State });
            entity.HasIndex(incident => incident.StartedUtc);
        });

        modelBuilder.Entity<DashboardDefinition>(entity =>
        {
            entity.HasKey(dashboard => dashboard.Id);
            entity.Property(dashboard => dashboard.UpdatedUtc).HasConversion<long>();
            entity.HasIndex(dashboard => dashboard.Name);
        });

        modelBuilder.Entity<NotificationDelivery>(entity =>
        {
            entity.HasKey(delivery => delivery.Id);
            entity.Property(delivery => delivery.NextAttemptUtc).HasConversion<long>();
            entity.Property(delivery => delivery.SentUtc).HasConversion<long?>();
            entity.HasIndex(delivery => delivery.EventKey).IsUnique();
            entity.HasIndex(delivery => new { delivery.Status, delivery.NextAttemptUtc });
        });
    }
}
