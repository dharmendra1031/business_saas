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

public sealed class BusinessFlowTests
{
    [Fact]
    public async Task Accounts_are_isolated_and_business_creation_is_idempotent()
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

        using var owner = factory.CreateClient();
        using var stranger = factory.CreateClient();

        var unauthenticated = await stranger.GetAsync("/api/businesses");
        Assert.Equal(HttpStatusCode.Unauthorized, unauthenticated.StatusCode);
        Assert.False(string.IsNullOrWhiteSpace(
            unauthenticated.Headers.GetValues("X-Request-Id").Single()));

        await RegisterAndLogin(owner, "owner@example.test");
        await RegisterAndLogin(stranger, "stranger@example.test");

        var authOptions = await owner.GetFromJsonAsync<JsonElement>("/api/auth/options");
        Assert.True(authOptions.GetProperty("canRegister").GetBoolean());
        Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync("/api/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await stranger.GetAsync("/api/auth/me")).StatusCode);
        // Unpublished templates must not appear in onboarding or be accepted by the API.
        long unsupportedTypeId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var unsupportedType = new BusinessType
            {
                Code = "SALON",
                Name = "Salon / Barber",
                IsSupported = false
            };
            db.BusinessTypes.Add(unsupportedType);
            await db.SaveChangesAsync();
            unsupportedTypeId = unsupportedType.Id;
        }

        var types = await owner.GetFromJsonAsync<JsonElement>("/api/business-types");
        Assert.Single(types.EnumerateArray());
        Assert.Equal("RETAIL", types[0].GetProperty("code").GetString());

        var unsupported = await Post(owner, "/api/businesses", new
        {
            name = "Unreleased Salon",
            businessTypeId = unsupportedTypeId,
            countryCode = "IN",
            currencyCode = "INR",
            timeZoneId = "Asia/Kolkata",
            requestKey = "unsupported-salon-20261111"
        });
        Assert.Equal(HttpStatusCode.BadRequest, unsupported.StatusCode);

        var markets = await owner.GetFromJsonAsync<JsonElement>("/api/business-markets");
        Assert.Equal(249, markets.GetArrayLength());
        Assert.Contains(markets.EnumerateArray(), item => item.GetProperty("code").GetString() == "IN");
        Assert.Contains(markets.EnumerateArray(), item => item.GetProperty("code").GetString() == "US");
        Assert.Contains(markets.EnumerateArray(), item => item.GetProperty("code").GetString() == "KW");

        var invalidMarket = await Post(owner, "/api/businesses", new
        {
            name = "Invalid Country Shop",
            businessTypeId = 1,
            countryCode = "XX",
            currencyCode = "USD",
            timeZoneId = "Unknown/Zone",
            requestKey = "invalid-market-20261110"
        });
        Assert.Equal(HttpStatusCode.BadRequest, invalidMarket.StatusCode);

        var invalidCurrency = await Post(owner, "/api/businesses", new
        {
            name = "Wrong Currency Shop",
            businessTypeId = 1,
            countryCode = "IN",
            currencyCode = "USD",
            timeZoneId = "Asia/Kolkata",
            requestKey = "invalid-currency-20261110"
        });
        Assert.Equal(HttpStatusCode.BadRequest, invalidCurrency.StatusCode);

        var usaMarket = await Post(owner, "/api/businesses", new
        {
            name = "West Coast Retail",
            businessTypeId = 1,
            countryCode = "US",
            currencyCode = "USD",
            timeZoneId = "America/Los_Angeles",
            requestKey = "united-states-20261110"
        });
        Assert.Equal(HttpStatusCode.Created, usaMarket.StatusCode);

        var creation = new
        {
            name = "Main Retail Shop",
            businessTypeId = 1,
            countryCode = "IN",
            currencyCode = "INR",
            timeZoneId = "Asia/Kolkata",
            requestKey = "request-main-20261109"
        };

