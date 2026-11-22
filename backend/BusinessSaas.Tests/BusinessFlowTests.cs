using System.Buffers.Binary;
using System.Net;
using System.Security.Cryptography;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using BusinessSaas.Api.Data;
using BusinessSaas.Api.Features;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
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

        Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync("/api/ready")).StatusCode);

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
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Equal(1, await db.RetailCategories.CountAsync(x =>
                x.BusinessId == firstId && x.NameKey == "GENERAL"));
            Assert.Equal(1, await db.RetailUnits.CountAsync(x =>
                x.BusinessId == firstId && x.Code == "ITEM"));
        }

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
        Assert.Equal("Trial", ownerDashboardData.GetProperty("accessStatus").GetString());
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
        Assert.Equal("TrialExpired", ownerDashboardData.GetProperty("accessStatus").GetString());

        foreach (var (status, expectedAccess, canOperate) in new[]
        {
            // Status alone never proves payment or grants paid access.
            ("Active", "Inactive", false),
            ("Suspended", "Suspended", false),
            ("PastDue", "Inactive", false),
            ("Grace", "Inactive", false),
            ("Cancelled", "Inactive", false),
            ("Trial", "TrialExpired", false)
        })
        {
            using (var scope = factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var subscription = await db.BusinessSubscriptions
                    .SingleAsync(x => x.BusinessId == otherOwnedId);
                subscription.Status = status;
                await db.SaveChangesAsync();
            }

            var current = await owner.GetFromJsonAsync<JsonElement>(
                $"/api/businesses/{otherOwnedId}/dashboard");
            Assert.Equal(expectedAccess, current.GetProperty("accessStatus").GetString());
            Assert.Equal(canOperate, current.GetProperty("canOperate").GetBoolean());
        }

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
    public async Task Platform_directory_requires_configured_owner_and_respects_paging()
    {
        using var database = new SqliteConnection("Data Source=:memory:");
        await database.OpenAsync();

        const string webhookSecret = "unit-testing-webhook-secret-for-razorpay-2026";
        var gateway = new StubRazorpayGateway();
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Payments:Enabled"] = "true",
                    ["Razorpay:Mode"] = "Test",
                    ["Razorpay:KeyId"] = "rzp_test_unit_fake_123",
                    ["Razorpay:KeySecret"] = "fake-unit-secret-never-live",
                    ["Razorpay:WebhookSecret"] = webhookSecret
                }));
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<DbContextOptions<AppDbContext>>();
                services.RemoveAll<IDbContextOptionsConfiguration<AppDbContext>>();
                services.RemoveAll<IRazorpayGateway>();
                services.AddSingleton<IRazorpayGateway>(gateway);
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
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await anonymous.GetAsync("/api/platform/overview")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await anonymous.GetAsync("/api/platform/customers")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await anonymous.GetAsync("/api/platform/audit")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await anonymous.GetAsync("/api/platform/plans")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await anonymous.GetAsync("/api/platform/billing/drafts")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await owner.GetAsync("/api/platform/billing/drafts")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await owner.GetAsync($"/api/platform/billing/businesses/{firstId}/preview?cycle=Monthly"))
            .StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await owner.GetAsync("/api/platform/plans")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await Post(owner, "/api/platform/businesses/1/extend-trial", new
            {
                additionalDays = 7, expectedTrialEndsAtUtc = DateTime.UtcNow,
                reason = "Not allowed for tenant owner"
            })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await owner.GetAsync("/api/platform/audit")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await owner.GetAsync("/api/platform/overview")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await owner.GetAsync("/api/platform/customers")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await owner.GetAsync("/api/platform/businesses")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await admin.GetAsync("/api/platform/businesses")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await admin.GetAsync($"/api/platform/businesses/{firstId}")).StatusCode);

        var access = await admin.GetFromJsonAsync<JsonElement>("/api/platform/access");
        Assert.False(access.GetProperty("canViewBusinesses").GetBoolean());
        Assert.False(access.GetProperty("canManageLicenses").GetBoolean());

        long platformOwnerId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            platformOwnerId = await db.Users
                .Where(x => x.Email == "directory-admin@example.test")
                .Select(x => x.Id).SingleAsync();

            // A legacy permission claim must not grant another customer platform access.
            var outsiderId = await db.Users
                .Where(x => x.Email == "directory-outsider@example.test")
                .Select(x => x.Id).SingleAsync();
            db.Set<Microsoft.AspNetCore.Identity.IdentityUserClaim<long>>().Add(new()
            {
                UserId = outsiderId,
                ClaimType = "platform.permission",
                ClaimValue = "businesses.view"
            });
            // Even an old tenant membership must not let the platform owner see shop data.
            db.BusinessUsers.Add(new BusinessUser
            {
                BusinessId = firstId,
                UserId = platformOwnerId,
                RoleCode = "Manager"
            });
            await db.SaveChangesAsync();
            scope.ServiceProvider.GetRequiredService<IConfiguration>()["Platform:OwnerUserId"]
                = platformOwnerId.ToString();
        }

        access = await admin.GetFromJsonAsync<JsonElement>("/api/platform/access");
        Assert.True(access.GetProperty("isPlatformOwner").GetBoolean());
        Assert.False(access.GetProperty("mfaVerified").GetBoolean());
        Assert.Equal(HttpStatusCode.Forbidden,
            (await admin.GetAsync("/api/platform/overview")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await admin.GetAsync("/api/platform/businesses")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await admin.GetAsync("/api/platform/audit")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await admin.GetAsync("/api/platform/plans")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await Post(admin, "/api/platform/plans/STARTER/draft",
                new { expectedRevision = 0 })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await Post(admin, "/api/platform/plans/STARTER/publish",
                new { expectedDraftRevision = 1, expectedPublishedVersion = 0,
                      reason = "Not authorized without verified MFA" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await admin.GetAsync("/api/platform/billing/drafts")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await Post(admin, "/api/platform/billing/drafts", new
            {
                businessId = firstId, expectedPlanVersionId = 1,
                billingCycle = "Monthly", requestKey = "mfa-not-verified-0001",
                reason = "MFA is required for drafting"
            })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await Post(admin, $"/api/platform/businesses/{firstId}/subscription-status",
                new { action = "Suspend", expectedStatus = "Trial", reason = "No MFA access to suspend" }))
            .StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await Post(admin, $"/api/platform/businesses/{firstId}/extend-trial", new
            {
                additionalDays = 7, expectedTrialEndsAtUtc = DateTime.UtcNow,
                reason = "MFA required before any change"
            })).StatusCode);

        var ownerMfa = await EnableOwnerMfaAndSignIn(admin, "directory-admin@example.test");
        access = await admin.GetFromJsonAsync<JsonElement>("/api/platform/access");
        Assert.True(access.GetProperty("mfaVerified").GetBoolean());
        Assert.Equal("SuperAdmin", access.GetProperty("roleCode").GetString());
        Assert.True(access.GetProperty("canViewBusinesses").GetBoolean());
        Assert.True(access.GetProperty("canManageLicenses").GetBoolean());
        Assert.Equal(HttpStatusCode.OK,
            (await admin.GetAsync("/api/platform/license-access")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await outsider.GetAsync("/api/platform/businesses")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await admin.GetAsync("/api/businesses")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await admin.GetAsync($"/api/businesses/{firstId}/dashboard")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await Post(owner, $"/api/businesses/{firstId}/staff",
                new { email = "directory-admin@example.test", roleCode = "Cashier" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await Post(admin, "/api/businesses", new
            {
                name = "Forbidden Super Admin Shop",
                businessTypeId = 1, countryCode = "IN", currencyCode = "INR",
                timeZoneId = "Asia/Kolkata", requestKey = "platform-owner-denied-create"
            })).StatusCode);

        Assert.Equal(HttpStatusCode.Forbidden,
            (await outsider.GetAsync("/api/platform/overview")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await outsider.GetAsync("/api/platform/customers")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await outsider.GetAsync("/api/platform/customers/1")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await outsider.GetAsync("/api/platform/audit")).StatusCode);

        // Only the verified MFA login grants platform access and produces a sign-in event.
        var audit = await admin.GetFromJsonAsync<JsonElement>("/api/platform/audit");
        Assert.Equal(2, audit.GetProperty("total").GetInt32());
        Assert.Equal(2, audit.GetProperty("items").GetArrayLength());
        Assert.Equal("PlatformOwner.MfaEnabled", audit.GetProperty("items")[1]
            .GetProperty("action").GetString());
        Assert.Equal(8, ownerMfa.recoveryCodes.Length);
        Assert.True(audit.GetProperty("items")[0].GetProperty("id").GetInt64() > 0);
        Assert.Equal(platformOwnerId, audit.GetProperty("items")[0]
            .GetProperty("actorUserId").GetInt64());
        Assert.Equal("PlatformOwner.SignIn", audit.GetProperty("items")[0]
            .GetProperty("action").GetString());
        Assert.Equal(HttpStatusCode.BadRequest,
            (await admin.GetAsync("/api/platform/audit?pageSize=100")).StatusCode);

        // Seed a real soon-to-expire trial; business creation defaults to a later expiry.
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var firstTrial = await db.BusinessSubscriptions.SingleAsync(x => x.BusinessId == firstId);
            firstTrial.TrialEndsAtUtc = DateTime.UtcNow.AddDays(2);
            await db.SaveChangesAsync();
        }

        Assert.Equal(HttpStatusCode.BadRequest,
            (await admin.GetAsync("/api/platform/overview?months=13")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await outsider.GetAsync("/api/platform/overview?months=12")).StatusCode);
        var annual = await admin.GetFromJsonAsync<JsonElement>("/api/platform/overview?months=12");
        Assert.Equal(12, annual.GetProperty("businessGrowth").GetArrayLength());

        var overview = await admin.GetFromJsonAsync<JsonElement>("/api/platform/overview");
        Assert.Equal(2, overview.GetProperty("totalCustomers").GetInt32());
        Assert.Equal(2, overview.GetProperty("totalBusinesses").GetInt32());
        Assert.Equal(0, overview.GetProperty("activeSubscriptions").GetInt32());
        Assert.Equal(2, overview.GetProperty("activeTrials").GetInt32());
        Assert.Equal(0, overview.GetProperty("expiredTrials").GetInt32());
        var growth = overview.GetProperty("businessGrowth");
        Assert.Equal(6, growth.GetArrayLength());
        Assert.Equal(2, growth.EnumerateArray().Sum(x =>
            x.GetProperty("businessesCreated").GetInt32()));
        Assert.Equal(1, overview.GetProperty("businessesByPlan").GetArrayLength());
        Assert.Equal("STARTER", overview.GetProperty("businessesByPlan")[0]
            .GetProperty("planCode").GetString());
        Assert.Equal(2, overview.GetProperty("businessesByPlan")[0]
            .GetProperty("businessCount").GetInt32());
        Assert.Equal(0, overview.GetProperty("unassignedBusinesses").GetInt32());
        Assert.Equal(1, overview.GetProperty("expiringTrialCount").GetInt32());
        Assert.Single(overview.GetProperty("expiringTrials").EnumerateArray());
        Assert.Equal(firstId, overview.GetProperty("expiringTrials")[0]
            .GetProperty("businessId").GetInt64());
        Assert.Equal("directory-owner@example.test", overview.GetProperty("expiringTrials")[0]
            .GetProperty("ownerEmail").GetString());
        Assert.Equal(2, overview.GetProperty("recentActivity").GetArrayLength());
        Assert.Equal("PlatformOwner.SignIn", overview.GetProperty("recentActivity")[0]
            .GetProperty("action").GetString());

        var customers = await admin.GetFromJsonAsync<JsonElement>(
            "/api/platform/customers?page=1&pageSize=1");
        Assert.Equal(2, customers.GetProperty("total").GetInt32());
        Assert.Single(customers.GetProperty("items").EnumerateArray());

        var filteredCustomers = await admin.GetFromJsonAsync<JsonElement>(
            "/api/platform/customers?search=directory-owner");
        Assert.Equal(1, filteredCustomers.GetProperty("total").GetInt32());
        var customerId = filteredCustomers.GetProperty("items")[0].GetProperty("id").GetInt64();
        Assert.Equal("directory-owner@example.test",
            filteredCustomers.GetProperty("items")[0].GetProperty("email").GetString());
        Assert.Equal(1, filteredCustomers.GetProperty("items")[0]
            .GetProperty("ownedBusinessCount").GetInt32());

        var customerDetail = await admin.GetFromJsonAsync<JsonElement>(
            $"/api/platform/customers/{customerId}");
        Assert.Equal("directory-owner@example.test",
            customerDetail.GetProperty("email").GetString());
        Assert.Single(customerDetail.GetProperty("businesses").EnumerateArray());
        Assert.Equal(firstId, customerDetail.GetProperty("businesses")[0]
            .GetProperty("id").GetInt64());
        Assert.Equal(HttpStatusCode.NotFound,
            (await admin.GetAsync($"/api/platform/customers/{platformOwnerId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await admin.GetAsync("/api/platform/customers/99999999")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await admin.GetAsync("/api/platform/customers?pageSize=100")).StatusCode);

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

        var plans = await admin.GetFromJsonAsync<JsonElement>("/api/platform/plans");
        Assert.Equal(5, plans.GetProperty("items").GetArrayLength());
        Assert.Equal("STARTER", plans.GetProperty("items")[0].GetProperty("code").GetString());
        Assert.Equal(2, plans.GetProperty("items")[0]
            .GetProperty("assignedBusinesses").GetInt32());
        Assert.False(plans.GetProperty("items")[0].GetProperty("pricingConfigured").GetBoolean());
        Assert.False(plans.GetProperty("items")[4].GetProperty("isPublic").GetBoolean());
        Assert.Equal(0, plans.GetProperty("unknownPlanAssignments").GetInt32());

        var originalEnd = overview.GetProperty("expiringTrials")[0]
            .GetProperty("trialEndsAtUtc").GetDateTime();
        Assert.Equal(DateTimeKind.Utc, originalEnd.Kind);
        var request = new
        {
            additionalDays = 7,
            expectedTrialEndsAtUtc = originalEnd,
            reason = "Customer requested extra time for retail setup"
        };
        Assert.Equal(HttpStatusCode.BadRequest,
            (await Post(admin, $"/api/platform/businesses/{firstId}/extend-trial",
                new { additionalDays = 7, expectedTrialEndsAtUtc = originalEnd, reason = "short" }))
            .StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await Post(admin, $"/api/platform/businesses/{firstId}/extend-trial",
                new { additionalDays = 31, expectedTrialEndsAtUtc = originalEnd,
                    reason = "Exceeds approved extension limits" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await Post(admin, "/api/platform/businesses/99999999/extend-trial", request))
            .StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await Post(outsider, $"/api/platform/businesses/{firstId}/extend-trial", request))
            .StatusCode);

        var extension = await Post(admin, $"/api/platform/businesses/{firstId}/extend-trial", request);
        Assert.Equal(HttpStatusCode.OK, extension.StatusCode);
        var extended = await extension.Content.ReadFromJsonAsync<JsonElement>();
        var extendedEnd = extended.GetProperty("trialEndsAtUtc").GetDateTime();
        Assert.Equal(DateTimeKind.Utc, extendedEnd.Kind);
        Assert.True(extendedEnd > originalEnd.AddDays(6));
        Assert.Equal(firstId, extended.GetProperty("businessId").GetInt64());
        Assert.True(extended.GetProperty("auditId").GetInt64() > 0);
        Assert.Equal(HttpStatusCode.Conflict,
            (await Post(admin, $"/api/platform/businesses/{firstId}/extend-trial", request))
            .StatusCode);

        var afterExtension = await owner.GetFromJsonAsync<JsonElement>(
            $"/api/businesses/{firstId}/dashboard");
        Assert.Equal(extendedEnd, afterExtension.GetProperty("subscription")
            .GetProperty("trialEndsAtUtc").GetDateTime());

        var trialAudit = await admin.GetFromJsonAsync<JsonElement>("/api/platform/audit");
        Assert.Equal(3, trialAudit.GetProperty("total").GetInt32());
        Assert.Equal("Business.TrialExtended", trialAudit.GetProperty("items")[0]
            .GetProperty("action").GetString());
        Assert.Equal(firstId, trialAudit.GetProperty("items")[0]
            .GetProperty("targetId").GetInt64());
        Assert.Equal(request.reason, trialAudit.GetProperty("items")[0]
            .GetProperty("reason").GetString());
        Assert.Equal(originalEnd.ToString("O"), trialAudit.GetProperty("items")[0]
            .GetProperty("previousValue").GetString());
        Assert.Equal(extendedEnd.ToString("O"), trialAudit.GetProperty("items")[0]
            .GetProperty("newValue").GetString());

        // Plan configuration is a revisioned draft only, never applied to current entitlements.
        var draftRequest = new
        {
            expectedRevision = 0,
            currencyCode = "INR",
            monthlyPrice = 799.50m,
            quarterlyPrice = 2099m,
            yearlyPrice = 7999m,
            maxLocations = 2,
            maxUsers = 5,
            maxProducts = 1000,
            reason = "Draft commercial terms for internal review"
        };
        Assert.Equal(HttpStatusCode.Forbidden,
            (await Post(outsider, "/api/platform/plans/STARTER/draft", draftRequest)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await Post(admin, "/api/platform/plans/UNKNOWN/draft", draftRequest)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await Post(admin, "/api/platform/plans/STARTER/draft", new
            {
                expectedRevision = 0, currencyCode = "INR", monthlyPrice = 1.234m,
                reason = "Invalid precision must be rejected"
            })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await Post(admin, "/api/platform/plans/STARTER/draft", new
            {
                expectedRevision = 0, monthlyPrice = 199m,
                reason = "Currency missing from draft pricing"
            })).StatusCode);

        var draftSave = await Post(admin, "/api/platform/plans/STARTER/draft", draftRequest);
        Assert.Equal(HttpStatusCode.OK, draftSave.StatusCode);
        var savedDraft = await draftSave.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, savedDraft.GetProperty("revision").GetInt32());
        Assert.True(savedDraft.GetProperty("auditId").GetInt64() > 0);
        Assert.Equal(HttpStatusCode.Conflict,
            (await Post(admin, "/api/platform/plans/STARTER/draft", draftRequest)).StatusCode);

        var configuredPlans = await admin.GetFromJsonAsync<JsonElement>("/api/platform/plans");
        Assert.Equal(1, configuredPlans.GetProperty("items")[0]
            .GetProperty("draft").GetProperty("revision").GetInt32());
        Assert.Equal(799.50m, configuredPlans.GetProperty("items")[0]
            .GetProperty("draft").GetProperty("monthlyPrice").GetDecimal());
        Assert.False(configuredPlans.GetProperty("items")[0]
            .GetProperty("pricingConfigured").GetBoolean());

        var revised = await Post(admin, "/api/platform/plans/STARTER/draft", new
        {
            expectedRevision = 1, currencyCode = "INR", monthlyPrice = 899.50m,
            quarterlyPrice = 2199m, yearlyPrice = 8399m,
            maxLocations = 3, maxUsers = 6, maxProducts = 1500,
            reason = "Revise unpublished plan after review"
        });
        Assert.Equal(HttpStatusCode.OK, revised.StatusCode);
        Assert.Equal(2, (await revised.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("revision").GetInt32());
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.All(await db.BusinessSubscriptions.ToListAsync(),
                subscription => Assert.Equal("STARTER", subscription.PlanCode));
        }

        // Publishing snapshots a reviewed draft, but cannot activate paid subscribers.
        var publish = new
        {
            expectedDraftRevision = 2,
            expectedPublishedVersion = 0,
            reason = "Commercial owner approved the complete plan terms"
        };
        Assert.Equal(HttpStatusCode.Forbidden,
            (await Post(outsider, "/api/platform/plans/STARTER/publish", publish)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await Post(admin, "/api/platform/plans/ENTERPRISE/publish", publish)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,
            (await Post(admin, "/api/platform/plans/STARTER/publish", new
            {
                expectedDraftRevision = 1,
                expectedPublishedVersion = 0,
                reason = "Trying to publish a stale draft revision"
            })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await Post(admin, "/api/platform/plans/STARTER/publish", new
            {
                expectedDraftRevision = 2, expectedPublishedVersion = 0,
                reason = "too short"
            })).StatusCode);

        var firstPublication = await Post(admin, "/api/platform/plans/STARTER/publish", publish);
        Assert.Equal(HttpStatusCode.OK, firstPublication.StatusCode);
        var published = await firstPublication.Content.ReadFromJsonAsync<JsonElement>();
        var publishedId = published.GetProperty("planVersionId").GetInt64();
        Assert.True(publishedId > 0);
        Assert.Equal(1, published.GetProperty("version").GetInt32());
        Assert.Equal(2, published.GetProperty("sourceDraftRevision").GetInt32());
        Assert.Equal(HttpStatusCode.Conflict,
            (await Post(admin, "/api/platform/plans/STARTER/publish", publish)).StatusCode);

        var publishedPlans = await admin.GetFromJsonAsync<JsonElement>("/api/platform/plans");
        Assert.True(publishedPlans.GetProperty("items")[0]
            .GetProperty("pricingConfigured").GetBoolean());
        Assert.Equal(publishedId, publishedPlans.GetProperty("items")[0]
            .GetProperty("published").GetProperty("id").GetInt64());
        Assert.Equal(899.50m, publishedPlans.GetProperty("items")[0]
            .GetProperty("published").GetProperty("monthlyPrice").GetDecimal());

        var thirdDraft = await Post(admin, "/api/platform/plans/STARTER/draft", new
        {
            expectedRevision = 2, currencyCode = "INR", monthlyPrice = 1099m,
            quarterlyPrice = 3199m, yearlyPrice = 11999m,
            maxLocations = 4, maxUsers = 7, maxProducts = 2000,
            reason = "New commercial revision after first catalog publication"
        });
        Assert.Equal(HttpStatusCode.OK, thirdDraft.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,
            (await Post(admin, "/api/platform/plans/STARTER/publish", publish)).StatusCode);
        var secondPublication = await Post(admin, "/api/platform/plans/STARTER/publish", new
        {
            expectedDraftRevision = 3, expectedPublishedVersion = 1,
            reason = "Reviewed the next catalog price and limits"
        });
        Assert.Equal(HttpStatusCode.OK, secondPublication.StatusCode);
        Assert.Equal(2, (await secondPublication.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("version").GetInt32());
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var versions = await db.PublishedPlanVersions
                .Where(x => x.PlanCode == "STARTER").OrderBy(x => x.Version).ToListAsync();
            Assert.Equal(2, versions.Count);
            Assert.Equal(899.50m, versions[0].MonthlyPrice);
            Assert.Equal(1099m, versions[1].MonthlyPrice);
            Assert.All(await db.BusinessSubscriptions.ToListAsync(), item =>
            {
                Assert.Equal("STARTER", item.PlanCode);
                Assert.Equal("Trial", item.Status);
            });
        }

        // Drafts cover explicit periods, including anniversary-based start dates.
        var periodStart = new DateOnly(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1);
        var periodEnd = periodStart.AddMonths(1);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await admin.GetAsync(
                $"/api/platform/billing/businesses/{firstId}/preview?cycle=Monthly"))
            .StatusCode);
        var anniversaryStart = periodStart.AddDays(5);
        var anniversaryPreview = await admin.GetFromJsonAsync<JsonElement>(
            $"/api/platform/billing/businesses/{firstId}/preview?cycle=Monthly&periodStartDate={anniversaryStart:yyyy-MM-dd}");
        Assert.Equal(anniversaryStart.AddMonths(1), DateOnly.Parse(
            anniversaryPreview.GetProperty("periodEndDate").GetString()!));
        Assert.Equal(HttpStatusCode.BadRequest,
            (await admin.GetAsync(
                $"/api/platform/billing/businesses/{firstId}/preview?cycle=Monthly&periodStartDate=1900-01-01"))
            .StatusCode);

        // Internal billing drafts use the immutable published price, never mark a payment.
        var draftListBefore = await admin.GetFromJsonAsync<JsonElement>(
            "/api/platform/billing/drafts");
        Assert.Equal(0, draftListBefore.GetProperty("total").GetInt32());
        Assert.Equal(HttpStatusCode.Forbidden,
            (await outsider.GetAsync(
                $"/api/platform/billing/businesses/{firstId}/preview?cycle=Monthly&periodStartDate={periodStart:yyyy-MM-dd}"))
            .StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await admin.GetAsync($"/api/platform/billing/businesses/{firstId}/preview?cycle=Weekly&periodStartDate={periodStart:yyyy-MM-dd}"))
            .StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,
            (await admin.GetAsync($"/api/platform/billing/businesses/999999/preview?cycle=Monthly&periodStartDate={periodStart:yyyy-MM-dd}"))
            .StatusCode);
        var preview = await admin.GetFromJsonAsync<JsonElement>(
            $"/api/platform/billing/businesses/{firstId}/preview?cycle=Monthly&periodStartDate={periodStart:yyyy-MM-dd}");
        Assert.Equal(periodStart, DateOnly.Parse(preview.GetProperty("periodStartDate").GetString()!));
        Assert.Equal(periodEnd, DateOnly.Parse(preview.GetProperty("periodEndDate").GetString()!));
        Assert.Equal("Alpha Retail", preview.GetProperty("businessName").GetString());
        Assert.Equal(1099m, preview.GetProperty("amount").GetDecimal());
        Assert.Equal("INR", preview.GetProperty("currencyCode").GetString());
        Assert.Equal(2, preview.GetProperty("version").GetInt32());
        var planVersionId = preview.GetProperty("publishedPlanVersionId").GetInt64();

        var billingRequest = new
        {
            businessId = firstId,
            expectedPlanVersionId = planVersionId,
            billingCycle = "Monthly",
            periodStartDate = periodStart,
            requestKey = "billing-first-request-20261118",
            reason = "Internal price record for a trial business"
        };
        Assert.Equal(HttpStatusCode.Forbidden,
            (await Post(outsider, "/api/platform/billing/drafts", billingRequest)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await Post(admin, "/api/platform/billing/drafts", new
            {
                businessId = firstId, expectedPlanVersionId = planVersionId,
                billingCycle = "Monthly", periodStartDate = periodStart,
                requestKey = "bad", reason = "Too short"
            })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,
            (await Post(admin, "/api/platform/billing/drafts", new
            {
                businessId = firstId, expectedPlanVersionId = publishedId,
                billingCycle = "Monthly", periodStartDate = periodStart,
                requestKey = "billing-old-plan-version-20261118",
                reason = "Old plan version must not be used for new drafts"
            })).StatusCode);

        var billingCreated = await Post(admin, "/api/platform/billing/drafts", billingRequest);
        Assert.Equal(HttpStatusCode.Created, billingCreated.StatusCode);
        var billingResult = await billingCreated.Content.ReadFromJsonAsync<JsonElement>();
        var billingId = billingResult.GetProperty("id").GetInt64();
        Assert.True(billingId > 0);
        Assert.False(billingResult.GetProperty("alreadyExists").GetBoolean());
        Assert.Equal(1099m, billingResult.GetProperty("amount").GetDecimal());
        Assert.Equal(periodStart, DateOnly.Parse(billingResult.GetProperty("periodStartDate").GetString()!));
        Assert.Equal(periodEnd, DateOnly.Parse(billingResult.GetProperty("periodEndDate").GetString()!));

        var billingAgain = await Post(admin, "/api/platform/billing/drafts", billingRequest);
        Assert.Equal(HttpStatusCode.OK, billingAgain.StatusCode);
        var existingBilling = await billingAgain.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(billingId, existingBilling.GetProperty("id").GetInt64());
        Assert.True(existingBilling.GetProperty("alreadyExists").GetBoolean());
        Assert.Equal(HttpStatusCode.Conflict,
            (await Post(admin, "/api/platform/billing/drafts", new
            {
                businessId = firstId, expectedPlanVersionId = planVersionId,
                billingCycle = "Yearly", periodStartDate = periodStart,
                requestKey = billingRequest.requestKey,
                reason = billingRequest.reason
            })).StatusCode);

        // Different keys and cycles cannot create overlapping charges.
        Assert.Equal(HttpStatusCode.Conflict,
            (await Post(admin, "/api/platform/billing/drafts", new
            {
                businessId = firstId, expectedPlanVersionId = planVersionId,
                billingCycle = "Quarterly", periodStartDate = periodStart,
                requestKey = "billing-overlap-quarter-20261118",
                reason = "Overlapping billing period should be rejected"
            })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,
            (await Post(admin, "/api/platform/billing/drafts", new
            {
                businessId = firstId, expectedPlanVersionId = planVersionId,
                billingCycle = "Monthly", periodStartDate = periodStart,
                requestKey = "billing-duplicate-month-20261118",
                reason = "Second charge for same period should fail"
            })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,
            (await admin.GetAsync(
                $"/api/platform/billing/businesses/{firstId}/preview?cycle=Monthly&periodStartDate={periodStart:yyyy-MM-dd}"))
            .StatusCode);
        var nextPeriodPreview = await admin.GetFromJsonAsync<JsonElement>(
            $"/api/platform/billing/businesses/{firstId}/preview?cycle=Monthly&periodStartDate={periodEnd:yyyy-MM-dd}");
        Assert.Equal(periodEnd,
            DateOnly.Parse(nextPeriodPreview.GetProperty("periodStartDate").GetString()!));

        var billingHistory = await admin.GetFromJsonAsync<JsonElement>(
            "/api/platform/billing/drafts?page=1&pageSize=10");
        Assert.Equal(1, billingHistory.GetProperty("total").GetInt32());
        Assert.Single(billingHistory.GetProperty("items").EnumerateArray());
        Assert.Equal(1099m, billingHistory.GetProperty("items")[0]
            .GetProperty("amount").GetDecimal());
        Assert.Equal(periodStart, DateOnly.Parse(billingHistory.GetProperty("items")[0]
            .GetProperty("periodStartDate").GetString()!));
        Assert.Equal("Draft", billingHistory.GetProperty("items")[0]
            .GetProperty("status").GetString());
        Assert.Equal(HttpStatusCode.BadRequest,
            (await admin.GetAsync("/api/platform/billing/drafts?page=0")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await admin.GetAsync("/api/platform/billing/drafts/999999")).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await admin.GetAsync($"/api/platform/billing/drafts/{billingId}")).StatusCode);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Equal(1, await db.SubscriptionBillingDrafts.CountAsync());
            Assert.Equal("Trial", (await db.BusinessSubscriptions
                .SingleAsync(x => x.BusinessId == firstId)).Status);
            var billingDraft = await db.SubscriptionBillingDrafts.SingleAsync();
            Assert.Equal(1099m, billingDraft.Amount);
            Assert.Equal(periodStart, billingDraft.PeriodStartDate);
            Assert.Equal(periodEnd, billingDraft.PeriodEndDate);
        }

        // Suspending a subscription blocks operations, replay is rejected and reactivation
        // restores its prior state without extending its original trial.
        var statusRequest = new
        {
            action = "Suspend", expectedStatus = "Trial",
            reason = "Customer requested temporary platform access suspension"
        };
        Assert.Equal(HttpStatusCode.Forbidden,
            (await Post(outsider, $"/api/platform/businesses/{firstId}/subscription-status",
                statusRequest)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await Post(admin, $"/api/platform/businesses/{firstId}/subscription-status", new
            {
                action = "Suspend", expectedStatus = "Trial", reason = "short"
            })).StatusCode);
        var suspended = await Post(admin,
            $"/api/platform/businesses/{firstId}/subscription-status", statusRequest);
        Assert.Equal(HttpStatusCode.OK, suspended.StatusCode);
        Assert.Equal("Suspended",
            (await suspended.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("status").GetString());
        Assert.Equal(HttpStatusCode.Conflict,
            (await Post(admin, $"/api/platform/businesses/{firstId}/subscription-status",
                statusRequest)).StatusCode);

        var blocked = await owner.GetFromJsonAsync<JsonElement>(
            $"/api/businesses/{firstId}/dashboard");
        Assert.False(blocked.GetProperty("canOperate").GetBoolean());
        Assert.Equal("Suspended", blocked.GetProperty("accessStatus").GetString());

        Assert.Equal(HttpStatusCode.Forbidden,
            (await Post(owner, $"/api/businesses/{firstId}/staff", new
            {
                email = "directory-outsider@example.test", roleCode = "Cashier"
            })).StatusCode);
        var staffCsrf = await owner.GetFromJsonAsync<JsonElement>("/api/auth/csrf");
        using var staffDelete = new HttpRequestMessage(HttpMethod.Delete,
            $"/api/businesses/{firstId}/staff/{platformOwnerId}");
        staffDelete.Headers.Add("X-CSRF-TOKEN", staffCsrf.GetProperty("token").GetString());
        Assert.Equal(HttpStatusCode.Forbidden,
            (await owner.SendAsync(staffDelete)).StatusCode);

        var reactivated = await Post(admin,
            $"/api/platform/businesses/{firstId}/subscription-status", new
            {
                action = "Reactivate", expectedStatus = "Suspended",
                reason = "Customer confirmed they are ready to resume their trial"
            });
        Assert.Equal(HttpStatusCode.OK, reactivated.StatusCode);
        Assert.Equal("Trial",
            (await reactivated.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("status").GetString());
        var resumed = await owner.GetFromJsonAsync<JsonElement>(
            $"/api/businesses/{firstId}/dashboard");
        Assert.True(resumed.GetProperty("canOperate").GetBoolean());
        Assert.Equal(extendedEnd,
            resumed.GetProperty("subscription").GetProperty("trialEndsAtUtc").GetDateTime());
        Assert.Equal(HttpStatusCode.Conflict,
            (await Post(admin, $"/api/platform/businesses/{firstId}/subscription-status", new
            {
                action = "Reactivate", expectedStatus = "Suspended",
                reason = "Replay attempt must not reactivate twice"
            })).StatusCode);

        var lastAudit = await admin.GetFromJsonAsync<JsonElement>("/api/platform/audit");
        Assert.Equal("Subscription.Reactivated", lastAudit.GetProperty("items")[0]
            .GetProperty("action").GetString());
        Assert.Equal("Suspended", lastAudit.GetProperty("items")[0]
            .GetProperty("previousValue").GetString());
        Assert.Equal("Trial", lastAudit.GetProperty("items")[0]
            .GetProperty("newValue").GetString());
        Assert.Equal("Subscription.Suspended", lastAudit.GetProperty("items")[1]
            .GetProperty("action").GetString());
        Assert.Equal("Billing.DraftCreated", lastAudit.GetProperty("items")[2]
            .GetProperty("action").GetString());
        Assert.Equal(billingId, lastAudit.GetProperty("items")[2]
            .GetProperty("targetId").GetInt64());
        Assert.Equal("Plan.Published", lastAudit.GetProperty("items")[3]
            .GetProperty("action").GetString());
        Assert.Equal("Plan.DraftSaved", lastAudit.GetProperty("items")[4]
            .GetProperty("action").GetString());

        // Payment preparation snapshots the draft without capturing money.
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await anonymous.GetAsync("/api/platform/billing/payment-requests")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await outsider.GetAsync("/api/platform/billing/payment-requests")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await Post(outsider, $"/api/platform/billing/drafts/{billingId}/payment-requests",
                new { requestKey = "outsider-payment-request-001", reason = "Not authorized" }))
            .StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await Post(admin, $"/api/platform/billing/drafts/{billingId}/payment-requests",
                new { requestKey = "bad", reason = "short" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await Post(admin, "/api/platform/billing/drafts/999999/payment-requests",
                new { requestKey = "payment-notfound-draft-001", reason = "Missing billing draft" }))
            .StatusCode);
        var preparePayment = new
        {
            requestKey = "prepare-billing-payment-20261118",
            reason = "Prepare payment request without collecting money"
        };
        var paymentPrepared = await Post(admin,
            $"/api/platform/billing/drafts/{billingId}/payment-requests", preparePayment);
        Assert.Equal(HttpStatusCode.Created, paymentPrepared.StatusCode);
        var paymentJson = await paymentPrepared.Content.ReadFromJsonAsync<JsonElement>();
        var paymentRequestId = paymentJson.GetProperty("id").GetInt64();
        Assert.True(paymentRequestId > 0);
        Assert.Equal("AwaitingGateway", paymentJson.GetProperty("status").GetString());
        Assert.Equal(1099m, paymentJson.GetProperty("amount").GetDecimal());
        Assert.Equal("INR", paymentJson.GetProperty("currencyCode").GetString());

        var replay = await Post(admin,
            $"/api/platform/billing/drafts/{billingId}/payment-requests", preparePayment);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        var replayResult = await replay.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(replayResult.GetProperty("alreadyExists").GetBoolean());
        Assert.Equal(paymentRequestId, replayResult.GetProperty("id").GetInt64());
        Assert.Equal(HttpStatusCode.Conflict,
            (await Post(admin, $"/api/platform/billing/drafts/{billingId}/payment-requests",
                new
                {
                    requestKey = "another-payment-request-20261118",
                    reason = "Duplicate payment request must be rejected"
                })).StatusCode);
        var paymentHistory = await admin.GetFromJsonAsync<JsonElement>(
            "/api/platform/billing/payment-requests");
        Assert.Equal(1, paymentHistory.GetProperty("total").GetInt32());
        Assert.Equal(paymentRequestId,
            paymentHistory.GetProperty("items")[0].GetProperty("id").GetInt64());
        Assert.Equal(HttpStatusCode.BadRequest,
            (await admin.GetAsync("/api/platform/billing/payment-requests?pageSize=51")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await admin.GetAsync("/api/platform/billing/payment-requests/999999")).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await admin.GetAsync($"/api/platform/billing/payment-requests/{paymentRequestId}"))
            .StatusCode);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Single(await db.SubscriptionPaymentRequests.ToListAsync());
            Assert.Equal("Trial", (await db.BusinessSubscriptions
                .SingleAsync(x => x.BusinessId == firstId)).Status);
            Assert.Equal("Billing.PaymentPrepared", (await db.PlatformAuditEvents
                .OrderByDescending(x => x.Id).FirstAsync()).Action);
        }

        // The tenant Owner checks out a prepared request, never another tenant.
        Assert.Equal(HttpStatusCode.NotFound,
            (await outsider.GetAsync($"/api/businesses/{firstId}/billing/checkout")).StatusCode);
        var checkoutSummary = await owner.GetFromJsonAsync<JsonElement>(
            $"/api/businesses/{firstId}/billing/checkout");
        Assert.True(checkoutSummary.GetProperty("enabled").GetBoolean());
        Assert.True(checkoutSummary.GetProperty("available").GetBoolean());
        Assert.Equal(paymentRequestId,
            checkoutSummary.GetProperty("paymentRequestId").GetInt64());
        Assert.Equal(HttpStatusCode.NotFound,
            (await Post(outsider, $"/api/businesses/{firstId}/billing/checkout",
                new { paymentRequestId })).StatusCode);
        var checkout = await Post(owner, $"/api/businesses/{firstId}/billing/checkout",
            new { paymentRequestId });
        Assert.Equal(HttpStatusCode.OK, checkout.StatusCode);
        var checkoutData = await checkout.Content.ReadFromJsonAsync<JsonElement>();
        var orderId = checkoutData.GetProperty("orderId").GetString()!;
        Assert.Equal(109900L, checkoutData.GetProperty("amount").GetInt64());
        Assert.Equal(1, gateway.OrderCalls);
        var retryOrder = await Post(owner, $"/api/businesses/{firstId}/billing/checkout",
            new { paymentRequestId });
        Assert.Equal(HttpStatusCode.OK, retryOrder.StatusCode);
        Assert.Equal(orderId, (await retryOrder.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("orderId").GetString());
        Assert.Equal(1, gateway.OrderCalls);

        // A timeout may leave the external order created but its local mapping uncertain.
        // Recovery must verify Razorpay receipt/amount before binding the existing order.
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var payment = await db.SubscriptionPaymentRequests.SingleAsync(x => x.Id == paymentRequestId);
            payment.Status = "OrderUncertain";
            payment.ProviderOrderId = null;
            await db.SaveChangesAsync();
        }
        var pending = await admin.GetFromJsonAsync<JsonElement>(
            "/api/platform/billing/reconciliation");
        Assert.Equal(0, pending.GetProperty("total").GetInt32());
        Assert.Single(pending.GetProperty("unresolvedOrders").EnumerateArray());
        Assert.Equal(HttpStatusCode.Forbidden,
            (await outsider.GetAsync("/api/platform/billing/reconciliation")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await Post(outsider,
                $"/api/platform/billing/payment-requests/{paymentRequestId}/reconcile-order",
                new { orderId, reason = "Unauthorised attempted gateway mapping" })).StatusCode);
        gateway.OrderReceiptOverride = "bs_invalid";
        Assert.Equal(HttpStatusCode.Conflict,
            (await Post(admin,
                $"/api/platform/billing/payment-requests/{paymentRequestId}/reconcile-order",
                new { orderId, reason = "The provider receipt must match this request" })).StatusCode);
        gateway.OrderReceiptOverride = null;
        var recovered = await Post(admin,
            $"/api/platform/billing/payment-requests/{paymentRequestId}/reconcile-order",
            new { orderId, reason = "Verified external order after an uncertain response" });
        Assert.Equal(HttpStatusCode.OK, recovered.StatusCode);
        Assert.False((await recovered.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("alreadyReconciled").GetBoolean());
        var recoveredAgain = await Post(admin,
            $"/api/platform/billing/payment-requests/{paymentRequestId}/reconcile-order",
            new { orderId, reason = "Verified external order after an uncertain response" });
        Assert.Equal(HttpStatusCode.OK, recoveredAgain.StatusCode);
        Assert.True((await recoveredAgain.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("alreadyReconciled").GetBoolean());
        Assert.Equal(1, gateway.OrderCalls);

        const string providerPaymentId = "pay_test_capture_0123";
        var webhookBody = JsonSerializer.Serialize(new
        {
            @event = "payment.captured",
            payload = new
            {
                payment = new
                {
                    entity = new
                    {
                        id = providerPaymentId, order_id = orderId,
                        amount = 109900, currency = "INR", status = "captured"
                    }
                }
            }
        });

        async Task<HttpResponseMessage> Webhook(string eventId, string body, bool correctSignature = true)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post,
                "/api/payments/razorpay/webhook")
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
            var bytes = HMACSHA256.HashData(Encoding.UTF8.GetBytes(webhookSecret),
                Encoding.UTF8.GetBytes(body));
            request.Headers.Add("X-Razorpay-Event-Id", eventId);
            request.Headers.Add("X-Razorpay-Signature", correctSignature
                ? Convert.ToHexStringLower(bytes) : new string('0', 64));
            return await anonymous.SendAsync(request);
        }

        Assert.Equal(HttpStatusCode.Unauthorized,
            (await Webhook("evt_invalid_signature_1", webhookBody, false)).StatusCode);
        gateway.FetchAmountOverride = 1L;
        Assert.Equal(HttpStatusCode.Conflict,
            (await Webhook("evt_wrong_amount_001", webhookBody)).StatusCode);
        gateway.FetchAmountOverride = null;

        // A signed authorization notification never activates a subscription.
        var authorizedBody = webhookBody.Replace("payment.captured", "payment.authorized");
        var authorized = await Webhook("evt_authorized_only_001", authorizedBody);
        Assert.Equal(HttpStatusCode.OK, authorized.StatusCode);
        Assert.True((await authorized.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("ignored").GetBoolean());

        // Captured money while suspended must not override the administrator's suspension.
        var suspension = await Post(admin,
            $"/api/platform/businesses/{firstId}/subscription-status", new
            {
                action = "Suspend", expectedStatus = "Trial",
                reason = "Payment is under review while access stays suspended"
            });
        Assert.Equal(HttpStatusCode.OK, suspension.StatusCode);

        var captured = await Webhook("evt_capture_valid_001", webhookBody);
        Assert.Equal(HttpStatusCode.OK, captured.StatusCode);
        Assert.True((await captured.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("activated").GetBoolean());
        var duplicate = await Webhook("evt_capture_valid_001", webhookBody);
        Assert.Equal(HttpStatusCode.OK, duplicate.StatusCode);
        Assert.True((await duplicate.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("alreadyProcessed").GetBoolean());
        Assert.Equal(HttpStatusCode.OK,
            (await Webhook("evt_capture_second_event_001", webhookBody)).StatusCode);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Equal(1, await db.SubscriptionPayments.CountAsync());
            Assert.Equal(2, await db.RazorpayWebhookEvents.CountAsync());
            var verified = await db.SubscriptionPayments.SingleAsync();
            Assert.Equal(providerPaymentId, verified.ProviderPaymentId);
            Assert.Equal("HeldSuspended", verified.ActivationStatus);
            var sub = await db.BusinessSubscriptions.SingleAsync(x => x.BusinessId == firstId);
            Assert.Equal("Suspended", sub.Status);
            Assert.Equal("Active", sub.StatusBeforeSuspension);
            Assert.Equal(planVersionId, sub.PaidPlanVersionId);
            Assert.True(sub.PaidStartsAtUtc < DateTime.UtcNow);
            Assert.True(sub.PaidEndsAtUtc > DateTime.UtcNow);
        }
        var heldDashboard = await owner.GetFromJsonAsync<JsonElement>(
            $"/api/businesses/{firstId}/dashboard");
        Assert.Equal("Suspended", heldDashboard.GetProperty("accessStatus").GetString());
        Assert.False(heldDashboard.GetProperty("canOperate").GetBoolean());
        var resumePaid = await Post(admin,
            $"/api/platform/businesses/{firstId}/subscription-status", new
            {
                action = "Reactivate", expectedStatus = "Suspended",
                reason = "Confirmed payment and approved access restoration"
            });
        Assert.Equal(HttpStatusCode.OK, resumePaid.StatusCode);

        var paidDashboard = await owner.GetFromJsonAsync<JsonElement>(
            $"/api/businesses/{firstId}/dashboard");
        Assert.Equal("Active", paidDashboard.GetProperty("accessStatus").GetString());
        Assert.True(paidDashboard.GetProperty("canOperate").GetBoolean());

        // Refunds are observed, verified and deduplicated. This API never issues refunds.
        const string providerRefundId = "rfnd_test_partial_001";
        var refundBody = JsonSerializer.Serialize(new
        {
            @event = "refund.processed",
            payload = new
            {
                refund = new
                {
                    entity = new
                    {
                        id = providerRefundId, payment_id = providerPaymentId,
                        amount = 50000, currency = "INR", status = "processed"
                    }
                }
            }
        });
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await Webhook("evt_refund_unsigned_001", refundBody, false)).StatusCode);
        gateway.RefundAmountOverride = 20000;
        Assert.Equal(HttpStatusCode.Conflict,
            (await Webhook("evt_refund_wrong_amount_001", refundBody)).StatusCode);
        gateway.RefundAmountOverride = null;
        var refund = await Webhook("evt_refund_processed_001", refundBody);
        Assert.Equal(HttpStatusCode.OK, refund.StatusCode);
        Assert.True((await refund.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("refundRecorded").GetBoolean());
        var refundReplay = await Webhook("evt_refund_processed_001", refundBody);
        Assert.Equal(HttpStatusCode.OK, refundReplay.StatusCode);
        Assert.True((await refundReplay.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("alreadyProcessed").GetBoolean());
        var refundOtherEvent = await Webhook("evt_refund_duplicate_002", refundBody);
        Assert.Equal(HttpStatusCode.OK, refundOtherEvent.StatusCode);
        Assert.True((await refundOtherEvent.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("alreadyRecorded").GetBoolean());

        // A second provider-confirmed refund cannot exceed the remaining payment.
        var excessRefundBody = refundBody.Replace("rfnd_test_partial_001", "rfnd_test_excess_002")
            .Replace("50000", "70000");
        gateway.RefundAmountOverride = 70000;
        Assert.Equal(HttpStatusCode.Conflict,
            (await Webhook("evt_refund_excess_003", excessRefundBody)).StatusCode);
        gateway.RefundAmountOverride = null;
        var failedRefundBody = refundBody.Replace("refund.processed", "refund.failed");
        Assert.Equal(HttpStatusCode.OK,
            (await Webhook("evt_refund_failed_004", failedRefundBody)).StatusCode);
        var stillActive = await owner.GetFromJsonAsync<JsonElement>(
            $"/api/businesses/{firstId}/dashboard");
        Assert.Equal("Active", stillActive.GetProperty("accessStatus").GetString());
        // Refunds are flagged for manual business-policy review, not silently revoked.

        var reconciliation = await admin.GetFromJsonAsync<JsonElement>(
            "/api/platform/billing/reconciliation");
        Assert.Equal(1, reconciliation.GetProperty("total").GetInt32());
        var row = reconciliation.GetProperty("items")[0];
        Assert.Equal(1099m, row.GetProperty("amount").GetDecimal());
        Assert.Equal(500m, row.GetProperty("refundedAmount").GetDecimal());
        Assert.Equal(599m, row.GetProperty("netAmount").GetDecimal());
        Assert.Equal("RefundReview", row.GetProperty("activationStatus").GetString());
        Assert.Equal(HttpStatusCode.BadRequest,
            (await admin.GetAsync("/api/platform/billing/reconciliation?pageSize=51")).StatusCode);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Equal(1, await db.SubscriptionRefunds.CountAsync());
            Assert.Equal(4, await db.RazorpayWebhookEvents.CountAsync());
            Assert.True((await db.SubscriptionRefunds.SingleAsync()).Id > 0);
        }

        // GST document drafts must be based on a verified captured payment/refund.
        var taxProfile = new
        {
            expectedRevision = 0,
            issuerName = "Test Platform Software",
            issuerAddress = "Registered test address, Lucknow, Uttar Pradesh, India",
            issuerGstin = "09ABCDE1234F1Z5",
            issuerStateCode = "09",
            recipientName = "Test Retail Business",
            recipientAddress = "Buyer test address, Kanpur, Uttar Pradesh, India",
            recipientGstin = (string?)null,
            recipientStateCode = "09",
            serviceCode = "998314",
            serviceDescription = "Software subscription service",
            invoiceSeries = "BSA",
            gstRate = 18m,
            pricesIncludeTax = true,
            reason = "Reviewing the billing tax profile for integration tests"
        };
        Assert.Equal(HttpStatusCode.Forbidden,
            (await Post(outsider,
                "/api/platform/billing/tax-profiles/" + firstId, taxProfile)).StatusCode);
        var savedTax = await Post(admin,
            "/api/platform/billing/tax-profiles/" + firstId, taxProfile);
        Assert.Equal(HttpStatusCode.OK, savedTax.StatusCode);
        Assert.Equal(1, (await savedTax.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("revision").GetInt32());
        Assert.Equal(HttpStatusCode.Conflict,
            (await Post(admin, "/api/platform/billing/tax-profiles/" + firstId,
                taxProfile)).StatusCode);

        var createInvoice = new
        {
            id = (long)0,
            reason = "Verified captured payment invoice draft for review"
        };
        Assert.Equal(HttpStatusCode.BadRequest,
            (await Post(admin, "/api/platform/billing/invoice-drafts", createInvoice)).StatusCode);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var capturedPayment = await db.SubscriptionPayments.SingleAsync();
            createInvoice = new
            {
                id = capturedPayment.Id,
                reason = "Verified captured payment invoice draft for review"
            };
        }
        var createdInvoice = await Post(admin, "/api/platform/billing/invoice-drafts", createInvoice);
        Assert.Equal(HttpStatusCode.Created, createdInvoice.StatusCode);
        var invoiceId = (await createdInvoice.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id").GetInt64();
        var invoice = await admin.GetFromJsonAsync<JsonElement>(
            "/api/platform/billing/document-drafts/" + invoiceId);
        Assert.Equal("Invoice", invoice.GetProperty("documentType").GetString());
        Assert.Equal("Draft", invoice.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, invoice.GetProperty("documentNumber").ValueKind);
        Assert.Equal(1099m, invoice.GetProperty("grossAmount").GetDecimal());
        Assert.Equal(1099m, invoice.GetProperty("taxableAmount").GetDecimal() +
            invoice.GetProperty("cgstAmount").GetDecimal() +
            invoice.GetProperty("sgstAmount").GetDecimal() +
            invoice.GetProperty("igstAmount").GetDecimal());
        var invoiceAgain = await Post(admin, "/api/platform/billing/invoice-drafts", createInvoice);
        Assert.Equal(HttpStatusCode.OK, invoiceAgain.StatusCode);
        Assert.Equal(invoiceId, (await invoiceAgain.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id").GetInt64());

        long verifiedRefundId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            verifiedRefundId = (await db.SubscriptionRefunds.SingleAsync()).Id;
        }
        var creditInput = new
        {
            id = verifiedRefundId,
            reason = "Processed partial refund pending credit note review"
        };
        var createdCredit = await Post(admin, "/api/platform/billing/credit-note-drafts", creditInput);
        Assert.Equal(HttpStatusCode.Created, createdCredit.StatusCode);
        var creditId = (await createdCredit.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id").GetInt64();
        var credit = await admin.GetFromJsonAsync<JsonElement>(
            "/api/platform/billing/document-drafts/" + creditId);
        Assert.Equal("CreditNote", credit.GetProperty("documentType").GetString());
        Assert.Equal("Draft", credit.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, credit.GetProperty("documentNumber").ValueKind);
        Assert.Equal(500m, credit.GetProperty("grossAmount").GetDecimal());
        Assert.Equal(invoiceId, credit.GetProperty("originalDocumentId").GetInt64());
        var creditAgain = await Post(admin, "/api/platform/billing/credit-note-drafts", creditInput);
        Assert.Equal(HttpStatusCode.OK, creditAgain.StatusCode);
        Assert.Equal(creditId, (await creditAgain.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id").GetInt64());
        Assert.Equal(HttpStatusCode.Forbidden,
            (await outsider.GetAsync("/api/platform/billing/document-drafts/" + invoiceId))
            .StatusCode);

        // A cached Active status must not outlive its paid period.
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var sub = await db.BusinessSubscriptions.SingleAsync(x => x.BusinessId == firstId);
            sub.PaidEndsAtUtc = DateTime.UtcNow.AddSeconds(-1);
            await db.SaveChangesAsync();
        }
        var expiredPaid = await owner.GetFromJsonAsync<JsonElement>(
            $"/api/businesses/{firstId}/dashboard");
        Assert.Equal("Inactive", expiredPaid.GetProperty("accessStatus").GetString());
        Assert.False(expiredPaid.GetProperty("canOperate").GetBoolean());
        Assert.Equal(HttpStatusCode.Forbidden,
            (await Post(owner, $"/api/businesses/{firstId}/staff",
                new { email = "directory-outsider@example.test", roleCode = "Cashier" }))
            .StatusCode);

        // An active paid subscription must never accept a trial extension.
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var secondTrial = await db.BusinessSubscriptions
                .SingleAsync(x => x.BusinessId != firstId);
            secondTrial.Status = "Active";
            await db.SaveChangesAsync();
        }
        long activeBusinessId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            activeBusinessId = await db.BusinessSubscriptions
                .Where(x => x.BusinessId != firstId).Select(x => x.BusinessId).SingleAsync();
        }
        Assert.Equal(HttpStatusCode.Conflict,
            (await Post(admin, $"/api/platform/businesses/{activeBusinessId}/extend-trial",
                request)).StatusCode);

        // A recovery code signs in once, but cannot be reused.
        Assert.Equal(HttpStatusCode.NoContent,
            (await Post(admin, "/api/auth/logout", new { })).StatusCode);
        var loginWithRecovery = await Post(admin, "/api/auth/login", new
        {
            email = "directory-admin@example.test", password = "SecurePassword123!"
        });
        Assert.True((await loginWithRecovery.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("requiresTwoFactor").GetBoolean());
        Assert.Equal(HttpStatusCode.NoContent,
            (await Post(admin, "/api/auth/mfa/verify", new
            {
                recoveryCode = ownerMfa.recoveryCodes[0]
            })).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await admin.GetAsync("/api/platform/overview")).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent,
            (await Post(admin, "/api/auth/logout", new { })).StatusCode);
        await Post(admin, "/api/auth/login", new
        {
            email = "directory-admin@example.test", password = "SecurePassword123!"
        });
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await Post(admin, "/api/auth/mfa/verify", new
            {
                recoveryCode = ownerMfa.recoveryCodes[0]
            })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await admin.GetAsync("/api/platform/businesses")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent,
            (await Post(admin, "/api/auth/mfa/verify", new
            {
                code = AuthenticatorCode(ownerMfa.secret)
            })).StatusCode);

        // Removing the configured owner revokes platform access without a new login.
        factory.Services.GetRequiredService<IConfiguration>()["Platform:OwnerUserId"] = "0";
        Assert.Equal(HttpStatusCode.Forbidden,
            (await admin.GetAsync("/api/platform/businesses")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await admin.GetAsync("/api/platform/customers")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await admin.GetAsync("/api/platform/overview")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await admin.GetAsync("/api/platform/audit")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await admin.GetAsync("/api/platform/plans")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await admin.GetAsync("/api/platform/billing/payment-requests")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await Post(admin, $"/api/platform/businesses/{firstId}/extend-trial", request))
            .StatusCode);
        Assert.False((await admin.GetFromJsonAsync<JsonElement>("/api/platform/access"))
            .GetProperty("isPlatformOwner").GetBoolean());
    }

    [Fact]
    public async Task Platform_license_access_requires_configured_owner()
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
            var userId = await db.Users.Where(x => x.Email == "license-admin@example.test")
                .Select(x => x.Id).SingleAsync();
            scope.ServiceProvider.GetRequiredService<IConfiguration>()["Platform:OwnerUserId"]
                = userId.ToString();
        }

        await EnableOwnerMfaAndSignIn(owner, "license-admin@example.test");
        var granted = await owner.GetFromJsonAsync<JsonElement>("/api/platform/license-access");
        Assert.True(granted.GetProperty("canManageLicenses").GetBoolean());

        factory.Services.GetRequiredService<IConfiguration>()["Platform:OwnerUserId"] = "0";
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
        Assert.Equal(HttpStatusCode.ServiceUnavailable,
            (await client.GetAsync("/api/ready")).StatusCode);
    }

    [Fact]
    public async Task Authentication_endpoints_are_rate_limited_per_remote_address()
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
        for (var attempt = 0; attempt < 30; attempt++)
        {
            var response = await Post(client, "/api/auth/login",
                new { email = "unknown@example.test", password = "invalid" });
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        using (var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login"))
        {
            request.Headers.TryAddWithoutValidation("X-Forwarded-For", "203.0.113.99");
            var csrf = await client.GetFromJsonAsync<JsonElement>("/api/auth/csrf");
            request.Headers.Add("X-CSRF-TOKEN", csrf.GetProperty("token").GetString());
            request.Content = JsonContent.Create(new
            {
                email = "another-unknown@example.test", password = "invalid"
            });
            var blocked = await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.TooManyRequests, blocked.StatusCode);
        }

        for (var attempt = 0; attempt < 10; attempt++)
        {
            var response = await Post(client, "/api/auth/register",
                new { email = "not-an-email", password = "invalid" });
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
        Assert.Equal(HttpStatusCode.TooManyRequests,
            (await Post(client, "/api/auth/register",
                new { email = "not-an-email", password = "invalid" })).StatusCode);

        var health = await client.GetAsync("/api/health");
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
        Assert.Equal("no-store", health.Headers.CacheControl?.ToString());
        Assert.Equal("nosniff", health.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("no-referrer", health.Headers.GetValues("Referrer-Policy").Single());
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

    [Fact]
    public async Task Platform_provisioning_requires_mfa_verified_owner_and_is_idempotent()
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
        using var admin = factory.CreateClient();
        using var owner = factory.CreateClient();
        using var outsider = factory.CreateClient();

        await RegisterAndLogin(admin, "platform-creation-admin@example.test");
        await RegisterAndLogin(owner, "platform-creation-owner@example.test");
        await RegisterAndLogin(outsider, "platform-creation-outsider@example.test");

        long ownerId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var platformId = await db.Users.Where(x => x.Email == "platform-creation-admin@example.test")
                .Select(x => x.Id).SingleAsync();
            var account = await db.Users.SingleAsync(x => x.Email == "platform-creation-owner@example.test");
            account.EmailConfirmed = true; // Fixture: verified by an external email-verification flow.
            ownerId = account.Id;
            await db.SaveChangesAsync();
            scope.ServiceProvider.GetRequiredService<IConfiguration>()["Platform:OwnerUserId"] =
                platformId.ToString();
        }

        var body = new
        {
            name = "Assigned Retail Shop",
            businessTypeId = 1,
            countryCode = "IN",
            currencyCode = "INR",
            timeZoneId = "Asia/Kolkata",
            ownerEmail = "platform-creation-owner@example.test",
            locationName = "Kanpur Main",
            planCode = "STARTER",
            requestKey = "platform-provision-verified-0001"
        };

        Assert.Equal(HttpStatusCode.Forbidden,
            (await Post(outsider, "/api/platform/businesses", body)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await Post(owner, "/api/platform/businesses", body)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await Post(admin, "/api/platform/businesses", body)).StatusCode);

        await EnableOwnerMfaAndSignIn(admin, "platform-creation-admin@example.test");
        var verified = await admin.GetFromJsonAsync<JsonElement>(
            "/api/platform/owner-lookup?email=platform-creation-owner%40example.test");
        Assert.Equal(ownerId, verified.GetProperty("id").GetInt64());
        Assert.True(verified.GetProperty("verified").GetBoolean());

        var first = await Post(admin, "/api/platform/businesses", body);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        var businessId = (await first.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id").GetInt64();
        Assert.True(businessId > 0);

        var retry = await Post(admin, "/api/platform/businesses", body);
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        var repeatId = (await retry.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id").GetInt64();
        Assert.Equal(businessId, repeatId);

        var changed = await Post(admin, "/api/platform/businesses", new
        {
            name = "Changed Shop Name",
            body.businessTypeId, body.countryCode, body.currencyCode,
            body.timeZoneId, body.ownerEmail, body.locationName, body.planCode, body.requestKey
        });
        Assert.Equal(HttpStatusCode.Conflict, changed.StatusCode);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Equal(1, await db.Businesses.CountAsync(x => x.Id == businessId));
            Assert.Equal(ownerId, await db.Businesses.Where(x => x.Id == businessId)
                .Select(x => x.CreatedByUserId).SingleAsync());
            Assert.Equal(1, await db.BusinessUsers.CountAsync(x => x.BusinessId == businessId &&
                x.UserId == ownerId && x.RoleCode == "Owner"));
            Assert.Equal(1, await db.BusinessLocations.CountAsync(x => x.BusinessId == businessId &&
                x.Name == "Kanpur Main"));
            Assert.Equal(1, await db.PlatformAuditEvents.CountAsync(x =>
                x.TargetId == businessId && x.Action == "Business.Provisioned"));
        }

        Assert.Equal(HttpStatusCode.OK,
            (await owner.GetAsync($"/api/businesses/{businessId}/dashboard")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await outsider.GetAsync($"/api/businesses/{businessId}/dashboard")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await admin.GetAsync($"/api/businesses/{businessId}/dashboard")).StatusCode);
    }

    private sealed class StubRazorpayGateway : IRazorpayGateway
    {
        public int OrderCalls { get; private set; }
        public long? FetchAmountOverride { get; set; }
        public long? RefundAmountOverride { get; set; }
        public string? OrderReceiptOverride { get; set; }
        private string orderId = "";
        private long amount;

        public Task<GatewayOrder> CreateOrder(long requestId, long orderAmount,
            CancellationToken cancellationToken)
        {
            OrderCalls++;
            orderId = $"order_test_request_{requestId}";
            amount = orderAmount;
            return Task.FromResult(new GatewayOrder(orderId, amount, "INR", "created"));
        }

        public Task<GatewayPayment> FetchPayment(string paymentId,
            CancellationToken cancellationToken) =>
            Task.FromResult(new GatewayPayment(paymentId, orderId,
                FetchAmountOverride ?? amount, "INR", "captured"));

        public Task<GatewayOrder> FetchOrder(string id, CancellationToken cancellationToken) =>
            Task.FromResult(new GatewayOrder(id, amount, "INR", "created",
                OrderReceiptOverride ?? $"bs_{id.Replace("order_test_request_", "")}"));

        public Task<GatewayRefund> FetchRefund(string id, CancellationToken cancellationToken) =>
            Task.FromResult(new GatewayRefund(id, "pay_test_capture_0123",
                RefundAmountOverride ?? 50000, "INR", "processed"));
    }

    private static async Task<(string secret, string[] recoveryCodes)> EnableOwnerMfaAndSignIn(
        HttpClient client, string email)
    {
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await Post(client, "/api/auth/mfa/setup", new { password = "wrong" })).StatusCode);

        var start = await Post(client, "/api/auth/mfa/setup", new
        {
            password = "SecurePassword123!"
        });
        Assert.Equal(HttpStatusCode.OK, start.StatusCode);
        var secret = (await start.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("secret").GetString()!;

        Assert.Equal(HttpStatusCode.BadRequest,
            (await Post(client, "/api/auth/mfa/enable", new
            {
                password = "SecurePassword123!", code = "0000000"
            })).StatusCode);

        var enabled = await Post(client, "/api/auth/mfa/enable", new
        {
            password = "SecurePassword123!", code = AuthenticatorCode(secret)
        });
        Assert.Equal(HttpStatusCode.OK, enabled.StatusCode);
        var codes = (await enabled.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("recoveryCodes").EnumerateArray()
            .Select(x => x.GetString()!).ToArray();
        Assert.Equal(8, codes.Length);

        var passwordLogin = await Post(client, "/api/auth/login", new
        {
            email, password = "SecurePassword123!"
        });
        Assert.Equal(HttpStatusCode.OK, passwordLogin.StatusCode);
        Assert.True((await passwordLogin.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("requiresTwoFactor").GetBoolean());
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.GetAsync("/api/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.GetAsync("/api/platform/businesses")).StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized,
            (await Post(client, "/api/auth/mfa/verify", new { code = "0000000" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent,
            (await Post(client, "/api/auth/mfa/verify", new
            {
                code = AuthenticatorCode(secret)
            })).StatusCode);
        var me = await client.GetFromJsonAsync<JsonElement>("/api/auth/me");
        Assert.True(me.GetProperty("mfaVerified").GetBoolean());
        return (secret, codes);
    }

    // Compute a real RFC 6238 code so the integration test verifies the Identity token provider.
    private static string AuthenticatorCode(string secret)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var bytes = new List<byte>();
        var buffer = 0;
        var bits = 0;
        foreach (var character in secret.ToUpperInvariant())
        {
            var value = alphabet.IndexOf(character);
            if (value < 0) continue;
            buffer = (buffer << 5) | value;
            bits += 5;
            if (bits < 8) continue;
            bits -= 8;
            bytes.Add((byte)(buffer >> bits));
            buffer &= (1 << bits) - 1;
        }

        var counter = new byte[8];
        BinaryPrimitives.WriteInt64BigEndian(counter, DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30);
        var digest = HMACSHA1.HashData(bytes.ToArray(), counter);
        var offset = digest[^1] & 0x0f;
        var otpValue = ((digest[offset] & 0x7f) << 24) |
                    (digest[offset + 1] << 16) |
                    (digest[offset + 2] << 8) |
                    digest[offset + 3];
        return (otpValue % 1000000).ToString("D6");
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
