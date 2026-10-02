using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RestaurantOrder.Domain.Auth;

namespace RestaurantOrder.Infrastructure.Persistence.Configurations;

public class PlatformRefreshTokenConfiguration : IEntityTypeConfiguration<PlatformRefreshToken>
{
    public void Configure(EntityTypeBuilder<PlatformRefreshToken> builder)
    {
        builder.ToTable("platform_refresh_tokens", "iam");

        builder.HasKey(rt => rt.Id);

        builder.Property(rt => rt.Id)
            .HasColumnName("id")
            .IsRequired();

        builder.Property(rt => rt.SessionId)
            .HasColumnName("session_id")
            .IsRequired();

        builder.Property(rt => rt.TokenFamilyId)
            .HasColumnName("token_family_id")
            .IsRequired();

        builder.Property(rt => rt.TokenHash)
            .HasColumnName("token_hash")
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(rt => rt.IsRevoked)
            .HasColumnName("is_revoked")
            .HasDefaultValue(false)
            .IsRequired();

        builder.Property(rt => rt.RevokedAtUtc)
            .HasColumnName("revoked_at_utc")
            .HasColumnType("timestamp with time zone")
            .IsRequired(false);

        builder.Property(rt => rt.ReplacedByTokenId)
            .HasColumnName("replaced_by_token_id")
            .IsRequired(false);

        builder.Property(rt => rt.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(rt => rt.ExpiresAtUtc)
            .HasColumnName("expires_at_utc")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.HasIndex(rt => rt.SessionId)
            .HasDatabaseName("ix_platform_refresh_tokens_session_id");

        builder.HasIndex(rt => rt.TokenFamilyId)
            .HasDatabaseName("ix_platform_refresh_tokens_family_id");

        builder.HasIndex(rt => rt.TokenHash)
            .IsUnique()
            .HasDatabaseName("ix_platform_refresh_tokens_hash");

        builder.HasOne<PlatformSession>()
            .WithMany()
            .HasForeignKey(rt => rt.SessionId)
            .HasConstraintName("fk_platform_refresh_tokens_session_id")
            .OnDelete(DeleteBehavior.Cascade);
    }
}
