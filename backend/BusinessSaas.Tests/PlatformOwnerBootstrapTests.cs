using BusinessSaas.Api.Data;
using BusinessSaas.Api.Features;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace BusinessSaas.Tests;

public sealed class PlatformOwnerBootstrapTests
{
    [Fact]
    public async Task Bootstrap_creates_dedicated_numeric_owner_without_business_membership()
    {
        using var database = new SqliteConnection("Data Source=:memory:");
        await database.OpenAsync();
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<DbContextOptions<AppDbContext>>();
                services.RemoveAll<IDbContextOptionsConfiguration<AppDbContext>>();
                services.AddDbContext<AppDbContext>(options => options.UseSqlite(database));
            });
        });
        using var client = factory.CreateClient();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection().Build();
        const string email = "bootstrap-owner@example.test";
        const string password = "Secure-Owner-Password-2026!";

        var id = await PlatformOwnerBootstrap.CreateAsync(factory.Services, configuration, email, password);
        Assert.True(id > 0);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            var account = await users.FindByEmailAsync(email);
            Assert.NotNull(account);
            Assert.Equal(id, account.Id);
            Assert.True(account.EmailConfirmed);
            Assert.True(await users.CheckPasswordAsync(account, password));
            Assert.False(await db.BusinessUsers.AnyAsync(x => x.UserId == id));
        }

        configuration["Platform:OwnerUserId"] = id.ToString();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            PlatformOwnerBootstrap.CreateAsync(factory.Services, configuration,
                "another-owner@example.test", password));
        Assert.Equal(1, await CountUsers(factory.Services));
    }

    [Fact]
    public async Task Bootstrap_rejects_invalid_credentials_and_existing_accounts()
    {
        using var database = new SqliteConnection("Data Source=:memory:");
        await database.OpenAsync();
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<DbContextOptions<AppDbContext>>();
                services.RemoveAll<IDbContextOptionsConfiguration<AppDbContext>>();
                services.AddDbContext<AppDbContext>(options => options.UseSqlite(database));
            });
        });
        using var client = factory.CreateClient();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection().Build();
        const string email = "bootstrap-owner@example.test";
        const string password = "Secure-Owner-Password-2026!";

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            PlatformOwnerBootstrap.CreateAsync(factory.Services, configuration, "not-an-email", password));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            PlatformOwnerBootstrap.CreateAsync(factory.Services, configuration, email, "short"));
        Assert.Equal(0, await CountUsers(factory.Services));

        await PlatformOwnerBootstrap.CreateAsync(factory.Services, configuration, email, password);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            PlatformOwnerBootstrap.CreateAsync(factory.Services, configuration, email, password));
        Assert.Equal(1, await CountUsers(factory.Services));
    }

    private static async Task<int> CountUsers(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Users.CountAsync();
    }
}
