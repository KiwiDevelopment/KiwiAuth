using System.Security.Claims;
using KiwiAuth.Data;
using KiwiAuth.Models;
using KiwiAuth.Models.Requests;
using KiwiAuth.Options;
using KiwiAuth.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;

namespace KiwiAuth.Endpoints;

internal static class MfaEndpoints
{
    internal static IEndpointRouteBuilder MapMfaEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/auth/mfa").WithTags("MFA");

        // Authenticated endpoints (require a valid access token)
        group.MapGet("/setup", HandleSetupAsync).RequireAuthorization();
        group.MapPost("/enable", HandleEnableAsync).RequireAuthorization();
        group.MapPost("/disable", HandleDisableAsync).RequireAuthorization();
        group.MapPost("/recovery-codes", HandleRegenerateRecoveryCodesAsync).RequireAuthorization();

        // Called during login — uses MFA session token, not a full access token
        group.MapPost("/verify", HandleVerifyAsync);

        return app;
    }

    // GET /auth/mfa/setup
    // Returns the TOTP secret and an otpauth:// URI for QR code display.
    // Does not enable MFA yet — the user must confirm by calling /enable.
    private static async Task<IResult> HandleSetupAsync(
        HttpContext context,
        UserManager<ApplicationUser> userManager,
        MfaService mfaService)
    {
        var user = await GetCurrentUserAsync(context, userManager);
        if (user is null) return Results.Unauthorized();

        if (user.TwoFactorEnabled)
            return Results.BadRequest(ApiResponse.Fail("mfa_already_enabled", "MFA is already enabled on this account."));

        var result = await mfaService.SetupAsync(user);
        return Results.Ok(ApiResponse.Ok(new
        {
            secret = result.Secret,
            authenticatorUri = result.AuthenticatorUri,
        }));
    }

    // POST /auth/mfa/enable
    // Verifies the first TOTP code to confirm setup and activates MFA.
    // Returns one-time recovery codes — store these securely.
    private static async Task<IResult> HandleEnableAsync(
        MfaCodeRequest request,
        HttpContext context,
        UserManager<ApplicationUser> userManager,
        MfaService mfaService)
    {
        var user = await GetCurrentUserAsync(context, userManager);
        if (user is null) return Results.Unauthorized();

        if (user.TwoFactorEnabled)
            return Results.BadRequest(ApiResponse.Fail("mfa_already_enabled", "MFA is already enabled."));

        if (string.IsNullOrWhiteSpace(request.Code))
            return Results.BadRequest(ApiResponse.Fail("validation_error", "TOTP code is required."));

        var (success, errorCode, recoveryCodes) = await mfaService.EnableAsync(user, request.Code);
        if (!success)
            return Results.BadRequest(ApiResponse.Fail(errorCode!, "Invalid TOTP code. Check your authenticator app."));

        return Results.Ok(ApiResponse.Ok(new
        {
            recoveryCodes,
            message = "MFA enabled. Save your recovery codes — they will not be shown again.",
        }));
    }

    // POST /auth/mfa/disable
    // Requires the current TOTP code for confirmation before disabling MFA.
    private static async Task<IResult> HandleDisableAsync(
        MfaCodeRequest request,
        HttpContext context,
        UserManager<ApplicationUser> userManager,
        MfaService mfaService)
    {
        var user = await GetCurrentUserAsync(context, userManager);
        if (user is null) return Results.Unauthorized();

        if (!user.TwoFactorEnabled)
            return Results.BadRequest(ApiResponse.Fail("mfa_not_enabled", "MFA is not enabled on this account."));

        if (string.IsNullOrWhiteSpace(request.Code))
            return Results.BadRequest(ApiResponse.Fail("validation_error", "TOTP code is required to disable MFA."));

        var (success, errorCode) = await mfaService.DisableAsync(user, request.Code);
        if (!success)
            return Results.BadRequest(ApiResponse.Fail(errorCode!, "Invalid TOTP code."));

        return Results.Ok(ApiResponse.Ok(new { message = "MFA has been disabled." }));
    }

    // POST /auth/mfa/verify
    // Completes the login flow when MFA is required.
    // Accepts either a TOTP code or a recovery code.
    private static async Task<IResult> HandleVerifyAsync(
        MfaVerifyRequest request,
        HttpContext context,
        UserManager<ApplicationUser> userManager,
        AuthService authService,
        MfaService mfaService,
        TokenService tokenService,
        IOptions<KiwiAuthOptions> kiwiOptions)
    {
        if (string.IsNullOrWhiteSpace(request.MfaSessionToken) || string.IsNullOrWhiteSpace(request.Code))
            return Results.BadRequest(ApiResponse.Fail("validation_error", "MFA session token and code are required."));

        var userId = tokenService.ValidateMfaSessionToken(request.MfaSessionToken);
        if (userId is null)
            return Results.Json(ApiResponse.Fail("invalid_session", "MFA session token is invalid or expired."), statusCode: 401);

        var user = await userManager.FindByIdAsync(userId);
        if (user is null || !user.TwoFactorEnabled)
            return Results.Json(ApiResponse.Fail("invalid_session", "MFA session is invalid."), statusCode: 401);

        bool verified;
        if (request.IsRecoveryCode)
        {
            verified = await mfaService.RedeemRecoveryCodeAsync(user, request.Code);
        }
        else
        {
            verified = await mfaService.VerifyTotpAsync(user, request.Code);
        }

        if (!verified)
            return Results.Json(ApiResponse.Fail("invalid_code", "Invalid MFA code."), statusCode: 401);

        var authResult = await authService.IssueTokensAsync(user, GetIp(context));

        AuthEndpoints.SetRefreshTokenCookie(context, authResult.RefreshToken, kiwiOptions.Value);

        return Results.Ok(ApiResponse.Ok(new { accessToken = authResult.AccessToken, user = authResult.User }));
    }

    // POST /auth/mfa/recovery-codes
    // Generates a new set of recovery codes (invalidates all previous ones).
    // Requires a valid TOTP code as confirmation.
    private static async Task<IResult> HandleRegenerateRecoveryCodesAsync(
        MfaCodeRequest request,
        HttpContext context,
        UserManager<ApplicationUser> userManager,
        MfaService mfaService)
    {
        var user = await GetCurrentUserAsync(context, userManager);
        if (user is null) return Results.Unauthorized();

        if (!user.TwoFactorEnabled)
            return Results.BadRequest(ApiResponse.Fail("mfa_not_enabled", "MFA is not enabled."));

        if (string.IsNullOrWhiteSpace(request.Code))
            return Results.BadRequest(ApiResponse.Fail("validation_error", "TOTP code is required."));

        var isValid = await mfaService.VerifyTotpAsync(user, request.Code);
        if (!isValid)
            return Results.BadRequest(ApiResponse.Fail("invalid_code", "Invalid TOTP code."));

        var codes = await mfaService.RegenerateRecoveryCodesAsync(user);
        return Results.Ok(ApiResponse.Ok(new
        {
            recoveryCodes = codes,
            message = "New recovery codes generated. Previous codes are now invalid.",
        }));
    }

    private static async Task<ApplicationUser?> GetCurrentUserAsync(
        HttpContext context, UserManager<ApplicationUser> userManager)
    {
        var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier)
                     ?? context.User.FindFirstValue("sub");

        return userId is null ? null : await userManager.FindByIdAsync(userId);
    }

    private static string GetIp(HttpContext context) =>
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
}
