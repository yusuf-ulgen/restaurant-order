using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RestaurantOrder.Domain.Auth;
using RestaurantOrder.Domain.Tenants;

namespace RestaurantOrder.Infrastructure.Persistence.Configurations;

public class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.ToTable("refresh_tokens", "iam");

        builder.HasKey(rt => rt.Id);

        builder.Property(rt => rt.Id)
            .HasColumnName("id")
            .IsRequired();

        builder.Property(rt => rt.TenantId)
            .HasColumnName("tenant_id")
            .HasConversion(id => id.Value, value => new TenantId(value))
            .IsRequired();

        builder.Property(rt => rt.SessionId)
            .HasColumnName("session_id")
            .IsRequired();

        builder.Property(rt => rt.TokenFamilyId)
            .HasColumnName("token_family_id")
            .IsRequired();

        builder.Property(rt => rt.TokenHash)
            .HasColumnName("token_hash")
            .HasMaxLength(128)
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

        builder.HasIndex(rt => rt.TenantId)
            .HasDatabaseName("ix_refresh_tokens_tenant_id");

        builder.HasIndex(rt => rt.SessionId)
            .HasDatabaseName("ix_refresh_tokens_session_id");

        builder.HasIndex(rt => rt.TokenFamilyId)
            .HasDatabaseName("ix_refresh_tokens_token_family_id");

        builder.HasIndex(rt => rt.TokenHash)
            .IsUnique()
            .HasDatabaseName("ix_refresh_tokens_token_hash");

        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(rt => rt.TenantId)
            .HasConstraintName("fk_refresh_tokens_tenants_tenant_id")
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<AuthSession>()
            .WithMany()
            .HasForeignKey(rt => rt.SessionId)
            .HasConstraintName("fk_refresh_tokens_sessions_session_id")
            .OnDelete(DeleteBehavior.Cascade);
    }
}
