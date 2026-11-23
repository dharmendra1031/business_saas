using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BusinessSaas.Api.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace BusinessSaas.Tests;

public sealed class OwnerDashboardIntegrationTests
{
    [Fact]
    public async Task Cash_summary_uses_local_business_day_and_never_crosses_tenants_or_roles()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<DbContextOptions<AppDbContext>>();
                services.RemoveAll<IDbContextOptionsConfiguration<AppDbContext>>();
                services.AddDbContext<AppDbContext>(options => options.UseSqlite(connection));
            });
        });

        using var owner = factory.CreateClient();
        using var outsider = factory.CreateClient();
        using var cashier = factory.CreateClient();
        using var manager = factory.CreateClient();
        using var guest = factory.CreateClient();
        await Login(owner, "dashboard-owner@example.test");
        await Login(outsider, "dashboard-other@example.test");
        await Login(cashier, "dashboard-cashier@example.test");
        await Login(manager, "dashboard-manager@example.test");

        var businessId = await BusinessFixtures.SeedAsync(factory.Services, owner, "Owner Shop", "owner-dashboard-key-2026");
        var otherId = await BusinessFixtures.SeedAsync(factory.Services, outsider, "Other Shop", "other-dashboard-key-2026");

        long todayId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var ownerId = await db.Users.Where(x => x.Email == "dashboard-owner@example.test")
                .Select(x => x.Id).SingleAsync();
            var cashierId = await db.Users.Where(x => x.Email == "dashboard-cashier@example.test")
                .Select(x => x.Id).SingleAsync();
            var managerId = await db.Users.Where(x => x.Email == "dashboard-manager@example.test")
                .Select(x => x.Id).SingleAsync();
            var otherOwnerId = await db.Users.Where(x => x.Email == "dashboard-other@example.test")
                .Select(x => x.Id).SingleAsync();
            var locationId = await db.BusinessLocations.Where(x => x.BusinessId == businessId)
                .Select(x => x.Id).SingleAsync();
            var otherLocationId = await db.BusinessLocations.Where(x => x.BusinessId == otherId)
                .Select(x => x.Id).SingleAsync();
            db.BusinessUsers.AddRange(
                new BusinessUser { BusinessId = businessId, UserId = cashierId, RoleCode = "Cashier" },
                new BusinessUser { BusinessId = businessId, UserId = managerId, RoleCode = "Manager" });

            var zone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Kolkata");
            var localDate = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, zone).Date;
            var startUtc = TimeZoneInfo.ConvertTimeToUtc(localDate, zone);
            var today = new RetailCashSale
            {
                BusinessId = businessId, LocationId = locationId, CashierId = ownerId,
                RequestKey = "dashboard-today-unique", CurrencyCode = "INR", Total = 125.75m,
                Tendered = 130m, PostedAtUtc = DateTime.UtcNow
            };
            db.RetailCashSales.AddRange(
                today,
                new RetailCashSale
                {
                    BusinessId = businessId, LocationId = locationId, CashierId = ownerId,
                    RequestKey = "dashboard-yesterday-unique", CurrencyCode = "INR", Total = 990m,
                    Tendered = 990m, PostedAtUtc = startUtc.AddTicks(-1)
                },
                new RetailCashSale
                {
                    BusinessId = businessId, LocationId = locationId, CashierId = ownerId,
                    RequestKey = "dashboard-future-unique", CurrencyCode = "INR", Total = 880m,
                    Tendered = 880m, PostedAtUtc = DateTime.UtcNow.AddDays(1)
                },
                new RetailCashSale
                {
                    BusinessId = otherId, LocationId = otherLocationId, CashierId = otherOwnerId,
                    RequestKey = "dashboard-other-unique", CurrencyCode = "INR", Total = 777m,
                    Tendered = 777m, PostedAtUtc = DateTime.UtcNow
                });
            await db.SaveChangesAsync();
            todayId = today.Id;
        }

        var response = await owner.GetFromJsonAsync<JsonElement>(
            $"/api/businesses/{businessId}/dashboard");
        var summary = response.GetProperty("cashSalesToday");
        Assert.Equal(125.75m, summary.GetProperty("total").GetDecimal());
        Assert.Equal(1, summary.GetProperty("billCount").GetInt32());
        Assert.Equal(2, summary.GetProperty("recentReceipts").GetArrayLength());
        Assert.Equal(todayId, summary.GetProperty("recentReceipts")[0].GetProperty("id").GetInt64());

        var cashierData = await cashier.GetFromJsonAsync<JsonElement>(
            $"/api/businesses/{businessId}/dashboard");
        Assert.Equal(JsonValueKind.Null, cashierData.GetProperty("cashSalesToday").ValueKind);

        var managerData = await manager.GetFromJsonAsync<JsonElement>(
            $"/api/businesses/{businessId}/dashboard");
        Assert.Equal("Manager", managerData.GetProperty("roleCode").GetString());
        Assert.Equal(JsonValueKind.Null, managerData.GetProperty("cashSalesToday").ValueKind);
        Assert.Equal(JsonValueKind.Null, managerData.GetProperty("stockAlerts").ValueKind);
        Assert.Equal(0, managerData.GetProperty("pendingApprovals").GetInt32());

        var otherData = await outsider.GetFromJsonAsync<JsonElement>(
            $"/api/businesses/{otherId}/dashboard");
        Assert.Equal(777m, otherData.GetProperty("cashSalesToday").GetProperty("total").GetDecimal());
        Assert.Equal(1, otherData.GetProperty("cashSalesToday").GetProperty("billCount").GetInt32());

        Assert.Equal(HttpStatusCode.OK,
            (await owner.GetAsync($"/api/businesses/{businessId}/staff")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await manager.GetAsync($"/api/businesses/{businessId}/staff")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await cashier.GetAsync($"/api/businesses/{businessId}/staff")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await outsider.GetAsync($"/api/businesses/{businessId}/staff")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await guest.GetAsync($"/api/businesses/{businessId}/staff")).StatusCode);

        Assert.Equal(HttpStatusCode.NotFound,
            (await outsider.GetAsync($"/api/businesses/{businessId}/dashboard")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await guest.GetAsync($"/api/businesses/{businessId}/dashboard")).StatusCode);
    }

    private static async Task Login(HttpClient client, string email)
    {
        Assert.Equal(HttpStatusCode.OK, (await Send(client, "/api/auth/register",
            new { email, password = "TestPassword123!" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await Send(client, "/api/auth/login",
            new { email, password = "TestPassword123!" })).StatusCode);
    }

    private static async Task<HttpResponseMessage> Send(HttpClient client, string path, object body)
    {
        var csrf = await client.GetFromJsonAsync<JsonElement>("/api/auth/csrf");
        using var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = JsonContent.Create(body)
        };
        request.Headers.Add("X-CSRF-TOKEN", csrf.GetProperty("token").GetString()!);
        return await client.SendAsync(request);
    }
}
