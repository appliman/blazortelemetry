using BlazorTelemetry.Core;

namespace BlazorTelemetry.Host.Mcp;

public sealed class McpKeyMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, IMediator repository)
    {
        var authorization = context.Request.Headers.Authorization.ToString();
        if (!authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        var supplied = authorization[7..].Trim();
        if (supplied.Length is < 16 or > 256)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        var key = await repository.FindActiveMcpReadKey(IngestionKeyGenerator.ComputeHash(supplied), context.RequestAborted);
        if (key is null)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        await next(context);
    }
}
