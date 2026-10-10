using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RestaurantOrder.Domain.Branches;
using RestaurantOrder.Domain.Floor;
using RestaurantOrder.Domain.Tenants;

namespace RestaurantOrder.Infrastructure.Persistence.Configurations;

public class DiningSessionMovementConfiguration : IEntityTypeConfiguration<DiningSessionMovement>
{
    public void Configure(EntityTypeBuilder<DiningSessionMovement> builder)
    {
        builder.ToTable("dining_session_movements", "tenancy", t =>
        {
            t.HasCheckConstraint(
                "ck_dining_session_movements_type",
                "movement_type IN (1, 2)");

            t.HasCheckConstraint(
                "ck_dining_session_movements_guests",
                "guests_moved >= 1");

            t.HasCheckConstraint(
                "ck_dining_session_movements_tables_diff",
                "(movement_type = 1 AND source_table_id <> target_table_id) OR (movement_type = 2)");
        });

        builder.HasKey(m => m.Id);

        builder.Property(m => m.Id)
            .HasColumnName("id")
            .IsRequired();

        builder.Property(m => m.TenantId)
            .HasColumnName("tenant_id")
            .HasConversion(id => id.Value, value => new TenantId(value))
            .IsRequired();

        builder.Property(m => m.BranchId)
            .HasColumnName("branch_id")
            .HasConversion(id => id.Value, value => new BranchId(value))
            .IsRequired();

        builder.Property(m => m.SourceSessionId)
            .HasColumnName("source_session_id")
            .HasConversion(id => id.Value, value => new DiningSessionId(value))
            .IsRequired();

        builder.Property(m => m.SourceTableId)
            .HasColumnName("source_table_id")
            .HasConversion(id => id.Value, value => new RestaurantTableId(value))
            .IsRequired();

        builder.Property(m => m.TargetSessionId)
            .HasColumnName("target_session_id")
            .HasConversion(
                id => id.HasValue ? id.Value.Value : (Guid?)null,
                value => value.HasValue ? new DiningSessionId(value.Value) : null)
            .IsRequired(false);

        builder.Property(m => m.TargetTableId)
            .HasColumnName("target_table_id")
            .HasConversion(id => id.Value, value => new RestaurantTableId(value))
            .IsRequired();

        builder.Property(m => m.MovementType)
            .HasColumnName("movement_type")
            .HasConversion<int>()
            .IsRequired();

        builder.Property(m => m.ActorId)
            .HasColumnName("actor_id")
            .IsRequired();

        builder.Property(m => m.ActorRole)
            .HasColumnName("actor_role")
            .HasMaxLength(DiningSessionMovement.MaxActorRoleLength)
            .IsRequired();

        builder.Property(m => m.Reason)
            .HasColumnName("reason")
            .HasMaxLength(DiningSessionMovement.MaxReasonLength)
            .IsRequired(false);

        builder.Property(m => m.IdempotencyKey)
            .HasColumnName("idempotency_key")
            .HasMaxLength(DiningSessionMovement.MaxIdempotencyKeyLength)
            .IsRequired();

        builder.Property(m => m.GuestsMoved)
            .HasColumnName("guests_moved")
            .IsRequired();

        builder.Property(m => m.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .IsRequired();

        // Foreign keys to tables
        builder.HasOne<RestaurantTable>()
            .WithMany()
            .HasForeignKey(m => new { m.TenantId, m.BranchId, m.SourceTableId })
            .HasPrincipalKey(rt => new { rt.TenantId, rt.BranchId, rt.Id })
            .HasConstraintName("fk_dining_session_movements_source_table")
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<RestaurantTable>()
            .WithMany()
            .HasForeignKey(m => new { m.TenantId, m.BranchId, m.TargetTableId })
            .HasPrincipalKey(rt => new { rt.TenantId, rt.BranchId, rt.Id })
            .HasConstraintName("fk_dining_session_movements_target_table")
            .OnDelete(DeleteBehavior.Restrict);

        // Unique index per tenant + branch + idempotency key
        builder.HasIndex(m => new { m.TenantId, m.BranchId, m.IdempotencyKey })
            .HasDatabaseName("ix_dining_session_movements_tenant_branch_idempotency_key")
            .IsUnique();

        // Operational lookup indexes
        builder.HasIndex(m => new { m.TenantId, m.BranchId, m.SourceSessionId })
            .HasDatabaseName("ix_dining_session_movements_tenant_branch_source_session");

        builder.HasIndex(m => new { m.TenantId, m.BranchId, m.CreatedAtUtc })
            .HasDatabaseName("ix_dining_session_movements_tenant_branch_created");
    }
}
