using System.Net;
using System.Security.Claims;
using BlazorTelemetry.Core;
using BlazorTelemetry.Host.Administration;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

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
    public async Task DownloadRequiresAdministratorAndAntiforgeryAndReturnsAValidDatabase()
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
        await using var _app = _builder.Build();
        _app.UseAuthentication();
        _app.UseAuthorization();
        _app.UseAntiforgery();
        _app.MapControllers();
        _app.MapGet("/test/sign-in/{role}", async (HttpContext _context, string role) =>
        {
            var _identity = new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, "backup-test"), new Claim(ClaimTypes.Role, role)],
                CookieAuthenticationDefaults.AuthenticationScheme);
            await _context.SignInAsync(new ClaimsPrincipal(_identity));
            return Results.Ok();
        });
        _app.MapGet("/test/token", (HttpContext _context, IAntiforgery _antiforgery) =>
            Results.Text(_antiforgery.GetAndStoreTokens(_context).RequestToken!));
        await _app.StartAsync();
        try
        {
            using var _handler = new HttpClientHandler { AllowAutoRedirect = false, UseProxy = false };
            using var _client = new HttpClient(_handler) { BaseAddress = new Uri(_app.Urls.Single()) };
            using var _anonymous = await _client.PostAsync("/administration/database/download", null);
            Assert.True(_anonymous.StatusCode is HttpStatusCode.Redirect or HttpStatusCode.Unauthorized);
            using var _readerLogin = await _client.GetAsync("/test/sign-in/Reader");
            using var _reader = await _client.PostAsync("/administration/database/download", null);
            Assert.True(_reader.StatusCode is HttpStatusCode.Redirect or HttpStatusCode.Forbidden);
            using var _adminLogin = await _client.GetAsync("/test/sign-in/Administrator");
            using var _missingToken = await _client.PostAsync("/administration/database/download", null);
            Assert.Equal(HttpStatusCode.BadRequest, _missingToken.StatusCode);
            var _token = await _client.GetStringAsync("/test/token");
            using var _form = new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = _token });
            using var _response = await _client.PostAsync("/administration/database/download", _form);
            Assert.Equal(HttpStatusCode.OK, _response.StatusCode);
            Assert.Equal("application/vnd.sqlite3", _response.Content.Headers.ContentType?.MediaType);
            Assert.Equal("attachment", _response.Content.Headers.ContentDisposition?.DispositionType);
            Assert.EndsWith(".db", _response.Content.Headers.ContentDisposition?.FileNameStar);
            Assert.True(_response.Headers.CacheControl?.NoStore);
            var _downloadPath = Path.Combine(_directory, "http-download.db");
            await File.WriteAllBytesAsync(_downloadPath, await _response.Content.ReadAsByteArrayAsync());
            await AssertDatabase(_downloadPath);
        }
        finally
        {
            await _app.StopAsync();
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
