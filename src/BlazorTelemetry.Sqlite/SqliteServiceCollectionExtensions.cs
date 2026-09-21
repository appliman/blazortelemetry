using BlazorTelemetry.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BlazorTelemetry.Sqlite;

public static class SqliteServiceCollectionExtensions
{
    public static IServiceCollection AddBlazorTelemetrySqlite(
        this IServiceCollection services,
        string connectionString,
        int poolSize = 16)
    {
        services.AddPooledDbContextFactory<TelemetryDbContext>(
            options => options.UseSqlite(connectionString),
            poolSize);
        services.AddScoped<ITelemetryRepository, TelemetryRepository>();
        return services;
    }
}
