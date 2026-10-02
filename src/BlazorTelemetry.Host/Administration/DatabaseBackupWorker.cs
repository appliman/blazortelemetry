using System.IO.Compression;
using BlazorTelemetry.Core;
using Microsoft.Data.Sqlite;

namespace BlazorTelemetry.Host.Administration;

public sealed class DatabaseBackupWorker(
    DatabaseBackupService backupService,
    BlazorTelemetryOptions options,
    TimeProvider timeProvider,
    ILogger<DatabaseBackupWorker> logger) : BackgroundService
{
    private const int MAXIMUM_JOBS = 4;
    private readonly object _gate = new();
    private readonly Dictionary<Guid, DatabaseBackupJob> _jobs = [];
    private readonly Queue<DatabaseBackupJob> _queue = new();
    private readonly SemaphoreSlim _signal = new(0, MAXIMUM_JOBS);
    private bool _stopping;

    public DatabaseBackupJob? Enqueue(string owner)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);
        lock (_gate)
        {
            if (_stopping)
            {
                return null;
            }
            var _previous = _jobs.Values.FirstOrDefault(_job => _job.Owner == owner);
            if (_previous is not null)
            {
                if (!_previous.Completion.IsCompleted)
                {
                    return _previous;
                }
                Remove(_previous);
            }
            if (_jobs.Count >= MAXIMUM_JOBS)
            {
                return null;
            }
            var _job = new DatabaseBackupJob(owner);
            _jobs.Add(_job.Id, _job);
            _queue.Enqueue(_job);
            _signal.Release();
            return _job;
        }
    }

    public DatabaseBackupJob? Find(string owner)
    {
        lock (_gate)
        {
            return _jobs.Values.FirstOrDefault(_job => _job.Owner == owner && _job.ExpiresUtc > timeProvider.GetUtcNow());
        }
    }

    public FileStream? OpenDownload(Guid id, string owner, out string? fileName)
    {
        lock (_gate)
        {
            fileName = null;
            if (!_jobs.TryGetValue(id, out var _job) || _job.Owner != owner ||
                !_job.Completion.IsCompletedSuccessfully || !_job.Completion.Result ||
                _job.ExpiresUtc <= timeProvider.GetUtcNow() || !File.Exists(_job.FilePath))
            {
                return null;
            }
            var _stream = new FileStream(_job.FilePath!, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete,
                64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
            fileName = _job.FileName;
            return _stream;
        }
    }

    public DatabaseBackupJob? FindAvailableArchive(string owner)
    {
        lock (_gate)
        {
            return _jobs.Values.FirstOrDefault(_job => _job.Owner == owner &&
                _job.Completion.IsCompletedSuccessfully && _job.Completion.Result &&
                _job.ExpiresUtc > timeProvider.GetUtcNow() && File.Exists(_job.FilePath));
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            CleanupOrphanedArchives();
            while (!stoppingToken.IsCancellationRequested)
            {
                await _signal.WaitAsync(TimeSpan.FromMinutes(1), stoppingToken);
                DatabaseBackupJob? _job;
                lock (_gate)
                {
                    foreach (var _expired in _jobs.Values.Where(_item => _item.ExpiresUtc <= timeProvider.GetUtcNow()).ToArray())
                    {
                        Remove(_expired);
                    }
                    _queue.TryDequeue(out _job);
                }
                CleanupOrphanedArchives();
                if (_job is not null)
                {
                    await Process(_job, stoppingToken);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        finally
        {
            lock (_gate)
            {
                _stopping = true;
                foreach (var _job in _jobs.Values.ToArray())
                {
                    _job.Complete(false);
                    Remove(_job);
                }
                _queue.Clear();
            }
        }
    }

    public override void Dispose()
    {
        base.Dispose();
        _signal.Dispose();
    }

    private async Task Process(DatabaseBackupJob job, CancellationToken cancellationToken)
    {
        try
        {
            await using (var _snapshot = await backupService.CreateBackup(cancellationToken))
            {
                var _directory = Path.GetDirectoryName(Path.GetFullPath(new SqliteConnectionStringBuilder(options.ConnectionString).DataSource))!;
                job.FilePath = Path.Combine(_directory, $".blazor-telemetry-backup-{job.Id:N}.zip");
                var _fileOptions = new FileStreamOptions
                {
                    Mode = FileMode.CreateNew,
                    Access = FileAccess.Write,
                    Options = FileOptions.Asynchronous,
                    BufferSize = 64 * 1024
                };
                if (!OperatingSystem.IsWindows())
                {
                    _fileOptions.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
                }
                await using var _output = new FileStream(job.FilePath, _fileOptions);
                using var _archive = new ZipArchive(_output, ZipArchiveMode.Create, leaveOpen: true);
                var _entry = _archive.CreateEntry("blazor-telemetry.db", CompressionLevel.Fastest);
                await using var _entryStream = _entry.Open();
                await _snapshot.CopyToAsync(_entryStream, cancellationToken);
            }
            cancellationToken.ThrowIfCancellationRequested();
            lock (_gate)
            {
                job.ExpiresUtc = timeProvider.GetUtcNow().AddMinutes(15);
                job.Complete(true);
            }
        }
        catch (Exception _exception)
        {
            if (_exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                logger.LogError(_exception, "Failed to prepare the SQLite ZIP download.");
            }
            lock (_gate)
            {
                DeleteArchive(job);
                job.ExpiresUtc = timeProvider.GetUtcNow().AddMinutes(15);
                job.Complete(false);
            }
        }
    }

    private void Remove(DatabaseBackupJob job)
    {
        DeleteArchive(job);
        _jobs.Remove(job.Id);
    }

    private void DeleteArchive(DatabaseBackupJob job)
    {
        if (job.FilePath is null)
        {
            return;
        }
        try
        {
            File.Delete(job.FilePath);
        }
        catch (Exception _exception)
        {
            logger.LogWarning(_exception, "Failed to delete a temporary database ZIP archive.");
        }
    }

    private void CleanupOrphanedArchives()
    {
        try
        {
            var _directory = Path.GetDirectoryName(Path.GetFullPath(new SqliteConnectionStringBuilder(options.ConnectionString).DataSource))!;
            var _cutoff = timeProvider.GetUtcNow().AddMinutes(-15).UtcDateTime;
            foreach (var _path in Directory.EnumerateFiles(_directory, ".blazor-telemetry-backup-*.zip"))
            {
                lock (_gate)
                {
                    if (_jobs.Values.Any(_job => _job.FilePath == _path))
                    {
                        continue;
                    }
                }
                if (File.GetLastWriteTimeUtc(_path) <= _cutoff)
                {
                    File.Delete(_path);
                }
            }
        }
        catch (Exception _exception)
        {
            logger.LogWarning(_exception, "Failed to clean up expired database ZIP archives.");
        }
    }
}
