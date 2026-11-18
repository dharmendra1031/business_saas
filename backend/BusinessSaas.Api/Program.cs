using System.Security.Cryptography.X509Certificates;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using BusinessSaas.Api.Data;
using BusinessSaas.Api.Features;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("Default");
if (string.IsNullOrWhiteSpace(connectionString))
    throw new InvalidOperationException("ConnectionStrings:Default is required.");

builder.Services.AddDbContext<AppDbContext>(options => options.UseSqlServer(connectionString));
builder.Services.AddHttpClient<IRazorpayGateway, RazorpayGateway>(client =>
{
    client.BaseAddress = new Uri("https://api.razorpay.com/v1/");
    client.Timeout = TimeSpan.FromSeconds(15);
});

// Use a shared encrypted key ring when deployment settings are provided.
// Keep certificate files and passwords outside source control.
var keyDirectory = builder.Configuration["DataProtection:KeyDirectory"];
if (!string.IsNullOrWhiteSpace(keyDirectory))
{
    var certificatePath = builder.Configuration["DataProtection:CertificatePath"];
    var certificatePassword = builder.Configuration["DataProtection:CertificatePassword"];
    if (string.IsNullOrWhiteSpace(certificatePath) || string.IsNullOrWhiteSpace(certificatePassword))
        throw new InvalidOperationException("DataProtection requires a certificate and password.");

    builder.Services.AddDataProtection()
        .SetApplicationName("BusinessSaas")
        .PersistKeysToFileSystem(new DirectoryInfo(keyDirectory))
        .ProtectKeysWithCertificate(X509CertificateLoader.LoadPkcs12FromFile(
            certificatePath, certificatePassword));
}

builder.Services.AddIdentityCore<AppUser>(options =>
{
    options.User.RequireUniqueEmail = true;
    options.SignIn.RequireConfirmedEmail = !builder.Environment.IsDevelopment();
    options.Password.RequiredLength = 8;
    options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
    options.Lockout.MaxFailedAccessAttempts = 5;
}).AddEntityFrameworkStores<AppDbContext>().AddSignInManager().AddDefaultTokenProviders();

builder.Services.AddAuthentication(IdentityConstants.ApplicationScheme)
    .AddIdentityCookies();

builder.Services.ConfigureApplicationCookie(options =>
{
    options.Cookie.Name = "BusinessSaas.Auth";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
        ? CookieSecurePolicy.SameAsRequest
        : CookieSecurePolicy.Always;
    options.Events.OnRedirectToLogin = context =>
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        return Task.CompletedTask;
    };
    options.Events.OnRedirectToAccessDenied = context =>
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        return Task.CompletedTask;
    };
});

builder.Services.Configure<CookieAuthenticationOptions>(IdentityConstants.TwoFactorUserIdScheme, options =>
{
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
        ? CookieSecurePolicy.SameAsRequest
        : CookieSecurePolicy.Always;
    options.ExpireTimeSpan = TimeSpan.FromMinutes(5);
});

builder.Services.AddAuthorization();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("login", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 30,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = true
        }));
    options.AddPolicy("register", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 10,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = true
        }));
    options.AddPolicy("mfa", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 15,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = true
        }));
});
builder.Services.AddAntiforgery(options => options.HeaderName = "X-CSRF-TOKEN");
builder.Services.AddProblemDetails(options =>
    options.CustomizeProblemDetails = context =>
        context.ProblemDetails.Extensions["requestId"] = context.HttpContext.TraceIdentifier);

var app = builder.Build();

if (args.Contains("--bootstrap-platform-owner", StringComparer.Ordinal))
{
    var userId = await PlatformOwnerBootstrap.CreateAsync(app.Services, app.Configuration,
        Environment.GetEnvironmentVariable("PLATFORM_BOOTSTRAP_EMAIL"),
        Environment.GetEnvironmentVariable("PLATFORM_BOOTSTRAP_PASSWORD"));
    Console.WriteLine($"Platform owner created with numeric User ID {userId}.");
    Console.WriteLine("Set Platform__OwnerUserId to this ID in the API host configuration and restart.");
    return;
}

