using System.Security.Claims;
using KiwiAuth.Data;
using KiwiAuth.Models;
using KiwiAuth.Models.Requests;
using KiwiAuth.Options;
using KiwiAuth.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Mvc;

namespace KiwiAuth.Endpoints;

internal static class AuthEndpoints
{
    private const string RefreshTokenCookie = "kiwi_refresh_token";

    internal static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/auth").WithTags("Auth");

        group.MapPost("/register", HandleRegisterAsync);
        group.MapPost("/login", HandleLoginAsync);
        group.MapPost("/refresh", HandleRefreshAsync);
        group.MapPost("/logout", HandleLogoutAsync);
        group.MapGet("/me", HandleMeAsync).RequireAuthorization();
        group.MapGet("/google/login", HandleGoogleLoginAsync);
        group.MapGet("/google/callback", HandleGoogleCallbackAsync);
        group.MapGet("/confirm-email", HandleConfirmEmailAsync);
        group.MapPost("/forgot-password", HandleForgotPasswordAsync);
        group.MapPost("/reset-password", HandleResetPasswordAsync);

        return app;
    }

    // POST /auth/register
    private static async Task<IResult> HandleRegisterAsync(
        RegisterRequest request,
        HttpContext context,
        AuthService authService,
        IOptions<KiwiAuthOptions> kiwiOptions)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
            return Results.BadRequest(ApiResponse.Fail("validation_error", "Email and password are required."));

        var (success, errorCode, errorMessage, result, emailConfirmationRequired) = await authService.RegisterAsync(
            request.Email, request.Password, request.FullName, GetIp(context));

        if (!success)
            return Results.BadRequest(ApiResponse.Fail(errorCode!, errorMessage!));

        if (emailConfirmationRequired)
            return Results.Ok(ApiResponse.Ok(new { emailConfirmationRequired = true, message = "Registration successful. Please check your email to confirm your account." }));

        SetRefreshTokenCookie(context, result!.RefreshToken, kiwiOptions.Value);

        return Results.Ok(ApiResponse.Ok(new { accessToken = result.AccessToken, user = result.User }));
    }

    // POST /auth/login
    private static async Task<IResult> HandleLoginAsync(
        LoginRequest request,
        HttpContext context,
        AuthService authService,
        IOptions<KiwiAuthOptions> kiwiOptions)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
            return Results.BadRequest(ApiResponse.Fail("validation_error", "Email and password are required."));

        var (success, errorCode, errorMessage, result) = await authService.LoginAsync(
            request.Email, request.Password, GetIp(context));

        if (!success)
            return Results.Json(ApiResponse.Fail(errorCode!, errorMessage!), statusCode: 401);

        // MFA is enabled — return a short-lived session token for the second factor.
        // The client calls POST /auth/mfa/verify to complete login.
        if (result!.RequiresMfa)
            return Results.Ok(ApiResponse.Ok(new { requiresMfa = true, mfaSessionToken = result.MfaSessionToken }));

        SetRefreshTokenCookie(context, result.RefreshToken!, kiwiOptions.Value);

        return Results.Ok(ApiResponse.Ok(new { accessToken = result.AccessToken, user = result.User }));
    }

    // POST /auth/refresh
    // Refresh token is read from the HttpOnly cookie — no body needed.
    private static async Task<IResult> HandleRefreshAsync(
        HttpContext context,
        AuthService authService,
        IOptions<KiwiAuthOptions> kiwiOptions)
    {
        var rawToken = context.Request.Cookies[RefreshTokenCookie];
        if (string.IsNullOrEmpty(rawToken))
            return Results.Json(ApiResponse.Fail("missing_token", "No refresh token provided."), statusCode: 401);

        var (success, errorCode, errorMessage, result) = await authService.RefreshAsync(rawToken, GetIp(context));

        if (!success)
            return Results.Json(ApiResponse.Fail(errorCode!, errorMessage!), statusCode: 401);

        SetRefreshTokenCookie(context, result!.RefreshToken, kiwiOptions.Value);

        return Results.Ok(ApiResponse.Ok(new { accessToken = result.AccessToken, user = result.User }));
    }

    // POST /auth/logout
    private static async Task<IResult> HandleLogoutAsync(
        HttpContext context,
        AuthService authService)
    {
        var rawToken = context.Request.Cookies[RefreshTokenCookie];

        if (!string.IsNullOrEmpty(rawToken))
            await authService.LogoutAsync(rawToken, GetIp(context));

        // Clear the cookie regardless of whether the token was found in the DB.
        context.Response.Cookies.Delete(RefreshTokenCookie, new CookieOptions { Path = "/auth" });

        return Results.Ok(ApiResponse.Ok());
    }

    // GET /auth/me
    private static async Task<IResult> HandleMeAsync(
        HttpContext context,
        UserManager<ApplicationUser> userManager)
    {
        // JwtBearerMiddleware maps the 'sub' claim to ClaimTypes.NameIdentifier by default.
        var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier)
                     ?? context.User.FindFirstValue("sub");

        if (string.IsNullOrEmpty(userId))
            return Results.Unauthorized();

        var user = await userManager.FindByIdAsync(userId);
        if (user is null)
            return Results.NotFound(ApiResponse.Fail("user_not_found", "User not found."));

        var roles = await userManager.GetRolesAsync(user);

        return Results.Ok(ApiResponse.Ok(new UserInfo
        {
            Id = user.Id,
            Email = user.Email!,
            FullName = user.FullName,
            Roles = [.. roles],
        }));
    }

    // GET /auth/google/login
    // Initiates the Google OAuth challenge. After consent, Google redirects to
    // /auth/google/callback-oidc (handled internally by ASP.NET), which then
    // redirects to /auth/google/callback (our endpoint below).
    private static IResult HandleGoogleLoginAsync()
    {
        var props = new AuthenticationProperties { RedirectUri = "/auth/google/callback" };
        return Results.Challenge(props, ["Google"]);
    }

    // GET /auth/google/callback
    // ASP.NET Core has already handled the OAuth code exchange at /auth/google/callback-oidc
    // and stored the external principal in the external cookie scheme. We read it here.
    private static async Task<IResult> HandleGoogleCallbackAsync(
        HttpContext context,
        UserManager<ApplicationUser> userManager,
        AuthService authService,
        IOptions<KiwiAuthOptions> kiwiOptions)
    {
        var options = kiwiOptions.Value;

        var result = await context.AuthenticateAsync(IdentityConstants.ExternalScheme);
        if (!result.Succeeded)
            return Results.Redirect($"{options.Frontend.GoogleErrorRedirectUrl}?error=auth_failed");

        var email = result.Principal.FindFirstValue(ClaimTypes.Email);
        if (string.IsNullOrEmpty(email))
            return Results.Redirect($"{options.Frontend.GoogleErrorRedirectUrl}?error=no_email");

        var user = await userManager.FindByEmailAsync(email);
        if (user is null)
        {
            // First Google login: create a local account automatically.
            var fullName = result.Principal.FindFirstValue(ClaimTypes.Name);
            user = new ApplicationUser
            {
                UserName = email,
                Email = email,
                FullName = fullName,
                EmailConfirmed = true, // Google has already verified this email.
            };

            var createResult = await userManager.CreateAsync(user);
            if (!createResult.Succeeded)
                return Results.Redirect($"{options.Frontend.GoogleErrorRedirectUrl}?error=create_failed");

            await userManager.AddToRoleAsync(user, "User");
        }

        var authResult = await authService.IssueTokensAsync(user, GetIp(context));
        SetRefreshTokenCookie(context, authResult.RefreshToken, options);

        // MVP trade-off: the access token is passed as a query param so the frontend
        // can pick it up and store it in memory. The frontend should read and discard
        // the param from the URL immediately (e.g., history.replaceState).
        //
        // Production alternative: a server-side one-time code that the frontend
        // exchanges for the access token via a POST. Eliminates token exposure in URL
        // and browser history.
        var redirectUrl = $"{options.Frontend.GoogleSuccessRedirectUrl}?token={Uri.EscapeDataString(authResult.AccessToken)}";
        return Results.Redirect(redirectUrl);
    }

    // GET /auth/confirm-email?userId=...&token=...
    // The link in the confirmation email points here (or to a frontend page that calls this).
    private static async Task<IResult> HandleConfirmEmailAsync(
        [FromQuery] string userId,
        [FromQuery] string token,
        AuthService authService)
    {
        if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(token))
            return Results.BadRequest(ApiResponse.Fail("validation_error", "userId and token are required."));

        var (success, errorCode, errorMessage) = await authService.ConfirmEmailAsync(userId, token);

        if (!success)
            return Results.BadRequest(ApiResponse.Fail(errorCode!, errorMessage!));

        return Results.Ok(ApiResponse.Ok(new { message = "Email confirmed successfully." }));
    }

    // POST /auth/forgot-password
    // Always returns 200 — never reveals whether the email is registered.
    private static async Task<IResult> HandleForgotPasswordAsync(
        ForgotPasswordRequest request,
        AuthService authService)
    {
        if (!string.IsNullOrWhiteSpace(request.Email))
            await authService.ForgotPasswordAsync(request.Email);

        return Results.Ok(ApiResponse.Ok(new { message = "If an account with that email exists, a password reset link has been sent." }));
    }

    // POST /auth/reset-password
    private static async Task<IResult> HandleResetPasswordAsync(
        ResetPasswordRequest request,
        AuthService authService)
    {
        if (string.IsNullOrWhiteSpace(request.UserId) || string.IsNullOrWhiteSpace(request.Token) || string.IsNullOrWhiteSpace(request.NewPassword))
            return Results.BadRequest(ApiResponse.Fail("validation_error", "UserId, token, and new password are required."));

        var (success, errorCode, errorMessage) = await authService.ResetPasswordAsync(
            request.UserId, request.Token, request.NewPassword);

        if (!success)
            return Results.BadRequest(ApiResponse.Fail(errorCode!, errorMessage!));

        return Results.Ok(ApiResponse.Ok(new { message = "Password has been reset successfully." }));
    }

    internal static void SetRefreshTokenCookie(HttpContext context, string token, KiwiAuthOptions options)
    {
        context.Response.Cookies.Append(RefreshTokenCookie, token, new CookieOptions
        {
            HttpOnly = true,              // Not accessible via JavaScript
            Secure = true,               // HTTPS only — disable for local HTTP dev if needed
            SameSite = SameSiteMode.Strict,
            Expires = DateTimeOffset.UtcNow.AddDays(options.RefreshToken.DaysToLive),
            Path = "/auth",              // Cookie is only sent to /auth/* routes
        });
    }

    private static string GetIp(HttpContext context) =>
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
}
