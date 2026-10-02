using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RestaurantOrder.Domain.Auth;

namespace RestaurantOrder.Infrastructure.Persistence.Configurations;

public class PlatformSessionConfiguration : IEntityTypeConfiguration<PlatformSession>
{
    public void Configure(EntityTypeBuilder<PlatformSession> builder)
    {
        builder.ToTable("platform_sessions", "iam");

        builder.HasKey(s => s.Id);

        builder.Property(s => s.Id)
            .HasColumnName("id")
            .IsRequired();

        builder.Property(s => s.UserId)
            .HasColumnName("user_id")
            .HasConversion(id => id.Value, value => new UserId(value))
            .IsRequired();

        builder.Property(s => s.FamilyId)
            .HasColumnName("family_id")
            .IsRequired();

        builder.Property(s => s.CurrentTokenHash)
            .HasColumnName("current_token_hash")
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(s => s.IpAddress)
            .HasColumnName("ip_address")
            .HasMaxLength(45)
            .IsRequired(false);

        builder.Property(s => s.UserAgent)
            .HasColumnName("user_agent")
            .HasMaxLength(500)
            .IsRequired(false);

        builder.Property(s => s.IsRevoked)
            .HasColumnName("is_revoked")
            .HasDefaultValue(false)
            .IsRequired();

        builder.Property(s => s.RevokedAtUtc)
            .HasColumnName("revoked_at_utc")
            .HasColumnType("timestamp with time zone")
            .IsRequired(false);

        builder.Property(s => s.RevocationReason)
            .HasColumnName("revocation_reason")
            .HasMaxLength(100)
            .IsRequired(false);

        builder.Property(s => s.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(s => s.LastSeenAtUtc)
            .HasColumnName("last_seen_at_utc")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(s => s.ExpiresAtUtc)
            .HasColumnName("expires_at_utc")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(s => s.ConcurrencyToken)
            .HasColumnName("concurrency_token")
            .HasMaxLength(36)
            .IsConcurrencyToken()
            .IsRequired();

        builder.HasIndex(s => s.UserId)
            .HasDatabaseName("ix_platform_sessions_user_id");

        builder.HasIndex(s => s.ExpiresAtUtc)
            .HasDatabaseName("ix_platform_sessions_expires_at_utc");

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(s => s.UserId)
            .HasConstraintName("fk_platform_sessions_users_user_id")
            .OnDelete(DeleteBehavior.Cascade);
    }
}