if (app.Environment.IsDevelopment())
{
    // Local SQL Server databases use reviewed migrations; SQLite stays in-memory for tests.
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    if (db.Database.IsSqlServer())
        await db.Database.MigrateAsync();
    else
        await db.Database.EnsureCreatedAsync();
}
else
{
    app.UseExceptionHandler();
    app.UseHsts();
    app.UseHttpsRedirection();
}

// Correlate a user's error reference with server logs without exposing internals.
app.Use(async (context, next) =>
{
    context.Response.Headers["X-Request-Id"] = context.TraceIdentifier;
    if (context.Request.Path.StartsWithSegments("/api"))
    {
        context.Response.Headers["Cache-Control"] = "no-store";
        context.Response.Headers["X-Content-Type-Options"] = "nosniff";
        context.Response.Headers["Referrer-Policy"] = "no-referrer";
    }
    await next();
});

app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();

// The platform owner cannot access tenant operations, even with old business memberships.
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/api/businesses") &&
        PlatformOwner.IsConfiguredOwner(context.User, context.RequestServices.GetRequiredService<IConfiguration>()))
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        return;
    }

    await next();
});

// Browser sessions use HttpOnly cookies. Every write, including login/logout,
// requires the CSRF token obtained from GET /api/auth/csrf.
app.Use(async (context, next) =>
{
    var method = context.Request.Method;
    var webhook = string.Equals(context.Request.Path.Value,
        "/api/payments/razorpay/webhook", StringComparison.OrdinalIgnoreCase);
    if (!webhook && context.Request.Path.StartsWithSegments("/api") &&
        method is "POST" or "PUT" or "PATCH" or "DELETE")
    {
        try
        {
            await context.RequestServices.GetRequiredService<IAntiforgery>()
                .ValidateRequestAsync(context);
        }
        catch (AntiforgeryValidationException)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsJsonAsync(new
            {
                message = "Invalid security token. Refresh the page and try again."
            });
            return;
        }
    }

    await next();
});

app.UseAuthorization();

// All tenant write endpoints respect the paid expiry, including older retail routes.
// Payment checkout is still available after a trial or paid period has expired.
app.Use(async (context, next) =>
{
    if (context.Request.Method is "POST" or "PUT" or "PATCH" or "DELETE" &&
        context.Request.Path.StartsWithSegments("/api/businesses", out var rest))
    {
        var path = rest.Value?.TrimStart('/').Split('/') ?? [];
        if (path.Length >= 2 && long.TryParse(path[0], out var businessId) &&
            !(path[1] == "billing" && path.Length >= 3 && path[2] == "checkout"))
        {
            var db = context.RequestServices.GetRequiredService<AppDbContext>();
            var subscription = await db.BusinessSubscriptions.AsNoTracking()
                .SingleOrDefaultAsync(x => x.BusinessId == businessId);
            if (subscription is not null && !SubscriptionAccess.CanOperate(subscription, DateTime.UtcNow))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsJsonAsync(new
                {
                    message = "Subscription access has expired or been suspended."
                });
                return;
            }
        }
    }

    await next();
});

app.MapGet("/api/health", () => Results.Ok(new { status = "ok" }));
app.MapGet("/api/ready", async (AppDbContext db, ILogger<Program> logger) =>
{
    if (!app.Environment.IsDevelopment() && string.IsNullOrWhiteSpace(keyDirectory))
    {
        logger.LogError("Persistent encrypted Data Protection keys are required for readiness");
        return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
    }

    try
    {
        if (!await db.Database.CanConnectAsync() ||
            db.Database.IsSqlServer() && (await db.Database.GetPendingMigrationsAsync()).Any())
            return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);

        return Results.Ok(new { status = "ready" });
    }
    catch (Exception error)
    {
        logger.LogError(error, "Database readiness check failed");
        return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
    }
});
app.MapAuthEndpoints();
app.MapBusinessEndpoints();
app.MapBusinessStaffEndpoints();
app.MapRetailMasterEndpoints();
app.MapRetailProductEndpoints();
app.MapRetailContactEndpoints();
app.MapRetailStockEndpoints();
app.MapRetailAdjustmentEndpoints();
app.MapRetailPurchaseEndpoints();
app.MapPlatformEndpoints();
app.MapPlatformBillingEndpoints();
app.MapRazorpayPaymentEndpoints();
app.MapPlatformPaymentReviewEndpoints();

app.Run();

public partial class Program;
