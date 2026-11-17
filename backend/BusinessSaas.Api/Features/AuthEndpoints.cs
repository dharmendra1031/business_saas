using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using BusinessSaas.Api.Data;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Identity;

namespace BusinessSaas.Api.Features;

public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/auth");

        group.MapGet("/csrf", (HttpContext context, IAntiforgery antiforgery) =>
            Results.Ok(new { token = antiforgery.GetAndStoreTokens(context).RequestToken }));

        group.MapGet("/options", (IWebHostEnvironment environment) =>
            Results.Ok(new { canRegister = environment.IsDevelopment() }));

        group.MapPost("/register", async (RegisterRequest request,
            IWebHostEnvironment environment, UserManager<AppUser> users) =>
        {
            if (!environment.IsDevelopment())
                return Results.Problem(
                    "Registration is unavailable until verified email delivery is configured.",
                    statusCode: StatusCodes.Status503ServiceUnavailable);

            var email = request.Email?.Trim() ?? "";
            if (!new EmailAddressAttribute().IsValid(email))
                return Results.BadRequest(new { message = "Enter a valid email address." });

            var result = await users.CreateAsync(new AppUser
            {
                UserName = email,
                Email = email
            }, request.Password ?? "");

            if (!result.Succeeded)
                return Results.BadRequest(new
                {
                    message = string.Join(" ", result.Errors.Select(x => x.Description))
                });

            return Results.Ok(new { message = "Account created. Sign in to continue." });
        }).RequireRateLimiting("register");

        group.MapPost("/login", async (LoginRequest request,
            UserManager<AppUser> users, SignInManager<AppUser> signIn,
            AppDbContext db, IConfiguration configuration, ILogger<Program> logger) =>
        {
            var user = await users.FindByEmailAsync(request.Email?.Trim() ?? "");
            if (user is null)
                return Results.Unauthorized();

            var result = await signIn.PasswordSignInAsync(
                user, request.Password ?? "", isPersistent: false, lockoutOnFailure: true);

            if (result.IsLockedOut)
            {
                logger.LogWarning("Account lockout for user {UserId}", user.Id);
                return Results.Problem("Account temporarily locked. Try again later.", statusCode: 429);
            }

            if (result.RequiresTwoFactor)
                return Results.Ok(new { requiresTwoFactor = true });

            if (!result.Succeeded)
                return Results.Unauthorized();

            logger.LogInformation("User {UserId} signed in", user.Id);
            return Results.NoContent();
        }).RequireRateLimiting("login");

        group.MapGet("/mfa/status", async (ClaimsPrincipal principal, IConfiguration configuration,
            UserManager<AppUser> users) =>
        {
            if (!PlatformOwner.IsConfiguredOwner(principal, configuration))
                return Results.Forbid();

            var account = await users.GetUserAsync(principal);
            if (account is null) return Results.Unauthorized();

            return Results.Ok(new
            {
                enabled = await users.GetTwoFactorEnabledAsync(account),
                verified = PlatformOwner.IsOwner(principal, configuration)
            });
        }).RequireAuthorization();

        group.MapPost("/mfa/setup", async (MfaSetupRequest request,
            ClaimsPrincipal principal, IConfiguration configuration, UserManager<AppUser> users) =>
        {
            if (!PlatformOwner.IsConfiguredOwner(principal, configuration))
                return Results.Forbid();

            var account = await users.GetUserAsync(principal);
            if (account is null) return Results.Unauthorized();
            if (await users.GetTwoFactorEnabledAsync(account))
                return Results.Forbid();
            if (!await users.CheckPasswordAsync(account, request.Password ?? ""))
                return Results.Unauthorized();

            var secret = await users.GetAuthenticatorKeyAsync(account);
            if (string.IsNullOrEmpty(secret))
            {
                var reset = await users.ResetAuthenticatorKeyAsync(account);
                if (!reset.Succeeded)
                    return Results.Problem("Unable to initialize authenticator.", statusCode: 503);
                secret = await users.GetAuthenticatorKeyAsync(account);
            }

            var label = Uri.EscapeDataString(account.Email ?? "Platform Owner");
            var uri = $"otpauth://totp/BusinessSaas:{label}?secret={secret}&issuer=BusinessSaas&digits=6";
            return Results.Ok(new { secret, uri });
        }).RequireAuthorization().RequireRateLimiting("mfa");

        group.MapPost("/mfa/enable", async (MfaEnableRequest request,
            ClaimsPrincipal principal, IConfiguration configuration, UserManager<AppUser> users,
            SignInManager<AppUser> signIn, AppDbContext db) =>
        {
            if (!PlatformOwner.IsConfiguredOwner(principal, configuration))
                return Results.Forbid();

            var account = await users.GetUserAsync(principal);
            if (account is null) return Results.Unauthorized();
            if (await users.GetTwoFactorEnabledAsync(account))
                return Results.Forbid();
            if (!await users.CheckPasswordAsync(account, request.Password ?? ""))
                return Results.Unauthorized();

            var code = (request.Code ?? "").Replace(" ", "").Replace("-", "");
            if (code.Length != 6 || !code.All(char.IsAsciiDigit) ||
                !await users.VerifyTwoFactorTokenAsync(account, TokenOptions.DefaultAuthenticatorProvider, code))
                return Results.BadRequest(new { message = "Invalid authenticator code." });

            var enabled = await users.SetTwoFactorEnabledAsync(account, true);
            if (!enabled.Succeeded)
                return Results.Problem("Unable to enable authenticator.", statusCode: 503);

            var recoveryCodes = (await users.GenerateNewTwoFactorRecoveryCodesAsync(account, 8))?.ToArray();
            if (recoveryCodes is null || recoveryCodes.Length != 8)
                return Results.Problem("Unable to issue recovery codes.", statusCode: 503);

            db.PlatformAuditEvents.Add(new PlatformAuditEvent
            {
                ActorUserId = account.Id,
                Action = "PlatformOwner.MfaEnabled",
                TargetType = "User",
                TargetId = account.Id
            });
            await db.SaveChangesAsync();
            await signIn.SignOutAsync();
            return Results.Ok(new { recoveryCodes });
        }).RequireAuthorization().RequireRateLimiting("mfa");

        group.MapPost("/mfa/verify", async (MfaVerifyRequest request,
            IConfiguration configuration, UserManager<AppUser> users,
            SignInManager<AppUser> signIn, AppDbContext db, ILogger<Program> logger) =>
        {
            var account = await signIn.GetTwoFactorAuthenticationUserAsync();
            if (account is null || !PlatformOwner.IsOwnerId(account.Id, configuration) ||
                !await users.GetTwoFactorEnabledAsync(account))
                return Results.Unauthorized();

            var recovery = request.RecoveryCode?.Trim();
            var code = (request.Code ?? "").Replace(" ", "").Replace("-", "");
            if (recovery is null && (code.Length != 6 || !code.All(char.IsAsciiDigit)))
                return Results.Unauthorized();

            var result = recovery is not null
                ? await signIn.TwoFactorRecoveryCodeSignInAsync(recovery)
                : await signIn.TwoFactorAuthenticatorSignInAsync(code, false, false);

            if (result.IsLockedOut)
                return Results.Problem("Account temporarily locked.", statusCode: 429);
            if (!result.Succeeded)
            {
                // Identity's recovery-code path does not increment the lockout counter.
                if (recovery is not null)
                    await users.AccessFailedAsync(account);
                return Results.Unauthorized();
            }

            db.PlatformAuditEvents.Add(new PlatformAuditEvent
            {
                ActorUserId = account.Id,
                Action = "PlatformOwner.SignIn",
                TargetType = "User",
                TargetId = account.Id
            });
            await db.SaveChangesAsync();
            logger.LogInformation("Platform owner {UserId} completed MFA sign in", account.Id);
            return Results.NoContent();
        }).RequireRateLimiting("mfa");

        group.MapGet("/me", (ClaimsPrincipal principal, IConfiguration configuration) =>
            Results.Ok(new
            {
                id = long.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier)!),
                email = principal.Identity?.Name,
                isPlatformOwner = PlatformOwner.IsConfiguredOwner(principal, configuration),
                mfaVerified = PlatformOwner.IsOwner(principal, configuration)
            })).RequireAuthorization();

        group.MapPost("/logout", async (ClaimsPrincipal principal,
            SignInManager<AppUser> signIn, ILogger<Program> logger) =>
        {
            await signIn.SignOutAsync();
            logger.LogInformation("User {UserId} signed out",
                principal.FindFirstValue(ClaimTypes.NameIdentifier));
            return Results.NoContent();
        }).RequireAuthorization();
    }

    private sealed record MfaSetupRequest(string? Password);
    private sealed record MfaEnableRequest(string? Password, string? Code);
    private sealed record MfaVerifyRequest(string? Code, string? RecoveryCode);
    private sealed record RegisterRequest(string? Email, string? Password);
    private sealed record LoginRequest(string? Email, string? Password);
}