        var first = await Post(owner, "/api/businesses", creation);
        Assert.True(first.StatusCode == HttpStatusCode.Created, await first.Content.ReadAsStringAsync());
        var firstId = (await first.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt64();
        Assert.True(firstId > 0);

        var retry = await Post(owner, "/api/businesses", creation);
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        var sameId = (await retry.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt64();
        Assert.Equal(firstId, sameId);

        var owned = await owner.GetFromJsonAsync<JsonElement>("/api/businesses");
        Assert.Equal(2, owned.GetArrayLength());

        var anotherOwned = await Post(owner, "/api/businesses", new
        {
            name = "Owner's Second Shop",
            businessTypeId = 1,
            countryCode = "KW",
            currencyCode = "KWD",
            timeZoneId = "Asia/Kuwait",
            requestKey = "request-second-owned-20261109"
        });
        Assert.Equal(HttpStatusCode.Created, anotherOwned.StatusCode);
        var otherOwnedId = (await anotherOwned.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id").GetInt64();
        Assert.NotEqual(firstId, otherOwnedId);

        owned = await owner.GetFromJsonAsync<JsonElement>("/api/businesses");
        Assert.Equal(3, owned.GetArrayLength());
        Assert.Equal(HttpStatusCode.OK,
            (await owner.GetAsync($"/api/businesses/{otherOwnedId}/dashboard")).StatusCode);
        var ownerDashboardData = await owner.GetFromJsonAsync<JsonElement>(
            $"/api/businesses/{otherOwnedId}/dashboard");
        Assert.True(ownerDashboardData.GetProperty("canOperate").GetBoolean());
        Assert.Equal("Trial", ownerDashboardData.GetProperty("subscription").GetProperty("status").GetString());
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var subscription = await db.BusinessSubscriptions
                .SingleAsync(x => x.BusinessId == otherOwnedId);
            subscription.TrialEndsAtUtc = DateTime.UtcNow.AddDays(-1);
            await db.SaveChangesAsync();
        }
        ownerDashboardData = await owner.GetFromJsonAsync<JsonElement>(
            $"/api/businesses/{otherOwnedId}/dashboard");
        Assert.False(ownerDashboardData.GetProperty("canOperate").GetBoolean());

        var second = await Post(stranger, "/api/businesses", new
        {
            name = "Another Shop",
            businessTypeId = 1,
            countryCode = "IN",
            currencyCode = "INR",
            timeZoneId = "Asia/Kolkata",
            requestKey = "request-other-20261109"
        });
        Assert.True(second.StatusCode == HttpStatusCode.Created, await second.Content.ReadAsStringAsync());
        var secondId = (await second.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt64();
        Assert.NotEqual(firstId, secondId);

        var ownerDashboard = await owner.GetAsync($"/api/businesses/{firstId}/dashboard");
        Assert.Equal(HttpStatusCode.OK, ownerDashboard.StatusCode);

        Assert.Equal(HttpStatusCode.NotFound,
            (await stranger.GetAsync($"/api/businesses/{firstId}/dashboard")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await owner.GetAsync($"/api/businesses/{secondId}/dashboard")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await stranger.GetAsync($"/api/businesses/{otherOwnedId}/dashboard")).StatusCode);

        var strangerList = await stranger.GetFromJsonAsync<JsonElement>("/api/businesses");
        Assert.Equal(1, strangerList.GetArrayLength());
        Assert.Equal(secondId, strangerList[0].GetProperty("id").GetInt64());

        // Membership removal must take effect without requiring the old cookie to expire.
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var member = await db.BusinessUsers.SingleAsync(x => x.BusinessId == firstId);
            db.BusinessUsers.Remove(member);
            await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.NotFound,
            (await owner.GetAsync($"/api/businesses/{firstId}/dashboard")).StatusCode);
        var remaining = await owner.GetFromJsonAsync<JsonElement>("/api/businesses");
        Assert.DoesNotContain(remaining.EnumerateArray(),
            item => item.GetProperty("id").GetInt64() == firstId);
    }

    [Fact]
    public async Task Business_staff_access_is_owner_managed_and_tenant_scoped()
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

        using var owner = factory.CreateClient();
        using var manager = factory.CreateClient();
        using var cashier = factory.CreateClient();
        using var outsider = factory.CreateClient();

        await RegisterAndLogin(owner, "team-owner@example.test");
        await RegisterAndLogin(manager, "team-manager@example.test");
        await RegisterAndLogin(cashier, "team-cashier@example.test");
        await RegisterAndLogin(outsider, "team-outsider@example.test");

        var created = await Post(owner, "/api/businesses", new
        {
            name = "Team Retail",
            businessTypeId = 1,
            countryCode = "IN",
            currencyCode = "INR",
            timeZoneId = "Asia/Kolkata",
            requestKey = "team-owner-request-20261111"
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var businessId = (await created.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id").GetInt64();
        var staffPath = $"/api/businesses/{businessId}/staff";

        Assert.Equal(HttpStatusCode.NotFound, (await outsider.GetAsync(staffPath)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await Post(owner, staffPath,
                new { email = "team-manager@example.test", roleCode = "Owner" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await Post(owner, staffPath,
                new { email = "unknown@example.test", roleCode = "Manager" })).StatusCode);

        var managerAdded = await Post(owner, staffPath,
            new { email = "team-manager@example.test", roleCode = "Manager" });
        Assert.Equal(HttpStatusCode.OK, managerAdded.StatusCode);

        var cashierAdded = await Post(owner, staffPath,
            new { email = "team-cashier@example.test", roleCode = "Cashier" });
        Assert.Equal(HttpStatusCode.OK, cashierAdded.StatusCode);
        var cashierId = (await cashierAdded.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("userId").GetInt64();

        Assert.Equal(HttpStatusCode.Conflict,
            (await Post(owner, staffPath,
                new { email = "team-manager@example.test", roleCode = "Cashier" })).StatusCode);
        var members = await owner.GetFromJsonAsync<JsonElement>(staffPath);
        Assert.Equal(3, members.GetArrayLength());
        Assert.Equal(HttpStatusCode.NotFound, (await manager.GetAsync(staffPath)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await Post(manager, staffPath,
                new { email = "team-outsider@example.test", roleCode = "Cashier" })).StatusCode);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.BusinessLocations.Add(new BusinessLocation
            {
                BusinessId = businessId,
                Code = "SIDE",
                Name = "Other Location",
                IsPrimary = false
            });
            await db.SaveChangesAsync();
        }

        var ownerDashboard = await owner.GetFromJsonAsync<JsonElement>(
            $"/api/businesses/{businessId}/dashboard");
        Assert.Equal("Owner", ownerDashboard.GetProperty("roleCode").GetString());
        Assert.Equal(2, ownerDashboard.GetProperty("locations").GetArrayLength());

        var managerDashboard = await manager.GetFromJsonAsync<JsonElement>(
            $"/api/businesses/{businessId}/dashboard");
        Assert.Equal("Manager", managerDashboard.GetProperty("roleCode").GetString());
        Assert.Single(managerDashboard.GetProperty("locations").EnumerateArray());

        var cashierDashboard = await cashier.GetFromJsonAsync<JsonElement>(
            $"/api/businesses/{businessId}/dashboard");
        Assert.Equal("Cashier", cashierDashboard.GetProperty("roleCode").GetString());
        Assert.Single(cashierDashboard.GetProperty("locations").EnumerateArray());

        var csrf = await owner.GetFromJsonAsync<JsonElement>("/api/auth/csrf");
        var ownerDetails = await owner.GetFromJsonAsync<JsonElement>("/api/auth/me");
        var ownerId = ownerDetails.GetProperty("id").GetInt64();
        using var revokeOwner = new HttpRequestMessage(HttpMethod.Delete,
            $"/api/businesses/{businessId}/staff/{ownerId}");
        revokeOwner.Headers.Add("X-CSRF-TOKEN", csrf.GetProperty("token").GetString());
        Assert.Equal(HttpStatusCode.NotFound,
            (await owner.SendAsync(revokeOwner)).StatusCode);

        using var revokeCashier = new HttpRequestMessage(HttpMethod.Delete,
            $"/api/businesses/{businessId}/staff/{cashierId}");
        revokeCashier.Headers.Add("X-CSRF-TOKEN", csrf.GetProperty("token").GetString());
        Assert.Equal(HttpStatusCode.NoContent,
            (await owner.SendAsync(revokeCashier)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await cashier.GetAsync($"/api/businesses/{businessId}/dashboard")).StatusCode);
    }

    [Fact]
    public async Task Failed_sign_ins_lock_the_account_after_five_attempts()
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
        var registered = await Post(client, "/api/auth/register", new
        {
            email = "lockout@example.test",
            password = "SecurePassword123!"
        });
        Assert.Equal(HttpStatusCode.OK, registered.StatusCode);

        for (var attempt = 1; attempt <= 4; attempt++)
        {
            var response = await Post(client, "/api/auth/login", new
            {
                email = "lockout@example.test",
                password = "WrongPassword123!"
            });
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        var locked = await Post(client, "/api/auth/login", new
        {
            email = "lockout@example.test",
            password = "WrongPassword123!"
        });
        Assert.Equal(HttpStatusCode.TooManyRequests, locked.StatusCode);

        var correctWhileLocked = await Post(client, "/api/auth/login", new
        {
            email = "lockout@example.test",
            password = "SecurePassword123!"
        });
        Assert.Equal(HttpStatusCode.TooManyRequests, correctWhileLocked.StatusCode);
    }

    [Fact]
    public async Task Platform_directory_requires_permission_and_respects_paging()
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

        using var owner = factory.CreateClient();
        using var admin = factory.CreateClient();
        using var outsider = factory.CreateClient();
        using var anonymous = factory.CreateClient();

        await RegisterAndLogin(owner, "directory-owner@example.test");
        await RegisterAndLogin(admin, "directory-admin@example.test");
        await RegisterAndLogin(outsider, "directory-outsider@example.test");

        var first = await Post(owner, "/api/businesses", new
        {
            name = "Alpha Retail",
            businessTypeId = 1,
            countryCode = "IN",
            currencyCode = "INR",
            timeZoneId = "Asia/Kolkata",
            requestKey = "directory-alpha-20261112"
        });
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        var firstId = (await first.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id").GetInt64();

        var second = await Post(outsider, "/api/businesses", new
        {
            name = "Beta Retail",
            businessTypeId = 1,
            countryCode = "IN",
            currencyCode = "INR",
            timeZoneId = "Asia/Kolkata",
            requestKey = "directory-beta-20261112"
        });
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized,
            (await anonymous.GetAsync("/api/platform/businesses")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await owner.GetAsync("/api/platform/businesses")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await admin.GetAsync("/api/platform/businesses")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await admin.GetAsync($"/api/platform/businesses/{firstId}")).StatusCode);

        var access = await admin.GetFromJsonAsync<JsonElement>("/api/platform/access");
        Assert.False(access.GetProperty("canViewBusinesses").GetBoolean());
        Assert.False(access.GetProperty("canManageLicenses").GetBoolean());

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var user = await db.Users.SingleAsync(x => x.Email == "directory-admin@example.test");
            db.Set<Microsoft.AspNetCore.Identity.IdentityUserClaim<long>>().Add(new()
            {
                UserId = user.Id,
                ClaimType = "platform.permission",
                ClaimValue = "businesses.view"
            });
            await db.SaveChangesAsync();
        }

        access = await admin.GetFromJsonAsync<JsonElement>("/api/platform/access");
        Assert.True(access.GetProperty("canViewBusinesses").GetBoolean());
        Assert.False(access.GetProperty("canManageLicenses").GetBoolean());
        Assert.Equal(HttpStatusCode.Forbidden,
            (await admin.GetAsync("/api/platform/license-access")).StatusCode);

        var page = await admin.GetFromJsonAsync<JsonElement>(
            "/api/platform/businesses?page=1&pageSize=1");
        Assert.Equal(2, page.GetProperty("total").GetInt32());
        Assert.Single(page.GetProperty("items").EnumerateArray());
        Assert.Equal(1, page.GetProperty("page").GetInt32());

        var filtered = await admin.GetFromJsonAsync<JsonElement>(
            "/api/platform/businesses?search=Alpha");
        Assert.Equal(1, filtered.GetProperty("total").GetInt32());
        Assert.Equal(firstId, filtered.GetProperty("items")[0].GetProperty("id").GetInt64());
        Assert.Equal("directory-owner@example.test",
            filtered.GetProperty("items")[0].GetProperty("ownerEmail").GetString());

        var details = await admin.GetFromJsonAsync<JsonElement>(
            $"/api/platform/businesses/{firstId}");
        Assert.Equal("Alpha Retail", details.GetProperty("name").GetString());
        Assert.Equal("Trial", details.GetProperty("subscriptionStatus").GetString());

        Assert.Equal(HttpStatusCode.BadRequest,
            (await admin.GetAsync("/api/platform/businesses?pageSize=100")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await admin.GetAsync("/api/platform/businesses/9999999")).StatusCode);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var user = await db.Users.SingleAsync(x => x.Email == "directory-admin@example.test");
            var claim = await db.Set<Microsoft.AspNetCore.Identity.IdentityUserClaim<long>>()
                .SingleAsync(x => x.UserId == user.Id &&
                    x.ClaimType == "platform.permission" &&
                    x.ClaimValue == "businesses.view");
            db.Remove(claim);
            await db.SaveChangesAsync();
        }

        Assert.Equal(HttpStatusCode.Forbidden,
            (await admin.GetAsync("/api/platform/businesses")).StatusCode);
    }

    [Fact]
    public async Task Platform_license_access_requires_explicit_permission()
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

        using var owner = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await owner.GetAsync("/api/platform/license-access")).StatusCode);

