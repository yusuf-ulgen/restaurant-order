using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RestaurantOrder.Domain.Auth;
using RestaurantOrder.Domain.Branches;
using RestaurantOrder.Domain.Tenants;

namespace RestaurantOrder.Infrastructure.Persistence.Configurations;

public class PinCredentialConfiguration : IEntityTypeConfiguration<PinCredential>
{
    public void Configure(EntityTypeBuilder<PinCredential> builder)
    {
        builder.ToTable("pin_credentials", "iam");

        builder.HasKey(p => p.Id);

        builder.Property(p => p.Id)
            .HasColumnName("id")
            .IsRequired();

        builder.Property(p => p.TenantId)
            .HasColumnName("tenant_id")
            .HasConversion(id => id.Value, value => new TenantId(value))
            .IsRequired();

        builder.Property(p => p.UserId)
            .HasColumnName("user_id")
            .HasConversion(id => id.Value, value => new UserId(value))
            .IsRequired();

        builder.Property(p => p.BranchId)
            .HasColumnName("branch_id")
            .HasConversion(id => id.Value, value => new BranchId(value))
            .IsRequired();

        builder.Property(p => p.PinHash)
            .HasColumnName("pin_hash")
            .HasMaxLength(500)
            .IsRequired();

        builder.Property(p => p.AlgorithmVersion)
            .HasColumnName("algorithm_version")
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(p => p.PepperKeyId)
            .HasColumnName("pepper_key_id")
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(p => p.FailedPinAttempts)
            .HasColumnName("failed_pin_attempts")
            .HasDefaultValue(0)
            .IsRequired();

        builder.Property(p => p.LockedUntilUtc)
            .HasColumnName("locked_until_utc")
            .HasColumnType("timestamp with time zone")
            .IsRequired(false);

        builder.Property(p => p.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(p => p.UpdatedAtUtc)
            .HasColumnName("updated_at_utc")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.HasIndex(p => new { p.TenantId, p.UserId, p.BranchId })
            .IsUnique()
            .HasDatabaseName("ix_pin_credentials_tenant_user_branch");

        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(p => p.TenantId)
            .HasConstraintName("fk_pin_credentials_tenants_tenant_id")
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(p => p.UserId)
            .HasConstraintName("fk_pin_credentials_users_user_id")
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<Branch>()
            .WithMany()
            .HasPrincipalKey(br => new { br.TenantId, br.Id })
            .HasForeignKey(p => new { p.TenantId, p.BranchId })
            .HasConstraintName("fk_pin_credentials_branches_tenant_id_branch_id")
            .OnDelete(DeleteBehavior.Restrict);
    }
}
