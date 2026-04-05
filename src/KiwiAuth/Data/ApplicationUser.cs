using Microsoft.AspNetCore.Identity;

namespace KiwiAuth.Data;

public class ApplicationUser : IdentityUser
{
    public string? FullName { get; set; }
    public ICollection<RefreshToken> RefreshTokens { get; set; } = [];
}
