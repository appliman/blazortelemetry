using BlazorTelemetry.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BlazorTelemetry.Sqlite;

public static class SqliteServiceCollectionExtensions
{
    public static IServiceCollection AddBlazorTelemetrySqlite(this IServiceCollection services, string connectionString)
    {
        services.AddPooledDbContextFactory<TelemetryDbContext>(options => options.UseSqlite(connectionString));
        services.AddScoped<ITelemetryRepository, TelemetryRepository>();
        return services;
    }
}
