namespace BlazorTelemetry.AspNetCore;

public static class TelemetryApplicationBuilderExtensions
{
    public static IApplicationBuilder UseBlazorTelemetry(this IApplicationBuilder application)
    {
        application.UseRequestDecompression();
        application.UseMiddleware<RequestTelemetryMiddleware>();
        return application;
    }
}
