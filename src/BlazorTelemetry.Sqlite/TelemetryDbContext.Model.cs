using BlazorTelemetry.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace BlazorTelemetry.Sqlite;

public sealed partial class TelemetryDbContext
{
    public DbSet<TelemetryLog> TelemetryLogs => Set<TelemetryLog>();
    public DbSet<TelemetryTrace> TelemetryTraces => Set<TelemetryTrace>();
    public DbSet<TelemetryMetric> TelemetryMetrics => Set<TelemetryMetric>();
    public DbSet<TelemetryRequest> TelemetryRequest => Set<TelemetryRequest>();
    public DbSet<TelemetryResource> TelemetryResources => Set<TelemetryResource>();

    private static void ConfigureTelemetry(ModelBuilder _model)
    {
        var _utc = new ValueConverter<DateTimeOffset, long>(_value => _value.UtcTicks, _value => new DateTimeOffset(_value, TimeSpan.Zero));
        _model.Entity<TelemetryResource>(_entity =>
        {
            _entity.ToTable("TelemetryResources");
            _entity.HasKey(_resource => _resource.Id);
            _entity.HasIndex(_resource => _resource.Hash);
            _entity.HasIndex(_resource => new { _resource.ServiceName, _resource.Id });
        });
        _model.Entity<TelemetryLog>(_entity =>
        {
            _entity.ToTable("TelemetryLogs");
            _entity.HasIndex(_item => _item.Fingerprint).IsUnique().HasFilter("Fingerprint IS NOT NULL");
            _entity.HasKey(_item => _item.Id);
            _entity.Property(_item => _item.TimestampUtc).HasConversion(_utc);
            _entity.Property(_item => _item.ObservedUtc).HasConversion(_utc);
            _entity.HasOne(_item => _item.Resource).WithMany().HasForeignKey(_item => _item.ResourceId).OnDelete(DeleteBehavior.Restrict);
            _entity.HasIndex(_item => _item.TimestampUtc);
            _entity.HasIndex(_item => new { _item.ResourceId, _item.TimestampUtc });
            _entity.HasIndex(_item => _item.TraceId);
            _entity.HasIndex(_item => new { _item.TraceId, _item.SpanId });
        });
        _model.Entity<TelemetryTrace>(_entity =>
        {
            _entity.ToTable("TelemetryTraces");
            _entity.HasKey(_item => _item.Id);
            _entity.Property(_item => _item.TimestampUtc).HasConversion(_utc);
            _entity.Property(_item => _item.ObservedUtc).HasConversion(_utc);
            _entity.HasOne(_item => _item.Resource).WithMany().HasForeignKey(_item => _item.ResourceId).OnDelete(DeleteBehavior.Restrict);
            _entity.HasIndex(_item => _item.TimestampUtc);
            _entity.HasIndex(_item => new { _item.ResourceId, _item.TimestampUtc });
            _entity.HasIndex(_item => _item.TraceId);
            _entity.HasIndex(_item => new { _item.TraceId, _item.SpanId });
            _entity.HasIndex(_item => _item.Fingerprint).IsUnique().HasFilter("Fingerprint IS NOT NULL");
        });
        _model.Entity<TelemetryMetric>(_entity =>
        {
            _entity.ToTable("TelemetryMetrics");
            _entity.Property(_item => _item.Body).HasColumnName("Description");
            _entity.HasKey(_item => _item.Id);
            _entity.Property(_item => _item.TimestampUtc).HasConversion(_utc);
            _entity.Property(_item => _item.ObservedUtc).HasConversion(_utc);
            _entity.HasOne(_item => _item.Resource).WithMany().HasForeignKey(_item => _item.ResourceId).OnDelete(DeleteBehavior.Restrict);
            _entity.HasIndex(_item => _item.TimestampUtc);
            _entity.HasIndex(_item => new { _item.ResourceId, _item.TimestampUtc });
            _entity.HasIndex(_item => _item.Fingerprint).IsUnique().HasFilter("Fingerprint IS NOT NULL");
            _entity.Property(_item => _item.StartTimeUtc).HasConversion(_utc);
            _entity.HasIndex(_item => new { _item.Name, _item.TimestampUtc });
            _entity.HasIndex(_item => new { _item.Name, _item.ResourceId, _item.TimestampUtc });
        });
        _model.Entity<TelemetryRequest>(_entity =>
        {
            _entity.ToTable("TelemetryRequest");
            _entity.HasKey(_item => _item.Id);
            _entity.Property(_item => _item.TimestampUtc).HasConversion(_utc);
            _entity.Property(_item => _item.ObservedUtc).HasConversion(_utc);
            _entity.HasOne(_item => _item.Resource).WithMany().HasForeignKey(_item => _item.ResourceId).OnDelete(DeleteBehavior.Restrict);
            _entity.HasIndex(_item => _item.TimestampUtc);
            _entity.HasIndex(_item => new { _item.ResourceId, _item.TimestampUtc });
            _entity.HasIndex(_item => _item.TraceId);
            _entity.HasIndex(_item => new { _item.TraceId, _item.SpanId });
            _entity.HasIndex(_item => _item.Fingerprint).IsUnique().HasFilter("Fingerprint IS NOT NULL");
        });
        _model.Entity<TelemetryTraceEvent>(_entity =>
        {
            _entity.ToTable("TelemetryTraceEvents");
            _entity.HasKey(_item => new { _item.TraceRecordId, _item.Ordinal });
            _entity.HasOne<TelemetryTrace>().WithMany().HasForeignKey(_item => _item.TraceRecordId).OnDelete(DeleteBehavior.Cascade);
            _entity.Property(_item => _item.TimestampUtc).HasConversion(_utc);
        });
        _model.Entity<TelemetryTraceLink>(_entity =>
        {
            _entity.ToTable("TelemetryTraceLinks");
            _entity.HasKey(_item => new { _item.TraceRecordId, _item.Ordinal });
            _entity.HasOne<TelemetryTrace>().WithMany().HasForeignKey(_item => _item.TraceRecordId).OnDelete(DeleteBehavior.Cascade);
        });
        _model.Entity<TelemetryMetricBucket>(_entity =>
        {
            _entity.ToTable("TelemetryMetricBuckets");
            _entity.HasKey(_item => new { _item.MetricId, _item.Ordinal, _item.Group });
            _entity.HasOne<TelemetryMetric>().WithMany().HasForeignKey(_item => _item.MetricId).OnDelete(DeleteBehavior.Cascade);
        });
        _model.Entity<TelemetryMetricQuantile>(_entity =>
        {
            _entity.ToTable("TelemetryMetricQuantiles");
            _entity.HasKey(_item => new { _item.MetricId, _item.Ordinal });
            _entity.HasOne<TelemetryMetric>().WithMany().HasForeignKey(_item => _item.MetricId).OnDelete(DeleteBehavior.Cascade);
        });
        _model.Entity<TelemetryMetricExemplar>(_entity =>
        {
            _entity.ToTable("TelemetryMetricExemplars");
            _entity.HasKey(_item => new { _item.MetricId, _item.Ordinal });
            _entity.HasOne<TelemetryMetric>().WithMany().HasForeignKey(_item => _item.MetricId).OnDelete(DeleteBehavior.Cascade);
            _entity.Property(_item => _item.TimestampUtc).HasConversion(_utc);
        });
    }
}
