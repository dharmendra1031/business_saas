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
        });

        group.MapPost("/login", async (LoginRequest request,
            UserManager<AppUser> users, SignInManager<AppUser> signIn,
            ILogger<Program> logger) =>
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

            if (!result.Succeeded)
                return Results.Unauthorized();

            logger.LogInformation("User {UserId} signed in", user.Id);
            return Results.NoContent();
        });

        group.MapGet("/me", (ClaimsPrincipal principal) =>
            Results.Ok(new
            {
                id = long.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier)!),
                email = principal.Identity?.Name
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

    private sealed record RegisterRequest(string? Email, string? Password);
    private sealed record LoginRequest(string? Email, string? Password);
}
