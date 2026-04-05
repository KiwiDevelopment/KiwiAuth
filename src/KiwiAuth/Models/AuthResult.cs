namespace KiwiAuth.Models;

public class AuthResult
{
    public string AccessToken { get; init; } = string.Empty;
    public string RefreshToken { get; init; } = string.Empty;
    public UserInfo User { get; init; } = null!;
}
