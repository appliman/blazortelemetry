using System.Threading.RateLimiting;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Blazor2fa;

public static class StartupExtensions
{
	public static WebApplicationBuilder AddBlazor2fa(this WebApplicationBuilder builder, Action<BlazorAuthConfiguration> options)
	{
		var settings = new BlazorAuthConfiguration();
		options(settings);

		return builder.AddBlazor2fa(settings);
	}

	public static WebApplicationBuilder AddBlazor2fa(this WebApplicationBuilder builder, BlazorAuthConfiguration settings)
	{
		builder.Services.AddSingleton(settings);
		builder.Services.TryAddSingleton(TimeProvider.System);
		builder.Services.AddSingleton<Blazor2faAuthenticationTicketStore>();

		builder.Services.AddMemoryCache();
		builder.Services.AddControllers();

		builder.Services.AddRateLimiter(options =>
		{
			options.AddFixedWindowLimiter("login", limiterOptions =>
			{
				limiterOptions.PermitLimit = 5;
				limiterOptions.Window = TimeSpan.FromMinutes(1);
				limiterOptions.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
				limiterOptions.QueueLimit = 0;
			});
		});

		return builder;
	}

	public static WebApplication UseBlazor2fa(this WebApplication app)
	{
		app.UseRateLimiter();
		app.MapControllers();
		return app;
	}
}
