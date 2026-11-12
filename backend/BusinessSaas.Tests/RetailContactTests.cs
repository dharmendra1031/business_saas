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

public sealed class RetailContactTests
{
    [Fact]
    public async Task Suppliers_and_customers_validate_tenant_roles_and_trial()
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
        using var other = factory.CreateClient();
        using var manager = factory.CreateClient();
        using var cashier = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await owner.GetAsync("/api/businesses/1/suppliers")).StatusCode);

        await Register(owner, "suppliers-owner@example.test");
        await Register(other, "suppliers-other@example.test");
        await Register(manager, "suppliers-manager@example.test");
        await Register(cashier, "suppliers-cashier@example.test");
        var businessId = await CreateBusiness(owner, "Supplier Shop", "supplier-main-20261112");
        var otherId = await CreateBusiness(other, "Another Supplier Shop", "supplier-other-20261112");
        // Retail APIs must reject a valid membership in a different business template.
        long salonTypeId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var salon = new BusinessType { Code = "SALON", Name = "Salon", IsSupported = true };
            db.BusinessTypes.Add(salon);
            await db.SaveChangesAsync();
            salonTypeId = salon.Id;
        }
        var salonResponse = await Send(owner, HttpMethod.Post, "/api/businesses", new
        {
            name = "Separate Salon", businessTypeId = salonTypeId,
            countryCode = "IN", currencyCode = "INR", timeZoneId = "Asia/Kolkata",
            requestKey = "separate-salon-20261112"
        });
        Assert.Equal(HttpStatusCode.Created, salonResponse.StatusCode);
        var salonId = (await salonResponse.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id").GetInt64();
        Assert.Equal(HttpStatusCode.NotFound,
            (await owner.GetAsync($"/api/businesses/{salonId}/categories")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await owner.GetAsync($"/api/businesses/{salonId}/products")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await owner.GetAsync($"/api/businesses/{salonId}/suppliers")).StatusCode);

        var suppliers = $"/api/businesses/{businessId}/suppliers";
        var customers = $"/api/businesses/{businessId}/customers";

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var managerId = await db.Users.Where(x => x.Email == "suppliers-manager@example.test")
                .Select(x => x.Id).SingleAsync();
            var cashierId = await db.Users.Where(x => x.Email == "suppliers-cashier@example.test")
                .Select(x => x.Id).SingleAsync();
            db.BusinessUsers.AddRange(
                new BusinessUser { BusinessId = businessId, UserId = managerId, RoleCode = "Manager" },
                new BusinessUser { BusinessId = businessId, UserId = cashierId, RoleCode = "Cashier" });
            await db.SaveChangesAsync();
        }

        Assert.Equal(HttpStatusCode.BadRequest,
            (await Send(owner, HttpMethod.Post, suppliers, new { name = "Wholesale", phone = "", email = "" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await Send(owner, HttpMethod.Post, suppliers, new { name = "Wholesale", phone = "", email = "bad@" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await Send(owner, HttpMethod.Post, suppliers, new { name = "Wholesale", phone = "ABC", email = "" })).StatusCode);

        var supplier = await Send(owner, HttpMethod.Post, suppliers,
            new { name = "Wholesale", phone = "+91 1234567890", email = "" });
        Assert.Equal(HttpStatusCode.Created, supplier.StatusCode);
        var supplierId = (await supplier.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id").GetInt64();
        Assert.True(supplierId > 0);

        Assert.Equal(HttpStatusCode.Conflict,
            (await Send(manager, HttpMethod.Post, suppliers,
                new { name = " wholesale ", phone = "2222222222", email = "" })).StatusCode);
        Assert.Equal(HttpStatusCode.Created,
            (await Send(other, HttpMethod.Post, $"/api/businesses/{otherId}/suppliers",
                new { name = "Wholesale", phone = "3333333333", email = "" })).StatusCode);

        var customer = await Send(owner, HttpMethod.Post, customers,
            new { name = "Walk-in Account", phone = "", email = "" });
        Assert.Equal(HttpStatusCode.Created, customer.StatusCode);
        var customerId = (await customer.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id").GetInt64();
        Assert.True(customerId > 0);

        Assert.Equal(HttpStatusCode.Created,
            (await Send(owner, HttpMethod.Post, customers,
                new { name = "Walk-in Account", phone = "", email = "" })).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await manager.GetAsync(suppliers)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await cashier.GetAsync(customers)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync(suppliers)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await Send(other, HttpMethod.Put, suppliers + "/" + supplierId,
                new { name = "Hijacked", phone = "4444444444", email = "", isActive = false })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await Send(other, HttpMethod.Put, customers + "/" + customerId,
                new { name = "Hijacked", phone = "", email = "", isActive = false })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await Send(owner, HttpMethod.Put, customers + "/999999999",
                new { name = "Unknown", phone = "", email = "", isActive = false })).StatusCode);

        var edited = await Send(manager, HttpMethod.Put, suppliers + "/" + supplierId,
            new { name = "Wholesale Ltd", phone = "5555555555", email = "team@example.test", isActive = false });
        Assert.Equal(HttpStatusCode.OK, edited.StatusCode);
        var page = await owner.GetFromJsonAsync<JsonElement>(suppliers + "?search=wholesale&page=1");
        Assert.Equal(1, page.GetProperty("total").GetInt32());
        Assert.False(page.GetProperty("items")[0].GetProperty("isActive").GetBoolean());
        Assert.Equal(HttpStatusCode.BadRequest,
            (await owner.GetAsync(customers + "?page=0")).StatusCode);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var subscription = await db.BusinessSubscriptions.SingleAsync(x => x.BusinessId == businessId);
            subscription.TrialEndsAtUtc = DateTime.UtcNow.AddMinutes(-5);
            await db.SaveChangesAsync();
        }

        Assert.Equal(HttpStatusCode.Forbidden,
            (await Send(owner, HttpMethod.Post, suppliers,
                new { name = "New Supplier", phone = "5555555555", email = "" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await Send(manager, HttpMethod.Put, customers + "/" + customerId,
                new { name = "Blocked", phone = "", email = "", isActive = false })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync(customers)).StatusCode);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Equal(1, await db.RetailSuppliers.CountAsync(x => x.BusinessId == businessId));
            Assert.Equal(2, await db.RetailCustomers.CountAsync(x => x.BusinessId == businessId));
            Assert.Equal(1, await db.RetailSuppliers.CountAsync(x => x.BusinessId == otherId));
        }
    }

    private static async Task Register(HttpClient client, string email)
    {
        Assert.Equal(HttpStatusCode.OK,
            (await Send(client, HttpMethod.Post, "/api/auth/register",
                new { email, password = "TestPassword123!" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent,
            (await Send(client, HttpMethod.Post, "/api/auth/login",
                new { email, password = "TestPassword123!" })).StatusCode);
    }

    private static async Task<long> CreateBusiness(HttpClient client, string name, string requestKey)
    {
        var response = await Send(client, HttpMethod.Post, "/api/businesses", new
        {
            name, businessTypeId = 1, countryCode = "IN", currencyCode = "INR",
            timeZoneId = "Asia/Kolkata", requestKey
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id").GetInt64();
    }

    private static async Task<HttpResponseMessage> Send(HttpClient client,
        HttpMethod method, string path, object body)
    {
        var token = await client.GetFromJsonAsync<JsonElement>("/api/auth/csrf");
        using var request = new HttpRequestMessage(method, path)
        {
            Content = JsonContent.Create(body)
        };
        request.Headers.Add("X-CSRF-TOKEN", token.GetProperty("token").GetString()!);
        return await client.SendAsync(request);
    }
}