        await RegisterAndLogin(owner, "license-admin@example.test");
        Assert.Equal(HttpStatusCode.Forbidden,
            (await owner.GetAsync("/api/platform/license-access")).StatusCode);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var user = await db.Users.SingleAsync(x => x.Email == "license-admin@example.test");
            db.Set<Microsoft.AspNetCore.Identity.IdentityUserClaim<long>>().Add(new()
            {
                UserId = user.Id,
                ClaimType = "platform.permission",
                ClaimValue = "licenses.manage"
            });
            await db.SaveChangesAsync();
        }

        var granted = await owner.GetFromJsonAsync<JsonElement>("/api/platform/license-access");
        Assert.True(granted.GetProperty("canManageLicenses").GetBoolean());

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var claim = await db.Set<Microsoft.AspNetCore.Identity.IdentityUserClaim<long>>()
                .SingleAsync(x => x.ClaimType == "platform.permission" && x.ClaimValue == "licenses.manage");
            db.Remove(claim);
            await db.SaveChangesAsync();
        }

        Assert.Equal(HttpStatusCode.Forbidden,
            (await owner.GetAsync("/api/platform/license-access")).StatusCode);
    }

    [Fact]
    public async Task Production_registration_stays_disabled_without_email_verification()
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.UseSetting("ConnectionStrings:Default",
                "Server=localhost;Database=BusinessSaasUnused;Trusted_Connection=True;Encrypt=False");
        });

        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        var response = await Post(client, "/api/auth/register", new
        {
            email = "unverified@example.test",
            password = "SecurePassword123!"
        });

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var options = await client.GetFromJsonAsync<JsonElement>("/api/auth/options");
        Assert.False(options.GetProperty("canRegister").GetBoolean());
    }

    [Fact]
    public async Task Writes_require_an_antiforgery_token()
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
        var response = await client.PostAsJsonAsync("/api/auth/register", new
        {
            email = "test@example.test",
            password = "SecurePassword123!"
        });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private static async Task RegisterAndLogin(HttpClient client, string email)
    {
        var register = await Post(client, "/api/auth/register", new
        {
            email,
            password = "SecurePassword123!"
        });
        Assert.Equal(HttpStatusCode.OK, register.StatusCode);

        var login = await Post(client, "/api/auth/login", new
        {
            email,
            password = "SecurePassword123!"
        });
        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
    }

    private static async Task<HttpResponseMessage> Post(HttpClient client, string url, object body)
    {
        var csrfResponse = await client.GetFromJsonAsync<JsonElement>("/api/auth/csrf");
        var token = csrfResponse.GetProperty("token").GetString()!;

        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = JsonContent.Create(body)
        };
        request.Headers.Add("X-CSRF-TOKEN", token);
        return await client.SendAsync(request);
    }
}
