using System.Net.Http.Json;
using System.Text.Json;
using BusinessSaas.Api.Data;
using BusinessSaas.Api.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BusinessSaas.Tests;

internal static class BusinessFixtures
{
    // Arrange existing tenant data for tests whose subject is outside provisioning.
    internal static async Task<long> SeedAsync(IServiceProvider services, HttpClient owner,
        string name, string key, long businessTypeId = 1)
    {
        var account = await owner.GetFromJsonAsync<JsonElement>("/api/auth/me");
        var ownerId = account.GetProperty("id").GetInt64();
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        var type = await db.BusinessTypes.SingleAsync(x => x.Id == businessTypeId);

        var business = new Business
        {
            Name = name,
            BusinessTypeId = type.Id,
            CreatedByUserId = ownerId,
            CreationKey = key,
            CountryCode = "IN",
            CurrencyCode = "INR",
            TimeZoneId = "Asia/Kolkata"
        };
        db.Businesses.Add(business);
        await db.SaveChangesAsync();

        db.BusinessUsers.Add(new BusinessUser
        {
            BusinessId = business.Id, UserId = ownerId, RoleCode = "Owner"
        });
        db.BusinessLocations.Add(new BusinessLocation
        {
            BusinessId = business.Id, Code = "MAIN", Name = "Main Shop", IsPrimary = true
        });
        db.BusinessSubscriptions.Add(new BusinessSubscription
        {
            BusinessId = business.Id,
            TrialEndsAtUtc = DateTime.UtcNow.AddDays(configuration.GetValue<int>("Saas:TrialDays"))
        });
        if (type.Code == "RETAIL") RetailModule.AddBusinessDefaults(db, business.Id);

        await db.SaveChangesAsync();
        return business.Id;
    }
}
