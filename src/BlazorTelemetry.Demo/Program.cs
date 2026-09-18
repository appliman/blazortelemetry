using BlazorTelemetry.AspNetCore;
using BlazorTelemetry.Client;
using BlazorTelemetry.Demo;
using BlazorTelemetry.Demo.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.DataProtection;
using OpenTelemetry.Exporter;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
var dataPath = Path.Combine(builder.Environment.ContentRootPath, "Data");
Directory.CreateDirectory(dataPath);
builder.Services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(dataPath, "keys"))).SetApplicationName("BlazorTelemetry.Demo");
builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<AuthenticationStateProvider, DemoAuthenticationStateProvider>();
builder.Services.AddAuthorizationBuilder()
    .AddPolicy("BlazorTelemetryReader", policy => policy.RequireAssertion(_ => true))
    .AddPolicy("BlazorTelemetryAdministrator", policy => policy.RequireAssertion(_ => true));
builder.Services.AddBlazorTelemetry(builder.Configuration, options => options.RequireIngestionKey = false);
builder.Services.AddSingleton<DemoTelemetryService>();
builder.Services.AddHttpClient("demo-api", client => client.BaseAddress = new Uri(builder.Configuration["DemoApiUrl"] ?? "http://localhost:5188"));

var otlpEndpoint = new Uri(builder.Configuration["OtlpEndpoint"] ?? "http://localhost:5279");
builder.Services.AddBlazorTelemetry(options =>
{
    options.ServiceName = "BlazorTelemetry.Demo";
    options.ServiceVersion = "1.0.1";
    options.DeploymentEnvironment = builder.Environment.EnvironmentName;
    options.Endpoint = otlpEndpoint;
    options.Protocol = OtlpExportProtocol.HttpProtobuf;
    options.AddSource(DemoTelemetryService.ACTIVITY_SOURCE_NAME);
    options.AddMeter(DemoTelemetryService.METER_NAME);
});
builder.Logging.AddFilter("BlazorTelemetry.AspNetCore", LogLevel.Warning);
builder.Logging.AddFilter("BlazorTelemetry.Sqlite", LogLevel.Warning);
builder.Logging.AddFilter("Microsoft.EntityFrameworkCore", LogLevel.Warning);
builder.Logging.AddFilter("System.Net.Http.HttpClient.Otlp", LogLevel.Warning);

var app = builder.Build();
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}
app.UseWhen(
    context => !context.Request.Path.StartsWithSegments("/v1") && !context.Request.Path.StartsWithSegments("/blazor-telemetry"),
    branch => branch.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true));
app.UseBlazorTelemetry();
app.UseHttpsRedirection();
app.UseAntiforgery();
app.MapStaticAssets();
app.MapBlazorTelemetry();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode().AddAdditionalAssemblies(typeof(BlazorTelemetry.TelemetryDashboard).Assembly);
app.Run();
