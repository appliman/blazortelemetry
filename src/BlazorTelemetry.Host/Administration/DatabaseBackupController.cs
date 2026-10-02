using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BlazorTelemetry.Host.Administration;

[Authorize(Policy = "BlazorTelemetryAdministrator")]
[Route("administration/database")]
public sealed class DatabaseBackupController(
    DatabaseBackupService backupService,
    ILogger<DatabaseBackupController> logger) : Controller
{
    [HttpPost("download", Order = -100)]
    [ValidateAntiForgeryToken]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Download(CancellationToken cancellationToken)
    {
        try
        {
            var _stream = await backupService.CreateBackup(cancellationToken);
            Response.RegisterForDisposeAsync(_stream);
            return File(_stream, "application/vnd.sqlite3",
                $"blazor-telemetry-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}.db");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new EmptyResult();
        }
        catch (Exception _exception)
        {
            logger.LogError(_exception, "Failed to create the SQLite database download.");
            return Problem("The database backup could not be created. Please try again.", statusCode: 500);
        }
    }
}
