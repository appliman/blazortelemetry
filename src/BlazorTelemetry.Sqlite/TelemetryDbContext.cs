using BlazorTelemetry.Core;
using Microsoft.EntityFrameworkCore;

namespace BlazorTelemetry.Sqlite;

public sealed class TelemetryDbContext(DbContextOptions<TelemetryDbContext> options) : DbContext(options)
{
    public DbSet<TelemetryItem> TelemetryItems => Set<TelemetryItem>();
    public DbSet<IngestionApplication> IngestionApplications => Set<IngestionApplication>();
    public DbSet<AlertRule> AlertRules => Set<AlertRule>();
    public DbSet<Incident> Incidents => Set<Incident>();
    public DbSet<DashboardDefinition> Dashboards => Set<DashboardDefinition>();
    public DbSet<NotificationDelivery> NotificationDeliveries => Set<NotificationDelivery>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TelemetryItem>(entity =>
        {
            entity.ToTable("TelemetryItems");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.TimestampUtc).HasConversion<long>();
            entity.Property(item => item.ObservedUtc).HasConversion<long>();
            entity.Property(item => item.ServiceName).HasMaxLength(256);
            entity.Property(item => item.Name).HasMaxLength(512);
            entity.Property(item => item.TraceId).HasMaxLength(32);
            entity.Property(item => item.SpanId).HasMaxLength(16);
            entity.Property(item => item.Fingerprint).HasMaxLength(128);
            entity.HasIndex(item => item.TimestampUtc);
            entity.HasIndex(item => new { item.Kind, item.TimestampUtc });
            entity.HasIndex(item => new { item.Kind, item.Name, item.TimestampUtc });
            entity.HasIndex(item => new { item.Kind, item.Name, item.ServiceName, item.TimestampUtc });
            entity.HasIndex(item => new { item.ServiceName, item.TimestampUtc });
            entity.HasIndex(item => item.TraceId);
            entity.HasIndex(item => item.Fingerprint).IsUnique().HasFilter("Fingerprint IS NOT NULL");
        });

        modelBuilder.Entity<IngestionApplication>(entity =>
        {
            entity.HasKey(application => application.Id);
            entity.HasIndex(application => application.KeyHash).IsUnique();
            entity.Property(application => application.CreatedUtc).HasConversion<long>();
            entity.Property(application => application.LastSeenUtc).HasConversion<long?>();
        });

        modelBuilder.Entity<AlertRule>().HasKey(rule => rule.Id);
        modelBuilder.Entity<AlertRule>().Property(rule => rule.SilencedUntilUtc).HasConversion<long?>();

        modelBuilder.Entity<Incident>(entity =>
        {
            entity.HasKey(incident => incident.Id);
            entity.Property(incident => incident.StartedUtc).HasConversion<long>();
            entity.Property(incident => incident.LastEvaluatedUtc).HasConversion<long>();
            entity.Property(incident => incident.ResolvedUtc).HasConversion<long?>();
            entity.Property(incident => incident.AcknowledgedUtc).HasConversion<long?>();
            entity.HasIndex(incident => new { incident.AlertRuleId, incident.State });
        });

        modelBuilder.Entity<DashboardDefinition>(entity =>
        {
            entity.HasKey(dashboard => dashboard.Id);
            entity.Property(dashboard => dashboard.UpdatedUtc).HasConversion<long>();
        });

        modelBuilder.Entity<NotificationDelivery>(entity =>
        {
            entity.HasKey(delivery => delivery.Id);
            entity.Property(delivery => delivery.NextAttemptUtc).HasConversion<long>();
            entity.Property(delivery => delivery.SentUtc).HasConversion<long?>();
            entity.HasIndex(delivery => delivery.EventKey).IsUnique();
        });
    }
}
