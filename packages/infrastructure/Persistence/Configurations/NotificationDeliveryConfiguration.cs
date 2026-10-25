using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RestaurantOrder.Domain.Branches;
using RestaurantOrder.Domain.Notifications;
using RestaurantOrder.Domain.Tenants;

namespace RestaurantOrder.Infrastructure.Persistence.Configurations;

public class NotificationDeliveryConfiguration : IEntityTypeConfiguration<NotificationDelivery>
{
    public void Configure(EntityTypeBuilder<NotificationDelivery> builder)
    {
        builder.ToTable("notification_deliveries", "tenancy");

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

        builder.Property(d => d.EventId)
            .HasColumnName("event_id")
            .IsRequired();

        builder.Property(d => d.EventType)
            .HasColumnName("event_type")
            .HasMaxLength(128)
            .IsRequired();

        builder.Property(d => d.DeviceRegistrationId)
            .HasColumnName("device_registration_id")
            .IsRequired();

        builder.Property(d => d.InstallationId)
            .HasColumnName("installation_id")
            .HasMaxLength(256)
            .IsRequired();

        builder.Property(d => d.RecipientType)
            .HasColumnName("recipient_type")
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(d => d.RecipientId)
            .HasColumnName("recipient_id")
            .IsRequired();

        builder.Property(d => d.Status)
            .HasColumnName("status")
            .HasConversion<int>()
            .IsRequired();

        builder.Property(d => d.AttemptCount)
            .HasColumnName("attempt_count")
            .IsRequired();

        builder.Property(d => d.MaxAttempts)
            .HasColumnName("max_attempts")
            .IsRequired();

        builder.Property(d => d.NextAttemptUtc)
            .HasColumnName("next_attempt_utc")
            .HasColumnType("timestamp with time zone")
            .IsRequired(false);

        builder.Property(d => d.LockClaimId)
            .HasColumnName("lock_claim_id")
            .HasMaxLength(100)
            .IsRequired(false);

        builder.Property(d => d.LockExpiresUtc)
            .HasColumnName("lock_expires_utc")
            .HasColumnType("timestamp with time zone")
            .IsRequired(false);

        builder.Property(d => d.ExpiresAtUtc)
            .HasColumnName("expires_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(d => d.CreatedAtUtc)
            .HasColumnName("created_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(d => d.ProcessedAtUtc)
            .HasColumnName("processed_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired(false);

        builder.Property(d => d.LastError)
            .HasColumnName("last_error")
            .HasMaxLength(500)
            .IsRequired(false);

        builder.HasIndex(d => new { d.TenantId, d.Status, d.NextAttemptUtc })
            .HasDatabaseName("ix_notification_deliveries_tenant_id_status_next_attempt");

        builder.HasIndex(d => new { d.TenantId, d.EventId })
            .HasDatabaseName("ix_notification_deliveries_tenant_id_event_id");

        builder.HasIndex(d => new { d.TenantId, d.DeviceRegistrationId })
            .HasDatabaseName("ix_notification_deliveries_tenant_id_device_registration_id");

        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(d => d.TenantId)
            .HasConstraintName("fk_notification_deliveries_tenants_tenant_id")
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Branch>()
            .WithMany()
            .HasPrincipalKey(b => new { b.TenantId, b.Id })
            .HasForeignKey(d => new { d.TenantId, d.BranchId })
            .HasConstraintName("fk_notification_deliveries_branches_tenant_id_branch_id")
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<DeviceRegistration>()
            .WithMany()
            .HasForeignKey(d => d.DeviceRegistrationId)
            .HasConstraintName("fk_notification_deliveries_device_registrations_device_id")
            .OnDelete(DeleteBehavior.Cascade);
    }
}
