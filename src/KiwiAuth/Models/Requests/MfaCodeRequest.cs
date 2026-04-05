namespace KiwiAuth.Models.Requests;

/// <summary>Used for /auth/mfa/enable and /auth/mfa/disable.</summary>
public class MfaCodeRequest
{
    public string Code { get; init; } = string.Empty;
}
