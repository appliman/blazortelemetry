using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace BlazorTelemetry.Host.Data;

public sealed class IdentityBootstrapService(
    IServiceProvider serviceProvider,
    IOptions<BootstrapAdminOptions> options,
    ILogger<IdentityBootstrapService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await context.Database.MigrateAsync(cancellationToken);
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        foreach (var roleName in new[] { "Reader", "Administrator" })
        {
            if (!await roleManager.RoleExistsAsync(roleName))
            {
                await roleManager.CreateAsync(new IdentityRole(roleName));
            }
        }

        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        await MigrateLegacyRole(roleManager, userManager, "Lecteur", "Reader");
        await MigrateLegacyRole(roleManager, userManager, "Administrateur", "Administrator");

        if (string.IsNullOrWhiteSpace(options.Value.Email) || string.IsNullOrWhiteSpace(options.Value.Password))
        {
            logger.LogWarning("No initial administrator is configured. Set BootstrapAdmin__Email and BootstrapAdmin__Password.");
            return;
        }

        var user = await userManager.FindByEmailAsync(options.Value.Email);
        if (user is null)
        {
            user = new ApplicationUser { UserName = options.Value.Email, Email = options.Value.Email, EmailConfirmed = true };
            var result = await userManager.CreateAsync(user, options.Value.Password);
            if (!result.Succeeded)
            {
                logger.LogError("Unable to create the initial administrator: {Errors}", string.Join(", ", result.Errors.Select(error => error.Description)));
                return;
            }
        }

        if (!await userManager.IsInRoleAsync(user, "Administrator"))
        {
            await userManager.AddToRoleAsync(user, "Administrator");
        }
    }

    private static async Task MigrateLegacyRole(
        RoleManager<IdentityRole> roleManager,
        UserManager<ApplicationUser> userManager,
        string legacyRole,
        string targetRole)
    {
        if (!await roleManager.RoleExistsAsync(legacyRole))
        {
            return;
        }

        var users = await userManager.GetUsersInRoleAsync(legacyRole);
        foreach (var user in users)
        {
            if (!await userManager.IsInRoleAsync(user, targetRole))
            {
                await userManager.AddToRoleAsync(user, targetRole);
            }

            await userManager.RemoveFromRoleAsync(user, legacyRole);
        }

        var role = await roleManager.FindByNameAsync(legacyRole);
        if (role is not null)
        {
            await roleManager.DeleteAsync(role);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
