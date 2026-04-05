using KiwiAuth.Data;
using KiwiAuth.Models;
using KiwiAuth.Options;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace KiwiAuth.Services;

public class MfaService
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly KiwiAuthOptions _options;

    public MfaService(UserManager<ApplicationUser> userManager, IOptions<KiwiAuthOptions> options)
    {
        _userManager = userManager;
        _options = options.Value;
    }

    /// <summary>
    /// Generates (or retrieves) the TOTP authenticator key for the user
    /// and returns the secret + otpauth:// URI for QR code display.
    /// Does NOT enable MFA — call EnableAsync after the user confirms the code.
    /// </summary>
    public async Task<MfaSetupResult> SetupAsync(ApplicationUser user)
    {
        var key = await _userManager.GetAuthenticatorKeyAsync(user);
        if (string.IsNullOrEmpty(key))
        {
            await _userManager.ResetAuthenticatorKeyAsync(user);
            key = await _userManager.GetAuthenticatorKeyAsync(user);
        }

        var issuer = _options.Jwt.Issuer;
        var email = Uri.EscapeDataString(user.Email!);
        var escapedIssuer = Uri.EscapeDataString(issuer);

        // Standard otpauth URI — compatible with Google Authenticator, Authy, 1Password, etc.
        var uri = $"otpauth://totp/{escapedIssuer}:{email}?secret={key}&issuer={escapedIssuer}&algorithm=SHA1&digits=6&period=30";

        return new MfaSetupResult { Secret = key!, AuthenticatorUri = uri };
    }

    /// <summary>
    /// Verifies the provided TOTP code and enables MFA on the account.
    /// Returns recovery codes on success.
    /// </summary>
    public async Task<(bool Success, string? ErrorCode, IReadOnlyList<string>? RecoveryCodes)> EnableAsync(
        ApplicationUser user, string totpCode)
    {
        var isValid = await _userManager.VerifyTwoFactorTokenAsync(
            user, _userManager.Options.Tokens.AuthenticatorTokenProvider, totpCode);

        if (!isValid)
            return (false, "invalid_code", null);

        await _userManager.SetTwoFactorEnabledAsync(user, true);

        var codes = await _userManager.GenerateNewTwoFactorRecoveryCodesAsync(user, _options.Mfa.RecoveryCodeCount);
        return (true, null, codes?.ToList().AsReadOnly());
    }

    /// <summary>
    /// Disables MFA. Requires the current TOTP code as confirmation.
    /// </summary>
    public async Task<(bool Success, string? ErrorCode)> DisableAsync(ApplicationUser user, string totpCode)
    {
        var isValid = await _userManager.VerifyTwoFactorTokenAsync(
            user, _userManager.Options.Tokens.AuthenticatorTokenProvider, totpCode);

        if (!isValid)
            return (false, "invalid_code");

        await _userManager.SetTwoFactorEnabledAsync(user, false);
        await _userManager.ResetAuthenticatorKeyAsync(user);

        return (true, null);
    }

    /// <summary>
    /// Verifies a TOTP code during login. Returns true if valid.
    /// </summary>
    public async Task<bool> VerifyTotpAsync(ApplicationUser user, string code) =>
        await _userManager.VerifyTwoFactorTokenAsync(
            user, _userManager.Options.Tokens.AuthenticatorTokenProvider, code);

    /// <summary>
    /// Redeems a recovery code. Each code can only be used once.
    /// </summary>
    public async Task<bool> RedeemRecoveryCodeAsync(ApplicationUser user, string code)
    {
        var result = await _userManager.RedeemTwoFactorRecoveryCodeAsync(user, code);
        return result.Succeeded;
    }

    /// <summary>
    /// Generates a fresh set of recovery codes, invalidating all previous ones.
    /// </summary>
    public async Task<IReadOnlyList<string>?> RegenerateRecoveryCodesAsync(ApplicationUser user)
    {
        var codes = await _userManager.GenerateNewTwoFactorRecoveryCodesAsync(user, _options.Mfa.RecoveryCodeCount);
        return codes?.ToList().AsReadOnly();
    }
}
