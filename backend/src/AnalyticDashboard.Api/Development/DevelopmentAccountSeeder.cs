using AnalyticDashboard.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;

namespace AnalyticDashboard.Api.Development;

public static class DevelopmentAccountSeeder
{
    public static async Task SeedAsync(
        IServiceProvider services,
        IConfiguration configuration)
    {
        using var scope = services.CreateScope();

        var userManager = scope.ServiceProvider
            .GetRequiredService<UserManager<ApplicationUser>>();

        var email = configuration["DevSeed:Email"]
            ?? throw new InvalidOperationException("DevSeed:Email is not configured.");

        var password = configuration["DevSeed:Password"]
            ?? throw new InvalidOperationException("DevSeed:Password is not configured.");

        if (await userManager.FindByEmailAsync(email) is not null)
        {
            return;
        }

        var result = await userManager.CreateAsync(
            new ApplicationUser
            {
                Email = email,
                UserName = email,
                DisplayName = "Developer",
                EmailConfirmed = true
            },
            password
        );

        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                $"Failed to seed the developer account: {string.Join(
                    ", ",
                    result.Errors.Select(error =>
                        $"{error.Code}: {error.Description}"
                    )
                )}"
            );
        }
    }
}
