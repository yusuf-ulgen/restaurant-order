using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RestaurantOrder.Domain.Auth;
using RestaurantOrder.Domain.Tenants;

namespace RestaurantOrder.Infrastructure.Persistence.Configurations;

public class IdentityNotificationOutboxMessageConfiguration : IEntityTypeConfiguration<IdentityNotificationOutboxMessage>
{
    public void Configure(EntityTypeBuilder<IdentityNotificationOutboxMessage> builder)
    {
        builder.ToTable("identity_notifications_outbox", "iam");

        builder.HasKey(o => o.Id);

        builder.Property(o => o.Id)
            .HasColumnName("id")
            .IsRequired();

        builder.Property(o => o.TenantId)
            .HasColumnName("tenant_id")
            .HasConversion(id => id.Value, value => new TenantId(value))
            .IsRequired();

        builder.Property(o => o.NotificationType)
            .HasColumnName("notification_type")
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(o => o.RecipientEmail)
            .HasColumnName("recipient_email")
            .HasMaxLength(255)
            .IsRequired();

        builder.Property(o => o.EncryptedPayload)
            .HasColumnName("encrypted_payload")
            .IsRequired();

        builder.Property(o => o.IdempotencyKey)
            .HasColumnName("idempotency_key")
            .HasMaxLength(128)
            .IsRequired();

        builder.Property(o => o.Status)
            .HasColumnName("status")
            .HasConversion<int>()
            .HasDefaultValue(IdentityNotificationStatus.Pending)
            .IsRequired();

        builder.Property(o => o.AttemptCount)
            .HasColumnName("attempt_count")
            .HasDefaultValue(0)
            .IsRequired();

        builder.Property(o => o.MaxAttempts)
            .HasColumnName("max_attempts")
            .HasDefaultValue(5)
            .IsRequired();

        builder.Property(o => o.NextAttemptUtc)
            .HasColumnName("next_attempt_utc")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(o => o.LastError)
            .HasColumnName("last_error")
            .IsRequired(false);

        builder.Property(o => o.ClaimToken)
            .HasColumnName("claim_token")
            .IsRequired(false);

        builder.Property(o => o.ClaimedAtUtc)
            .HasColumnName("claimed_at_utc")
            .HasColumnType("timestamp with time zone")
            .IsRequired(false);

        builder.Property(o => o.LockedUntilUtc)
            .HasColumnName("locked_until_utc")
            .HasColumnType("timestamp with time zone")
            .IsRequired(false);

        builder.Property(o => o.DeliveredAtUtc)
            .HasColumnName("delivered_at_utc")
            .HasColumnType("timestamp with time zone")
            .IsRequired(false);

        builder.Property(o => o.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(o => o.UpdatedAtUtc)
            .HasColumnName("updated_at_utc")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.HasIndex(o => o.TenantId)
            .HasDatabaseName("ix_identity_notifications_outbox_tenant_id");

        builder.HasIndex(o => o.IdempotencyKey)
            .IsUnique()
            .HasDatabaseName("uq_identity_notifications_outbox_idempotency_key");

        builder.HasIndex(o => new { o.Status, o.NextAttemptUtc })
            .HasDatabaseName("ix_identity_notifications_outbox_status_next_attempt");

        builder.HasIndex(o => new { o.Status, o.LockedUntilUtc })
            .HasDatabaseName("ix_identity_notifications_outbox_status_locked_until");

        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(o => o.TenantId)
            .HasConstraintName("fk_identity_notifications_outbox_tenants_tenant_id")
            .OnDelete(DeleteBehavior.Cascade);
    }
}
