namespace KiwiAuth.Models;

/// <summary>
/// Returned by LoginAsync. Either carries full auth tokens (no MFA)
/// or a short-lived MFA session token to complete the second factor.
/// </summary>
public class LoginResult
{
    public bool RequiresMfa { get; init; }

    // Populated when RequiresMfa = true.
    // Short-lived JWT the client must send to POST /auth/mfa/verify.
    public string? MfaSessionToken { get; init; }

    // Populated when RequiresMfa = false.
    public string? AccessToken { get; init; }
    public string? RefreshToken { get; init; }
    public UserInfo? User { get; init; }
}
