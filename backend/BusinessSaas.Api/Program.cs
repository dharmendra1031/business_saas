using System.Security.Cryptography.X509Certificates;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using BusinessSaas.Api.Data;
using BusinessSaas.Api.Features;
using BusinessSaas.Api.Reports;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Acceptance is opt-in and never permits live payment. It is not a production switch.
if (builder.Environment.IsEnvironment("RetailAcceptance") &&
    (!builder.Configuration.GetValue<bool>("RetailAcceptance:Enabled") ||
     builder.Configuration.GetValue<bool>("Payments:AllowLive") ||
     !string.Equals(builder.Configuration["Razorpay:Mode"], "Test", StringComparison.OrdinalIgnoreCase)))
    throw new InvalidOperationException("RetailAcceptance requires explicit enablement and test-only payments.");

var connectionString = builder.Configuration.GetConnectionString("Default");
if (string.IsNullOrWhiteSpace(connectionString))
    throw new InvalidOperationException("ConnectionStrings:Default is required.");

builder.Services.AddDbContext<AppDbContext>(options => options.UseSqlServer(connectionString));
builder.Services.AddSingleton<IOwnerInvitationMailer, SmtpOwnerInvitationMailer>();
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

// Maintenance affects authorized tenant operations, not login, health, public status or recovery.
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/api/businesses", out var rest))
    {
        var parts = rest.Value?.Trim('/').Split('/') ?? [];
        if (parts.Length >= 2 && long.TryParse(parts[0], out var businessId) &&
            long.TryParse(context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value,
                out var actorId))
        {
            var db = context.RequestServices.GetRequiredService<AppDbContext>();
            if (await db.BusinessUsers.AsNoTracking().AnyAsync(x =>
                x.BusinessId == businessId && x.UserId == actorId))
            {
                var window = await PlatformStatusEndpoints.ActiveMaintenance(db);
                var isWrite = context.Request.Method is "POST" or "PUT" or "PATCH" or "DELETE";
                if (window is not null && (window.Mode == "Unavailable" || isWrite))
                {
                    context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                    context.Response.Headers.RetryAfter = Math.Max(1, (int)
                        Math.Ceiling((window.EndUtc - DateTime.UtcNow).TotalSeconds)).ToString();
                    await context.Response.WriteAsJsonAsync(new {
                        code = "PLATFORM_MAINTENANCE", window.Mode, window.Title,
                        message = window.Message, window.EndUtc
                    });
                    return;
                }
                if (isWrite)
                {
                    var environment = context.RequestServices.GetRequiredService<IWebHostEnvironment>();
                    foreach (var module in PlatformModuleReleaseEndpoints.ModulesForWrite(parts))
                    {
                        if (!await PlatformModuleReleaseEndpoints.Available(db, environment, module))
                        {
                            context.Response.StatusCode = StatusCodes.Status409Conflict;
                            await context.Response.WriteAsJsonAsync(new {
                                code = "MODULE_NOT_AVAILABLE",
                                message = $"Module {module} is currently unavailable for new changes."
                            });
                            return;
                        }
                    }
                }
            }
        }
    }
    await next();
});

// All tenant write endpoints respect the paid expiry, including older retail routes.
// Payment checkout is still available after a trial or paid period has expired.
app.Use(async (context, next) =>
{
    if (context.Request.Method is "POST" or "PUT" or "PATCH" or "DELETE" &&
        context.Request.Path.StartsWithSegments("/api/businesses", out var rest))
    {
        var path = rest.Value?.Trim('/').Split('/') ?? [];
        if (path.Length >= 2 && long.TryParse(path[0], out var businessId) &&
            !(path.Length == 3 && path[1].Equals("billing", StringComparison.OrdinalIgnoreCase) &&
                path[2].Equals("checkout", StringComparison.OrdinalIgnoreCase)))
        {
            var db = context.RequestServices.GetRequiredService<AppDbContext>();
            var subscription = await db.BusinessSubscriptions.AsNoTracking()
                .SingleOrDefaultAsync(x => x.BusinessId == businessId, context.RequestAborted);
            if (subscription is null || !SubscriptionAccess.CanOperate(subscription, DateTime.UtcNow))
            {
                // Never disclose a tenant's subscription status to a non-member.
                if (!long.TryParse(context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value,
                        out var actorId) ||
                    !await db.BusinessUsers.AsNoTracking().AnyAsync(x =>
                        x.BusinessId == businessId && x.UserId == actorId, context.RequestAborted))
                {
                    context.Response.StatusCode = StatusCodes.Status404NotFound;
                    return;
                }

                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsJsonAsync(new
                {
                    message = "Subscription access is unavailable, expired or suspended."
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
app.MapOwnerInvitationEndpoints();
app.MapBusinessEndpoints();
app.MapBusinessStaffEndpoints();
app.MapRetailModule();
app.MapPlatformEndpoints();
app.MapPlatformBusinessProvisioning();
app.MapPlatformTypeAvailability();
app.MapPlatformModuleRelease();
app.MapPlatformStatus();
app.MapPlatformBillingEndpoints();
app.MapRazorpayPaymentEndpoints();
app.MapPlatformPaymentReviewEndpoints();
app.MapBillingDocumentEndpoints();

// Opt-in on Windows development hosts only; finish before accepting a first sale.
// A failed warm-up must not block access to checkout or historical data.
if (app.Environment.IsDevelopment() && OperatingSystem.IsWindows() &&
    app.Configuration.GetValue<bool>("Receipts:WarmUpOnStart"))
{
    var started = System.Diagnostics.Stopwatch.GetTimestamp();
    try
    {
        RetailReceiptRenderer.WarmUp();
        app.Logger.LogInformation("RDLC warm-up completed in {ElapsedMs:F1} ms",
            System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds);
    }
    catch (Exception error)
    {
        app.Logger.LogWarning(error, "RDLC warm-up failed; normal receipt rendering remains available");
    }
}

app.Run();

public partial class Program;
