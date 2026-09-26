using BlazorTelemetry.Client;
using BlazorTelemetry.Demo;
using BlazorTelemetry.Demo.Components;
using Microsoft.AspNetCore.DataProtection;
using OpenTelemetry.Exporter;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
var dataPath = Path.Combine(builder.Environment.ContentRootPath, "Data");
Directory.CreateDirectory(dataPath);
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(dataPath, "keys")))
    .SetApplicationName("BlazorTelemetry.Demo");
builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddSingleton<DemoTelemetryService>();
builder.Services.AddHttpClient("demo-api", client => client.BaseAddress = new Uri(builder.Configuration["DemoApiUrl"] ?? "http://localhost:5188"));

var otlpEndpoint = new Uri(builder.Configuration["OtlpEndpoint"] ?? "http://localhost:8080");
builder.Services.AddBlazorTelemetry(options =>
{
    options.ServiceName = "BlazorTelemetry.Demo";
    options.ServiceVersion = "1.0.1";
    options.DeploymentEnvironment = builder.Environment.EnvironmentName;
    options.Endpoint = otlpEndpoint;
    options.Headers = builder.Configuration["OtlpHeaders"];
    options.Protocol = OtlpExportProtocol.HttpProtobuf;
    options.AddSource(DemoTelemetryService.ACTIVITY_SOURCE_NAME);
    options.AddMeter(DemoTelemetryService.METER_NAME);
});
builder.Logging.AddFilter("Microsoft.EntityFrameworkCore", LogLevel.Warning);
builder.Logging.AddFilter("System.Net.Http.HttpClient.Otlp", LogLevel.Warning);

var app = builder.Build();
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();
app.UseAntiforgery();
app.MapStaticAssets();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
app.Run();
