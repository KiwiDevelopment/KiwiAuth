namespace KiwiAuth.Data;

public class RefreshToken
{
    public int Id { get; set; }

    public string UserId { get; set; } = string.Empty;
    public ApplicationUser User { get; set; } = null!;

    // Only the hash is stored — raw token is never persisted.
    public string TokenHash { get; set; } = string.Empty;

    public DateTime ExpiresAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? RevokedAtUtc { get; set; }

    // Set on rotation to link old → new token in the chain.
    public string? ReplacedByTokenHash { get; set; }

    public string? CreatedByIp { get; set; }
    public string? RevokedByIp { get; set; }

    public bool IsExpired => DateTime.UtcNow >= ExpiresAtUtc;
    public bool IsRevoked => RevokedAtUtc.HasValue;
    public bool IsActive => !IsRevoked && !IsExpired;
}
