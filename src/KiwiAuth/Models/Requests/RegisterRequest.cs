namespace KiwiAuth.Models.Requests;

public class RegisterRequest
{
    public string Email { get; init; } = string.Empty;
    public string Password { get; init; } = string.Empty;
    public string? FullName { get; init; }
}
