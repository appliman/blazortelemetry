using BlazorTelemetry.Core;
using BlazorTelemetry.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace BlazorTelemetry.AspNetCore;

public static class BlazorTelemetryServiceCollectionExtensions
{
    public static IServiceCollection AddBlazorTelemetry(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<BlazorTelemetryOptions>? configure = null)
    {
        services.AddOptions<BlazorTelemetryOptions>()
            .Bind(configuration.GetSection(BlazorTelemetryOptions.SECTION_NAME))
            .Validate(options => options.RawRetentionDays > 0 && options.MetricRetentionDays > 0, "Retention values must be positive.")
            .Validate(options => options.QueueCapacity > 0, "The ingestion queue capacity must be positive.")
            .Validate(options => options.MaximumDashboardMetricRows > 0, "The dashboard metric row limit must be positive.")
            .Validate(options => options.DashboardRefreshIntervalSeconds > 0, "The dashboard refresh interval must be positive.")
            .Validate(options => options.DbContextPoolSize > 0, "The database context pool size must be positive.")
            .ValidateOnStart();

        if (configure is not null)
        {
            services.PostConfigure(configure);
        }

        var snapshot = new BlazorTelemetryOptions();
        configuration.GetSection(BlazorTelemetryOptions.SECTION_NAME).Bind(snapshot);
        configure?.Invoke(snapshot);
        EnsureDatabaseDirectory(snapshot.ConnectionString);

        services.AddSingleton(serviceProvider => serviceProvider.GetRequiredService<IOptions<BlazorTelemetryOptions>>().Value);
        services.AddBlazorTelemetrySqlite(snapshot.ConnectionString, snapshot.DbContextPoolSize);
        services.AddRequestDecompression();
        services.AddGrpc(options => options.MaxReceiveMessageSize = snapshot.MaximumRequestBytes);
        services.AddSingleton<CollectorCounters>();
        services.AddSingleton<TelemetryIngestionQueue>();
        services.AddSingleton<IngestionKeyAuthorizer>();
        services.AddSingleton<OtlpValueConverter>();
        services.AddSingleton<OtlpParser>();
        services.AddHostedService<TelemetryWriterService>();
        services.AddHostedService<TelemetryDatabaseInitializer>();
        services.AddHostedService<TelemetryRetentionService>();
        services.AddHostedService<AlertEvaluationService>();
        services.AddHostedService<NotificationDeliveryService>();
        services.AddHttpClient();
        services.AddHttpContextAccessor();
        services.AddScoped<TelemetryCircuitHandler>();
        services.AddScoped<Microsoft.AspNetCore.Components.Server.Circuits.CircuitHandler>(provider => provider.GetRequiredService<TelemetryCircuitHandler>());
        return services;
    }

    private static void EnsureDatabaseDirectory(string connectionString)
    {
        var builder = new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder(connectionString);
        var fullPath = Path.GetFullPath(builder.DataSource);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }
}
