using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RestaurantOrder.Domain.Auth;
using RestaurantOrder.Domain.Branches;
using RestaurantOrder.Domain.Floor;
using RestaurantOrder.Domain.Notifications;
using RestaurantOrder.Domain.Tenants;

namespace RestaurantOrder.Infrastructure.Persistence.Configurations;

public class DeviceRegistrationConfiguration : IEntityTypeConfiguration<DeviceRegistration>
{
    public void Configure(EntityTypeBuilder<DeviceRegistration> builder)
    {
        builder.ToTable("device_registrations", "tenancy");

        builder.HasKey(d => d.Id);

        builder.Property(d => d.Id)
            .HasColumnName("id")
            .IsRequired();

        builder.Property(d => d.TenantId)
            .HasColumnName("tenant_id")
            .HasConversion(id => id.Value, value => new TenantId(value))
            .IsRequired();

        builder.Property(d => d.BranchId)
            .HasColumnName("branch_id")
            .HasConversion(id => id.Value, value => new BranchId(value))
            .IsRequired();

        builder.Property(d => d.UserId)
            .HasColumnName("user_id")
            .HasConversion(
                id => id.HasValue ? id.Value.Value : (Guid?)null,
                value => value.HasValue ? new UserId(value.Value) : null)
            .IsRequired(false);

        builder.Property(d => d.DiningSessionId)
            .HasColumnName("dining_session_id")
            .HasConversion(
                id => id.HasValue ? id.Value.Value : (Guid?)null,
                value => value.HasValue ? new DiningSessionId(value.Value) : null)
            .IsRequired(false);

        builder.Property(d => d.InstallationId)
            .HasColumnName("installation_id")
            .HasMaxLength(256)
            .IsRequired();

        builder.Property(d => d.Platform)
            .HasColumnName("platform")
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(d => d.IsActive)
            .HasColumnName("is_active")
            .IsRequired();

        builder.Property(d => d.CreatedAtUtc)
            .HasColumnName("created_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(d => d.UpdatedAtUtc)
            .HasColumnName("updated_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(d => d.ExpiresAtUtc)
            .HasColumnName("expires_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.HasIndex(d => new { d.TenantId, d.InstallationId })
            .IsUnique()
            .HasDatabaseName("ix_device_registrations_tenant_id_installation_id");

        builder.HasIndex(d => new { d.TenantId, d.BranchId, d.IsActive })
            .HasDatabaseName("ix_device_registrations_tenant_id_branch_id_is_active");

        builder.HasIndex(d => new { d.TenantId, d.UserId, d.IsActive })
            .HasDatabaseName("ix_device_registrations_tenant_id_user_id_is_active");

        builder.HasIndex(d => new { d.TenantId, d.DiningSessionId, d.IsActive })
            .HasDatabaseName("ix_device_registrations_tenant_id_session_id_is_active");

        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(d => d.TenantId)
            .HasConstraintName("fk_device_registrations_tenants_tenant_id")
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Branch>()
            .WithMany()
            .HasPrincipalKey(b => new { b.TenantId, b.Id })
            .HasForeignKey(d => new { d.TenantId, d.BranchId })
            .HasConstraintName("fk_device_registrations_branches_tenant_id_branch_id")
            .OnDelete(DeleteBehavior.Cascade);
    }
}
