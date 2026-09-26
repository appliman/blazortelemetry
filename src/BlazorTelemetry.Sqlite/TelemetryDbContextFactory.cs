using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace BlazorTelemetry.Sqlite;

public sealed class TelemetryDbContextFactory : IDesignTimeDbContextFactory<TelemetryDbContext>
{
    public TelemetryDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<TelemetryDbContext>()
            .UseSqlite("Data Source=blazor-telemetry-design.db",
                sqliteOptions => sqliteOptions.UseParameterizedCollectionMode(ParameterTranslationMode.MultipleParameters))
            .Options;
        return new TelemetryDbContext(options);
    }
}
