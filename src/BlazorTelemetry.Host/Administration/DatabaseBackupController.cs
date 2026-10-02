using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace BlazorTelemetry.Host.Administration;

[Authorize(Policy = "BlazorTelemetryAdministrator")]
[Route("administration/database")]
public sealed class DatabaseBackupController(
    DatabaseBackupWorker backupWorker,
    ILogger<DatabaseBackupController> logger) : Controller
{
    [HttpGet("download/{id:guid}", Order = -100)]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public IActionResult Download(Guid id)
    {
        try
        {
            var _owner = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(_owner))
            {
                return Forbid();
            }
            var _stream = backupWorker.OpenDownload(id, _owner, out var _fileName);
            if (_stream is null)
            {
                return NotFound();
            }
            Response.RegisterForDisposeAsync(_stream);
            return File(_stream, "application/zip", _fileName);
        }
        catch (Exception _exception)
        {
            logger.LogError(_exception, "Failed to open the SQLite ZIP download.");
            return Problem("The database ZIP could not be downloaded. Please try again.", statusCode: 500);
        }
    }
}
