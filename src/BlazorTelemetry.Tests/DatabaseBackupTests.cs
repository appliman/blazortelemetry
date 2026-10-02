using System.Net;
using System.IO.Compression;
using System.Security.Claims;
using BlazorTelemetry.Core;
using BlazorTelemetry.Host.Administration;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace BlazorTelemetry.Tests;

public sealed class DatabaseBackupTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"telemetry-backup-tests-{Guid.NewGuid():N}");

    public DatabaseBackupTests()
    {
        Directory.CreateDirectory(_directory);
    }

    [Fact]
    public async Task BackupIncludesCommittedWalDataAndDeletesTemporaryFileWhenClosed()
    {
        await using var _source = await CreateDatabase();
        Assert.True(new FileInfo($"{_source.DataSource}-wal").Length > 0);
        using var _service = new DatabaseBackupService(new BlazorTelemetryOptions { ConnectionString = _source.ConnectionString });
        string _backupPath;
        var _downloadPath = Path.Combine(_directory, "download.db");
        await using (var _backup = await _service.CreateBackup(CancellationToken.None))
        {
            _backupPath = _backup.Name;
            Assert.Equal(_directory, Path.GetDirectoryName(_backupPath));
            await using var _download = File.Create(_downloadPath);
            await _backup.CopyToAsync(_download);
        }

        Assert.False(File.Exists(_backupPath));
        await AssertDatabase(_downloadPath);
        await using var _command = _source.CreateCommand();
        _command.CommandText = "INSERT INTO BackupTest VALUES ('after backup');";
        await _command.ExecuteNonQueryAsync();
        await AssertDatabase(_downloadPath);
    }

    [Fact]
    public async Task MissingDatabaseDoesNotCreateAnEmptyDatabaseOrLeaveTemporaryFiles()
    {
        using var _service = new DatabaseBackupService(new BlazorTelemetryOptions
        {
            ConnectionString = $"Data Source={Path.Combine(_directory, "missing.db")};Pooling=False"
        });
        await Assert.ThrowsAsync<SqliteException>(() => _service.CreateBackup(CancellationToken.None));
        Assert.Empty(Directory.GetFiles(_directory));
    }

    [Fact]
    public async Task CancelledBackupDoesNotLeaveFilesOrBlockTheNextBackup()
    {
        await using var _source = await CreateDatabase();
        using var _service = new DatabaseBackupService(new BlazorTelemetryOptions { ConnectionString = _source.ConnectionString });
        using var _cancellation = new CancellationTokenSource();
        await _cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _service.CreateBackup(_cancellation.Token));
        Assert.Empty(Directory.GetFiles(_directory, ".blazor-telemetry-backup-*"));
        await using var _backup = await _service.CreateBackup(CancellationToken.None);
        Assert.True(_backup.Length > 0);
    }

    [Fact]
    public async Task DownloadRequiresRequestingAdministratorAndReturnsAnUnencryptedZip()
    {
        await using var _source = await CreateDatabase();
        var _builder = WebApplication.CreateBuilder();
        _builder.Configuration.Sources.Clear();
        _builder.Configuration.AddInMemoryCollection();
        _builder.WebHost.UseUrls("http://127.0.0.1:0");
        _builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();
        _builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie();
        _builder.Services.AddAuthorizationBuilder().AddPolicy("BlazorTelemetryAdministrator", _policy => _policy.RequireRole("Administrator"));
        _builder.Services.AddControllersWithViews().AddApplicationPart(typeof(DatabaseBackupController).Assembly);
        _builder.Services.AddSingleton(new BlazorTelemetryOptions { ConnectionString = _source.ConnectionString });
        _builder.Services.AddSingleton<DatabaseBackupService>();
        _builder.Services.AddSingleton(TimeProvider.System);
        _builder.Services.AddSingleton<DatabaseBackupWorker>();
        _builder.Services.AddHostedService(_services => _services.GetRequiredService<DatabaseBackupWorker>());
        await using var _app = _builder.Build();
        _app.UseAuthentication();
        _app.UseAuthorization();
        _app.UseAntiforgery();
        _app.MapControllers();
        _app.MapGet("/test/sign-in/{role}", async (HttpContext _context, string role, string? owner) =>
        {
            var _identity = new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, owner ?? "backup-test"), new Claim(ClaimTypes.Role, role)],
                CookieAuthenticationDefaults.AuthenticationScheme);
            await _context.SignInAsync(new ClaimsPrincipal(_identity));
            return Results.Ok();
        });
        var _worker = _app.Services.GetRequiredService<DatabaseBackupWorker>();
        var _job = Assert.IsType<DatabaseBackupJob>(_worker.Enqueue("backup-test"));
        Assert.False(_job.Completion.IsCompleted);
        Assert.Null(_worker.FindAvailableArchive("backup-test"));
        Assert.Null(_worker.OpenDownload(_job.Id, "backup-test", out _));
        await _app.StartAsync();
        try
        {
            using var _handler = new HttpClientHandler { AllowAutoRedirect = false, UseProxy = false };
            using var _client = new HttpClient(_handler) { BaseAddress = new Uri(_app.Urls.Single()) };
            var _url = $"/administration/database/download/{_job.Id}";
            using var _anonymous = await _client.GetAsync(_url);
            Assert.True(_anonymous.StatusCode is HttpStatusCode.Redirect or HttpStatusCode.Unauthorized);
            using var _readerLogin = await _client.GetAsync("/test/sign-in/Reader");
            using var _reader = await _client.GetAsync(_url);
            Assert.True(_reader.StatusCode is HttpStatusCode.Redirect or HttpStatusCode.Forbidden);
            Assert.True(await _job.Completion.WaitAsync(TimeSpan.FromSeconds(15)));
            Assert.Same(_job, _worker.FindAvailableArchive("backup-test"));
            Assert.Null(_worker.FindAvailableArchive("another-admin"));
            using var _otherLogin = await _client.GetAsync("/test/sign-in/Administrator?owner=another-admin");
            using var _otherDownload = await _client.GetAsync(_url);
            Assert.Equal(HttpStatusCode.NotFound, _otherDownload.StatusCode);
            using var _adminLogin = await _client.GetAsync("/test/sign-in/Administrator");
            using var _response = await _client.GetAsync(_url);
            Assert.Equal(HttpStatusCode.OK, _response.StatusCode);
            Assert.Equal("application/zip", _response.Content.Headers.ContentType?.MediaType);
            Assert.Equal("attachment", _response.Content.Headers.ContentDisposition?.DispositionType);
            Assert.EndsWith(".zip", _response.Content.Headers.ContentDisposition?.FileNameStar);
            Assert.True(_response.Headers.CacheControl?.NoStore);
            var _downloadPath = Path.Combine(_directory, "http-download.db");
            await using var _zipStream = await _response.Content.ReadAsStreamAsync();
            using var _zip = new ZipArchive(_zipStream, ZipArchiveMode.Read);
            var _entry = Assert.Single(_zip.Entries);
            Assert.Equal("blazor-telemetry.db", _entry.FullName);
            _entry.ExtractToFile(_downloadPath);
            await AssertDatabase(_downloadPath);
        }
        finally
        {
            await _app.StopAsync();
        }
        Assert.Empty(Directory.GetFiles(_directory, ".blazor-telemetry-backup-*"));
    }

    [Fact]
    public async Task QueueIsBoundedDeduplicatesPendingRequestsAndSurvivesBrowserCancellation()
    {
        await using var _source = await CreateDatabase();
        var _options = new BlazorTelemetryOptions { ConnectionString = _source.ConnectionString };
        using var _service = new DatabaseBackupService(_options);
        using var _worker = new DatabaseBackupWorker(_service, _options, TimeProvider.System, NullLogger<DatabaseBackupWorker>.Instance);
        var _first = Assert.IsType<DatabaseBackupJob>(_worker.Enqueue("first"));
        Assert.Same(_first, _worker.Enqueue("first"));
        Assert.NotNull(_worker.Enqueue("second"));
        Assert.NotNull(_worker.Enqueue("third"));
        Assert.NotNull(_worker.Enqueue("fourth"));
        Assert.Null(_worker.Enqueue("fifth"));
        Assert.False(_first.Completion.IsCompleted);
        using var _browserCancellation = new CancellationTokenSource();
        await _browserCancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _first.Completion.WaitAsync(_browserCancellation.Token));
        await _worker.StartAsync(CancellationToken.None);
        try
        {
            Assert.True(await _first.Completion.WaitAsync(TimeSpan.FromSeconds(15)));
            Assert.Same(_first, _worker.Find("first"));
            await using var _stream = _worker.OpenDownload(_first.Id, "first", out _);
            Assert.NotNull(_stream);
            using var _zip = new ZipArchive(_stream, ZipArchiveMode.Read);
            var _path = Path.Combine(_directory, "queued.db");
            Assert.Single(_zip.Entries).ExtractToFile(_path);
            await AssertDatabase(_path);
        }
        finally
        {
            await _worker.StopAsync(CancellationToken.None);
        }
        Assert.Empty(Directory.GetFiles(_directory, ".blazor-telemetry-backup-*"));
        Assert.Null(_worker.Enqueue("after-shutdown"));
    }

    [Fact]
    public async Task FailedBackgroundBackupNotifiesTheCallerAndAllowsRetry()
    {
        var _options = new BlazorTelemetryOptions { ConnectionString = $"Data Source={Path.Combine(_directory, "source.db")};Pooling=False" };
        using var _service = new DatabaseBackupService(_options);
        using var _worker = new DatabaseBackupWorker(_service, _options, TimeProvider.System, NullLogger<DatabaseBackupWorker>.Instance);
        await _worker.StartAsync(CancellationToken.None);
        try
        {
            var _failed = Assert.IsType<DatabaseBackupJob>(_worker.Enqueue("owner"));
            Assert.False(await _failed.Completion.WaitAsync(TimeSpan.FromSeconds(15)));
            Assert.Null(_worker.OpenDownload(_failed.Id, "owner", out _));
            Assert.Empty(Directory.GetFiles(_directory));
            await using var _source = await CreateDatabase();
            var _retry = Assert.IsType<DatabaseBackupJob>(_worker.Enqueue("owner"));
            Assert.NotEqual(_failed.Id, _retry.Id);
            Assert.True(await _retry.Completion.WaitAsync(TimeSpan.FromSeconds(15)));
        }
        finally
        {
            await _worker.StopAsync(CancellationToken.None);
        }
        Assert.Empty(Directory.GetFiles(_directory, ".blazor-telemetry-backup-*"));
    }

    public void Dispose()
    {
        foreach (var _path in Directory.GetFiles(_directory))
        {
            File.Delete(_path);
        }
        Directory.Delete(_directory);
    }

    [Fact]
    public async Task ExpiredArchivesAreUnavailableAndCleanedUpIncludingFilesFromAPreviousRun()
    {
        await using var _source = await CreateDatabase();
        var _options = new BlazorTelemetryOptions { ConnectionString = _source.ConnectionString };
        var _clock = new BackupTestTimeProvider();
        var _orphanPath = Path.Combine(_directory, $".blazor-telemetry-backup-{Guid.NewGuid():N}.zip");
        await File.WriteAllTextAsync(_orphanPath, "expired archive");
        File.SetLastWriteTimeUtc(_orphanPath, _clock.GetUtcNow().AddHours(-1).UtcDateTime);
        using var _service = new DatabaseBackupService(_options);
        using var _worker = new DatabaseBackupWorker(_service, _options, _clock, NullLogger<DatabaseBackupWorker>.Instance);
        await _worker.StartAsync(CancellationToken.None);
        try
        {
            var _job = Assert.IsType<DatabaseBackupJob>(_worker.Enqueue("owner"));
            Assert.True(await _job.Completion.WaitAsync(TimeSpan.FromSeconds(15)));
            Assert.False(File.Exists(_orphanPath));
            Assert.Single(Directory.GetFiles(_directory, "*.zip"));
            _clock.Advance(TimeSpan.FromMinutes(16));
            Assert.Null(_worker.Find("owner"));
            Assert.Null(_worker.FindAvailableArchive("owner"));
            Assert.Null(_worker.OpenDownload(_job.Id, "owner", out _));
            // A new queued job wakes the cleanup loop without a real-time delay.
            var _next = Assert.IsType<DatabaseBackupJob>(_worker.Enqueue("another-owner"));
            Assert.True(await _next.Completion.WaitAsync(TimeSpan.FromSeconds(15)));
            Assert.Single(Directory.GetFiles(_directory, "*.zip"));
            Assert.Null(_worker.Find("owner"));
        }
        finally
        {
            await _worker.StopAsync(CancellationToken.None);
        }
        Assert.Empty(Directory.GetFiles(_directory, ".blazor-telemetry-backup-*"));
    }

    private async Task<SqliteConnection> CreateDatabase()
    {
        var _connection = new SqliteConnection($"Data Source={Path.Combine(_directory, "source.db")};Pooling=False");
        await _connection.OpenAsync();
        await using var _command = _connection.CreateCommand();
        _command.CommandText = "PRAGMA journal_mode=WAL; PRAGMA wal_autocheckpoint=0; CREATE TABLE BackupTest (Value TEXT); INSERT INTO BackupTest VALUES ('committed WAL data');";
        await _command.ExecuteNonQueryAsync();
        return _connection;
    }

    private static async Task AssertDatabase(string path)
    {
        await using var _connection = new SqliteConnection($"Data Source={path};Mode=ReadOnly;Pooling=False");
        await _connection.OpenAsync();
        await using var _command = _connection.CreateCommand();
        _command.CommandText = "PRAGMA integrity_check;";
        Assert.Equal("ok", await _command.ExecuteScalarAsync());
        _command.CommandText = "SELECT Value FROM BackupTest;";
        Assert.Equal("committed WAL data", await _command.ExecuteScalarAsync());
        _command.CommandText = "SELECT COUNT(*) FROM BackupTest;";
        Assert.Equal(1L, await _command.ExecuteScalarAsync());
    }
}
