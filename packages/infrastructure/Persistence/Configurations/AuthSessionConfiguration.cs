using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RestaurantOrder.Domain.Auth;
using RestaurantOrder.Domain.Branches;
using RestaurantOrder.Domain.Tenants;

namespace RestaurantOrder.Infrastructure.Persistence.Configurations;

public class AuthSessionConfiguration : IEntityTypeConfiguration<AuthSession>
{
    public void Configure(EntityTypeBuilder<AuthSession> builder)
    {
        builder.ToTable("sessions", "iam");

        builder.HasKey(s => s.Id);

        builder.Property(s => s.Id)
            .HasColumnName("id")
            .IsRequired();

        builder.Property(s => s.TenantId)
            .HasColumnName("tenant_id")
            .HasConversion(id => id.Value, value => new TenantId(value))
            .IsRequired();

        builder.Property(s => s.UserId)
            .HasColumnName("user_id")
            .HasConversion(id => id.Value, value => new UserId(value))
            .IsRequired();

        builder.Property(s => s.MembershipId)
            .HasColumnName("membership_id")
            .IsRequired();

        builder.Property(s => s.BranchId)
            .HasColumnName("branch_id")
            .HasConversion(
                id => id.HasValue ? id.Value.Value : (Guid?)null,
                value => value.HasValue ? new BranchId(value.Value) : null)
            .IsRequired(false);

        builder.Property(s => s.AuthMethod)
            .HasColumnName("auth_method")
            .HasConversion<int>()
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

        builder.HasIndex(s => s.TenantId)
            .HasDatabaseName("ix_sessions_tenant_id");

        builder.HasIndex(s => s.UserId)
            .HasDatabaseName("ix_sessions_user_id");

        builder.HasIndex(s => s.MembershipId)
            .HasDatabaseName("ix_sessions_membership_id");

        builder.HasIndex(s => s.ExpiresAtUtc)
            .HasDatabaseName("ix_sessions_expires_at_utc");

        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(s => s.TenantId)
            .HasConstraintName("fk_sessions_tenants_tenant_id")
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(s => s.UserId)
            .HasConstraintName("fk_sessions_users_user_id")
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<UserMembership>()
            .WithMany()
            .HasForeignKey(s => s.MembershipId)
            .HasConstraintName("fk_sessions_memberships_membership_id")
            .OnDelete(DeleteBehavior.Cascade);
    }
}
