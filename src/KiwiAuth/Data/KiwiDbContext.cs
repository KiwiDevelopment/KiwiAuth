using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace KiwiAuth.Data;

public class KiwiDbContext : IdentityDbContext<ApplicationUser>
{
    public KiwiDbContext(DbContextOptions<KiwiDbContext> options) : base(options) { }

    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<RefreshToken>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.HasIndex(e => e.TokenHash).IsUnique();
            entity.HasIndex(e => e.UserId);

            entity.Property(e => e.TokenHash)
                  .HasMaxLength(128)
                  .IsRequired();

            entity.HasOne(e => e.User)
                  .WithMany(u => u.RefreshTokens)
                  .HasForeignKey(e => e.UserId)
                  .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
