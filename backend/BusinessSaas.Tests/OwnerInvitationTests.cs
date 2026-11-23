using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BusinessSaas.Api.Data;
using BusinessSaas.Api.Features;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace BusinessSaas.Tests;

public sealed class OwnerInvitationTests
{
    [Fact]
    public async Task Invited_owner_verifies_email_only_once_before_super_admin_assigns_business()
    {
        using var database = new SqliteConnection("Data Source=:memory:");
        await database.OpenAsync();
        var mailer = new FakeMailer();
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<DbContextOptions<AppDbContext>>();
                services.RemoveAll<IDbContextOptionsConfiguration<AppDbContext>>();
                services.AddDbContext<AppDbContext>(options => options.UseSqlite(database));
                services.RemoveAll<IOwnerInvitationMailer>();
                services.AddSingleton<IOwnerInvitationMailer>(mailer);
            });
        });

        using var admin = factory.CreateClient();
        using var owner = factory.CreateClient();
        using var outsider = factory.CreateClient();
        await BusinessFlowTests.SignInProvisioningAdmin(factory.Services, admin,
            "invite-platform-admin@example.test");

        var config = factory.Services.GetRequiredService<Microsoft.Extensions.Configuration.IConfiguration>();
        config["OwnerInvitations:PublicBaseUrl"] = "https://saas.example.test/";

        const string email = "new-retail-owner@example.test";
        var inviteRequest = new { email, reason = "Approved new retail owner onboarding" };
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await Send(outsider, "/api/platform/owner-invitations", inviteRequest)).StatusCode);

        mailer.IsEnabled = false;
        Assert.Equal(HttpStatusCode.ServiceUnavailable,
            (await Send(admin, "/api/platform/owner-invitations", inviteRequest)).StatusCode);
        using (var scope = factory.Services.CreateScope())
            Assert.Empty(await scope.ServiceProvider.GetRequiredService<AppDbContext>()
                .PlatformOwnerInvitations.ToListAsync());

        mailer.IsEnabled = true;
        var response = await Send(admin, "/api/platform/owner-invitations", inviteRequest);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var issued = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Sent", issued.GetProperty("status").GetString());
        Assert.Equal(email, mailer.SentEmail);
        Assert.NotNull(mailer.Link);
        Assert.StartsWith("https://saas.example.test/?invite=", mailer.Link);
        var token = Uri.UnescapeDataString(new Uri(mailer.Link!).Query["?invite=".Length..]);
        Assert.Equal(43, token.Length);
        Assert.DoesNotContain(token, await response.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.TooManyRequests,
            (await Send(admin, "/api/platform/owner-invitations", inviteRequest)).StatusCode);

        Assert.Equal(HttpStatusCode.NotFound,
            (await Send(outsider, "/api/auth/owner-invitations/inspect",
                new { token = "not-a-valid-token" })).StatusCode);
        var preview = await Send(outsider, "/api/auth/owner-invitations/inspect", new { token });
        Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
        Assert.Equal(email, (await preview.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("email").GetString());
        Assert.Equal(HttpStatusCode.BadRequest,
            (await Send(owner, "/api/auth/owner-invitations/accept",
                new { token, password = "short" })).StatusCode);

        var accepted = await Send(owner, "/api/auth/owner-invitations/accept",
            new { token, password = "VerifiedOwnerPassword123!" });
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        Assert.True((await accepted.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("verified").GetBoolean());
        Assert.Equal(HttpStatusCode.Conflict,
            (await Send(outsider, "/api/auth/owner-invitations/accept",
                new { token, password = "AnotherPassword123!" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await Send(outsider, "/api/auth/owner-invitations/inspect", new { token })).StatusCode);

        long ownerId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var users = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<AppUser>>();
            var created = await users.FindByEmailAsync(email);
            Assert.NotNull(created);
            ownerId = created.Id;
            Assert.True(created.EmailConfirmed);
            Assert.True(await users.CheckPasswordAsync(created, "VerifiedOwnerPassword123!"));
            Assert.False(await db.BusinessUsers.AnyAsync(x => x.UserId == ownerId));
            Assert.Single(await db.PlatformOwnerInvitations.Where(x =>
                x.AcceptedUserId == ownerId && x.AcceptedAtUtc != null).ToListAsync());
            Assert.Equal(1, await db.PlatformAuditEvents.CountAsync(x =>
                x.Action == "OwnerInvitation.Issued"));
            Assert.Equal(1, await db.PlatformAuditEvents.CountAsync(x =>
                x.Action == "OwnerInvitation.Accepted"));
        }

        var lookup = await admin.GetFromJsonAsync<JsonElement>(
            "/api/platform/owner-lookup?email=" + Uri.EscapeDataString(email));
        Assert.Equal(ownerId, lookup.GetProperty("id").GetInt64());
        var businessRequest = new {
            name = "Invited Shop", businessTypeId = 1,
            countryCode = "IN", currencyCode = "INR", timeZoneId = "Asia/Kolkata",
            ownerEmail = email, locationName = "Main Shop", planCode = "STARTER",
            requestKey = "invite-retail-provision-20261123"
        };
        var business = await Send(admin, "/api/platform/businesses", businessRequest);
        Assert.Equal(HttpStatusCode.Created, business.StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await Send(admin, "/api/platform/businesses", businessRequest)).StatusCode);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Single(await db.BusinessUsers.Where(x =>
                x.UserId == ownerId && x.RoleCode == "Owner").ToListAsync());
            Assert.Single(await db.Businesses.Where(x => x.CreatedByUserId == ownerId).ToListAsync());
        }
    }

    [Fact]
    public async Task Expired_invite_cannot_be_used_and_delivery_failures_leave_no_active_token()
    {
        using var database = new SqliteConnection("Data Source=:memory:");
        await database.OpenAsync();
        var mailer = new FakeMailer();
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<DbContextOptions<AppDbContext>>();
                services.RemoveAll<IDbContextOptionsConfiguration<AppDbContext>>();
                services.AddDbContext<AppDbContext>(options => options.UseSqlite(database));
                services.RemoveAll<IOwnerInvitationMailer>();
                services.AddSingleton<IOwnerInvitationMailer>(mailer);
            });
        });
        using var admin = factory.CreateClient();
        using var outsider = factory.CreateClient();
        await BusinessFlowTests.SignInProvisioningAdmin(factory.Services, admin,
            "expiring-invite-platform@example.test");
        factory.Services.GetRequiredService<Microsoft.Extensions.Configuration.IConfiguration>()
            ["OwnerInvitations:PublicBaseUrl"] = "https://saas.example.test/";
        mailer.Fail = true;
        Assert.Equal(HttpStatusCode.ServiceUnavailable,
            (await Send(admin, "/api/platform/owner-invitations", new {
                email = "failed-invite@example.test",
                reason = "Delivery failure should revoke token"
            })).StatusCode);
        using (var scope = factory.Services.CreateScope())
        {
            var row = await scope.ServiceProvider.GetRequiredService<AppDbContext>()
                .PlatformOwnerInvitations.SingleAsync();
            Assert.NotNull(row.RevokedAtUtc);
        }

        mailer.Fail = false;
        var sent = await Send(admin, "/api/platform/owner-invitations", new {
            email = "expired-invite@example.test",
            reason = "Verify expired invitation rejection"
        });
        Assert.Equal(HttpStatusCode.OK, sent.StatusCode);
        var token = Uri.UnescapeDataString(new Uri(mailer.Link!).Query["?invite=".Length..]);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var row = await db.PlatformOwnerInvitations.SingleAsync(x =>
                x.Email == "expired-invite@example.test");
            row.ExpiresAtUtc = DateTime.UtcNow.AddMinutes(-1);
            await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.NotFound,
            (await Send(outsider, "/api/auth/owner-invitations/inspect", new { token })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,
            (await Send(outsider, "/api/auth/owner-invitations/accept",
                new { token, password = "ExpiredInvitePassword123!" })).StatusCode);
        using (var scope = factory.Services.CreateScope())
            Assert.False(await scope.ServiceProvider.GetRequiredService<AppDbContext>()
                .Users.AnyAsync(x => x.Email == "expired-invite@example.test"));
    }

    private static async Task<HttpResponseMessage> Send(HttpClient client, string path, object input)
    {
        var csrf = await client.GetFromJsonAsync<JsonElement>("/api/auth/csrf");
        using var request = new HttpRequestMessage(HttpMethod.Post, path) {
            Content = JsonContent.Create(input)
        };
        request.Headers.Add("X-CSRF-TOKEN", csrf.GetProperty("token").GetString()!);
        return await client.SendAsync(request);
    }

    private sealed class FakeMailer : IOwnerInvitationMailer
    {
        public bool IsEnabled { get; set; } = true;
        public bool IsConfigured => IsEnabled;
        public bool Fail { get; set; }
        public string? Link { get; private set; }
        public string? SentEmail { get; private set; }

        public Task SendAsync(string email, string link, CancellationToken cancellationToken)
        {
            Link = link;
            SentEmail = email;
            if (Fail) throw new InvalidOperationException("Simulated email failure.");
            return Task.CompletedTask;
        }
    }
}
