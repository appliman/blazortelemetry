using BlazorTelemetry.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace BlazorTelemetry.Tests;

internal sealed class TestDbContextFactory(DbContextOptions<TelemetryDbContext> options) : IDbContextFactory<TelemetryDbContext>
{
    public TelemetryDbContext CreateDbContext() => new(options);
}
