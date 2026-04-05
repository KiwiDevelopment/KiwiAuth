namespace KiwiAuth.Models.Requests;

public class MfaVerifyRequest
{
    /// <summary>The MFA session token returned by POST /auth/login when MFA is required.</summary>
    public string MfaSessionToken { get; init; } = string.Empty;

    /// <summary>6-digit TOTP code from the authenticator app, or an 8-character recovery code.</summary>
    public string Code { get; init; } = string.Empty;

    /// <summary>Set to true to treat Code as a recovery code instead of a TOTP code.</summary>
    public bool IsRecoveryCode { get; init; } = false;
}
