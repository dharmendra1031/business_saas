using System.Security.Cryptography.X509Certificates;
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
}).AddEntityFrameworkStores<AppDbContext>().AddSignInManager();

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

builder.Services.AddAuthorization();
builder.Services.AddAntiforgery(options => options.HeaderName = "X-CSRF-TOKEN");
builder.Services.AddProblemDetails(options =>
    options.CustomizeProblemDetails = context =>
        context.ProblemDetails.Extensions["requestId"] = context.HttpContext.TraceIdentifier);

var app = builder.Build();

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
    await next();
});

app.UseAuthentication();

// Browser sessions use HttpOnly cookies. Every write, including login/logout,
// requires the CSRF token obtained from GET /api/auth/csrf.
app.Use(async (context, next) =>
{
    var method = context.Request.Method;
    if (context.Request.Path.StartsWithSegments("/api") &&
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

app.MapGet("/api/health", () => Results.Ok(new { status = "ok" }));
app.MapAuthEndpoints();
app.MapBusinessEndpoints();
app.MapBusinessStaffEndpoints();
app.MapRetailMasterEndpoints();
app.MapRetailProductEndpoints();
app.MapRetailContactEndpoints();
app.MapRetailStockEndpoints();
app.MapRetailAdjustmentEndpoints();
app.MapPlatformEndpoints();

app.Run();

public partial class Program;
