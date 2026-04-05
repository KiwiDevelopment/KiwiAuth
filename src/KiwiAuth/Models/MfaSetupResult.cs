namespace KiwiAuth.Models;

public class MfaSetupResult
{
    /// <summary>Base32-encoded TOTP secret. Feed to an authenticator app.</summary>
    public string Secret { get; init; } = string.Empty;

    /// <summary>
    /// otpauth:// URI for QR code generation.
    /// Pass this to a QR library on the frontend (e.g. qrcode.js, react-qr-code).
    /// </summary>
    public string AuthenticatorUri { get; init; } = string.Empty;
}
