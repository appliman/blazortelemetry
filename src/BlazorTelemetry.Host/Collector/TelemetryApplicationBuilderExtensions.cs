namespace BlazorTelemetry.AspNetCore;

public static class TelemetryApplicationBuilderExtensions
{
    public static IApplicationBuilder UseBlazorTelemetry(this IApplicationBuilder application)
    {
        application.UseRequestDecompression();
        return application;
    }
}
