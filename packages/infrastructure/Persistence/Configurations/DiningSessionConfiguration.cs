using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RestaurantOrder.Domain.Branches;
using RestaurantOrder.Domain.Floor;
using RestaurantOrder.Domain.Tenants;

namespace RestaurantOrder.Infrastructure.Persistence.Configurations;

public class DiningSessionConfiguration : IEntityTypeConfiguration<DiningSession>
{
    public void Configure(EntityTypeBuilder<DiningSession> builder)
    {
        builder.ToTable("dining_sessions", "tenancy", t =>
        {
            t.HasCheckConstraint(
                "ck_dining_sessions_status",
                "status IN (1, 2, 3, 4)");

            t.HasCheckConstraint(
                "ck_dining_sessions_guest_count",
                "guest_count >= 1");

            t.HasCheckConstraint(
                "ck_dining_sessions_status_timestamps",
                "((status = 1 AND activated_at_utc IS NULL AND bill_requested_at_utc IS NULL AND closed_at_utc IS NULL) " +
                "OR (status = 2 AND activated_at_utc IS NOT NULL AND closed_at_utc IS NULL) " +
                "OR (status = 3 AND activated_at_utc IS NOT NULL AND bill_requested_at_utc IS NOT NULL AND closed_at_utc IS NULL) " +
                "OR (status = 4 AND closed_at_utc IS NOT NULL))");
        });

        builder.HasKey(ds => ds.Id);
        builder.HasAlternateKey(ds => new { ds.TenantId, ds.BranchId, ds.Id });

        builder.Property(ds => ds.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, value => new DiningSessionId(value))
            .IsRequired();

        builder.Property(ds => ds.TenantId)
            .HasColumnName("tenant_id")
            .HasConversion(id => id.Value, value => new TenantId(value))
            .IsRequired();

        builder.Property(ds => ds.BranchId)
            .HasColumnName("branch_id")
            .HasConversion(id => id.Value, value => new BranchId(value))
            .IsRequired();

        builder.Property(ds => ds.TableId)
            .HasColumnName("table_id")
            .HasConversion(id => id.Value, value => new RestaurantTableId(value))
            .IsRequired();

        builder.Property(ds => ds.Status)
            .HasColumnName("status")
            .HasConversion<int>()
            .IsRequired();

        builder.Property(ds => ds.GuestCount)
            .HasColumnName("guest_count")
            .IsRequired();

        builder.Property(ds => ds.AssignedWaiterId)
            .HasColumnName("assigned_waiter_id")
            .IsRequired(false);

        builder.Property(ds => ds.OpenedAtUtc)
            .HasColumnName("opened_at_utc")
            .IsRequired();

        builder.Property(ds => ds.ActivatedAtUtc)
            .HasColumnName("activated_at_utc")
            .IsRequired(false);

        builder.Property(ds => ds.BillRequestedAtUtc)
            .HasColumnName("bill_requested_at_utc")
            .IsRequired(false);

        builder.Property(ds => ds.ClosedAtUtc)
            .HasColumnName("closed_at_utc")
            .IsRequired(false);

        builder.Property(ds => ds.CloseReason)
            .HasColumnName("close_reason")
            .HasMaxLength(DiningSession.MaxCloseReasonLength)
            .IsRequired(false);

        builder.Property(ds => ds.MergedIntoSessionId)
            .HasColumnName("merged_into_session_id")
            .HasConversion(
                id => id.HasValue ? id.Value.Value : (Guid?)null,
                value => value.HasValue ? new DiningSessionId(value.Value) : null)
            .IsRequired(false);

        builder.Property(ds => ds.ConcurrencyToken)
            .HasColumnName("concurrency_token")
            .IsConcurrencyToken()
            .IsRequired();

        builder.Property(ds => ds.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .IsRequired();

        builder.Property(ds => ds.UpdatedAtUtc)
            .HasColumnName("updated_at_utc")
            .IsRequired(false);

        // Composite FK to RestaurantTable alternate key (TenantId, BranchId, Id)
        builder.HasOne<RestaurantTable>()
            .WithMany()
            .HasForeignKey(ds => new { ds.TenantId, ds.BranchId, ds.TableId })
            .HasPrincipalKey(rt => new { rt.TenantId, rt.BranchId, rt.Id })
            .OnDelete(DeleteBehavior.Restrict);

        // Self-referencing optional composite FK for merged session
        builder.HasOne<DiningSession>()
            .WithMany()
            .HasForeignKey(ds => new { ds.TenantId, ds.BranchId, ds.MergedIntoSessionId })
            .HasPrincipalKey(ds => new { ds.TenantId, ds.BranchId, ds.Id })
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired(false);

        // Partial unique index enforcing only one active/non-closed session per table (status <> 4)
        builder.HasIndex(ds => new { ds.TenantId, ds.BranchId, ds.TableId })
            .HasDatabaseName("ix_dining_sessions_tenant_branch_table_active")
            .HasFilter("status <> 4")
            .IsUnique();

        // Operational lookup indexes
        builder.HasIndex(ds => new { ds.TenantId, ds.BranchId, ds.Status })
            .HasDatabaseName("ix_dining_sessions_tenant_branch_status");

        builder.HasIndex(ds => new { ds.TenantId, ds.BranchId, ds.TableId, ds.CreatedAtUtc })
            .HasDatabaseName("ix_dining_sessions_tenant_branch_table_created");
    }
}
