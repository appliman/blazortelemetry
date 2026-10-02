using BlazorTelemetry.Core;
using Microsoft.Data.Sqlite;

namespace BlazorTelemetry.Host.Administration;

public sealed class DatabaseBackupService(BlazorTelemetryOptions options) : IDisposable
{
    private readonly SemaphoreSlim _backupLock = new(1, 1);

    public async Task<FileStream> CreateBackup(CancellationToken cancellationToken)
    {
        await _backupLock.WaitAsync(cancellationToken);
        string? _backupPath = null;
        try
        {
            var _sourceOptions = new SqliteConnectionStringBuilder(options.ConnectionString)
            {
                Mode = SqliteOpenMode.ReadOnly,
                Pooling = false
            };
            await using var _source = new SqliteConnection(_sourceOptions.ToString());
            await _source.OpenAsync(cancellationToken);
            var _directory = Path.GetDirectoryName(Path.GetFullPath(_source.DataSource))!;
            _backupPath = Path.Combine(_directory, $".blazor-telemetry-backup-{Guid.NewGuid():N}.db");
            var _fileOptions = new FileStreamOptions
            {
                Mode = FileMode.CreateNew,
                Access = FileAccess.Write
            };
            if (!OperatingSystem.IsWindows())
            {
                _fileOptions.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            }
            await using (var _file = new FileStream(_backupPath, _fileOptions))
            {
            }

            var _targetOptions = new SqliteConnectionStringBuilder
            {
                DataSource = _backupPath,
                Pooling = false
            };
            await using (var _target = new SqliteConnection(_targetOptions.ToString()))
            {
                await _target.OpenAsync(cancellationToken);
                // SQLite's backup API includes committed WAL data in a consistent snapshot.
                _source.BackupDatabase(_target);
            }
            cancellationToken.ThrowIfCancellationRequested();
            return new FileStream(_backupPath, FileMode.Open, FileAccess.Read, FileShare.Read,
                64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan | FileOptions.DeleteOnClose);
        }
        catch
        {
            if (_backupPath is not null)
            {
                File.Delete(_backupPath);
            }
            throw;
        }
        finally
        {
            _backupLock.Release();
        }
    }

    public void Dispose()
    {
        _backupLock.Dispose();
    }
}
