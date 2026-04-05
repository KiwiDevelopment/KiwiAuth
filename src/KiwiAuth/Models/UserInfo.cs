namespace KiwiAuth.Models;

public class UserInfo
{
    public string Id { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public string? FullName { get; init; }
    public List<string> Roles { get; init; } = [];
}
