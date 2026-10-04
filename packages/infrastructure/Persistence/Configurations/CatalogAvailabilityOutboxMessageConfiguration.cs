using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RestaurantOrder.Domain.Branches;
using RestaurantOrder.Domain.Catalog;
using RestaurantOrder.Domain.Tenants;

namespace RestaurantOrder.Infrastructure.Persistence.Configurations;

public class CatalogAvailabilityOutboxMessageConfiguration : IEntityTypeConfiguration<CatalogAvailabilityOutboxMessage>
{
    public void Configure(EntityTypeBuilder<CatalogAvailabilityOutboxMessage> builder)
    {
        builder.ToTable("catalog_availability_outbox", "tenancy");

        builder.HasKey(o => o.Id);

        builder.Property(o => o.Id)
            .HasColumnName("id")
            .IsRequired();

        builder.Property(o => o.TenantId)
            .HasColumnName("tenant_id")
            .HasConversion(id => id.Value, value => new TenantId(value))
            .IsRequired();

        builder.Property(o => o.BranchId)
            .HasColumnName("branch_id")
            .HasConversion(id => id.Value, value => new BranchId(value))
            .IsRequired();

        builder.Property(o => o.AggregateId)
            .HasColumnName("aggregate_id")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(o => o.EventType)
            .HasColumnName("event_type")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(o => o.Payload)
            .HasColumnName("payload")
            .HasColumnType("jsonb")
            .IsRequired();

        builder.Property(o => o.SchemaVersion)
            .HasColumnName("schema_version")
            .IsRequired();

        builder.Property(o => o.Status)
            .HasColumnName("status")
            .HasConversion<int>()
            .IsRequired();

        builder.Property(o => o.IdempotencyKey)
            .HasColumnName("idempotency_key")
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(o => o.AttemptCount)
            .HasColumnName("attempt_count")
            .IsRequired();

        builder.Property(o => o.MaxAttempts)
            .HasColumnName("max_attempts")
            .IsRequired();

        builder.Property(o => o.NextAttemptUtc)
            .HasColumnName("next_attempt_utc")
            .HasColumnType("timestamp with time zone")
            .IsRequired(false);

        builder.Property(o => o.LastError)
            .HasColumnName("last_error")
            .HasMaxLength(500)
            .IsRequired(false);

        builder.Property(o => o.OccurredAtUtc)
            .HasColumnName("occurred_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(o => o.CreatedAtUtc)
            .HasColumnName("created_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(o => o.DispatchedAtUtc)
            .HasColumnName("dispatched_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired(false);

        builder.HasIndex(o => new { o.TenantId, o.IdempotencyKey })
            .IsUnique()
            .HasDatabaseName("ix_catalog_availability_outbox_tenant_id_idempotency_key");

        builder.HasIndex(o => new { o.TenantId, o.BranchId })
            .HasDatabaseName("ix_catalog_availability_outbox_tenant_id_branch_id");

        builder.HasIndex(o => new { o.Status, o.NextAttemptUtc })
            .HasDatabaseName("ix_catalog_availability_outbox_status_next_attempt");

        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(o => o.TenantId)
            .HasConstraintName("fk_catalog_availability_outbox_tenants_tenant_id")
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Branch>()
            .WithMany()
            .HasPrincipalKey(b => new { b.TenantId, b.Id })
            .HasForeignKey(o => new { o.TenantId, o.BranchId })
            .HasConstraintName("fk_catalog_availability_outbox_branches_tenant_id_branch_id")
            .OnDelete(DeleteBehavior.Cascade);
    }
}
