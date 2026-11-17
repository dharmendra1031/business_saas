using System.ComponentModel.DataAnnotations;
using BusinessSaas.Api.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace BusinessSaas.Api.Features;

public static class PlatformOwnerBootstrap
{
    public static async Task<long> CreateAsync(IServiceProvider services, IConfiguration configuration,
        string? email, string? password)
    {
        if (long.TryParse(configuration["Platform:OwnerUserId"], out var existingOwnerId) &&
            existingOwnerId > 0)
            throw new InvalidOperationException("Platform owner is already configured.");

        var address = email?.Trim() ?? "";
        if (address.Length > 256 || !new EmailAddressAttribute().IsValid(address))
            throw new InvalidOperationException("A valid PLATFORM_BOOTSTRAP_EMAIL is required.");
        if (string.IsNullOrWhiteSpace(password) || password.Length < 16)
            throw new InvalidOperationException("PLATFORM_BOOTSTRAP_PASSWORD must have at least 16 characters.");

        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        if (!await db.Database.CanConnectAsync() ||
            db.Database.IsSqlServer() && (await db.Database.GetPendingMigrationsAsync()).Any())
            throw new InvalidOperationException("Apply migrations and verify database access before bootstrapping.");

        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        if (await users.FindByEmailAsync(address) is not null)
            throw new InvalidOperationException("This account already exists. Choose a dedicated admin email.");

        // Only a trusted operator may use this offline command after verifying the mailbox.
        var account = new AppUser { UserName = address, Email = address, EmailConfirmed = true };
        var result = await users.CreateAsync(account, password);
        if (!result.Succeeded)
            throw new InvalidOperationException("Account creation failed: " +
                string.Join(" ", result.Errors.Select(x => x.Description)));

        return account.Id;
    }
}
