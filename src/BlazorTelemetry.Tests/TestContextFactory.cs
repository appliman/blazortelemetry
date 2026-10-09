using BlazorTelemetry.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace BlazorTelemetry.Tests;

internal sealed class TestContextFactory(DbContextOptions<TelemetryDbContext> options) : IDbContextFactory<TelemetryDbContext>
{
    public TelemetryDbContext CreateDbContext() => new(options);
}
