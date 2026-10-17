using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RestaurantOrder.Domain.Branches;
using RestaurantOrder.Domain.Common;
using RestaurantOrder.Domain.Floor;
using RestaurantOrder.Domain.Orders;
using RestaurantOrder.Domain.Tenants;

namespace RestaurantOrder.Infrastructure.Persistence.Configurations;

public class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        builder.ToTable("orders", "tenancy", t =>
        {
            t.HasCheckConstraint("ck_orders_round_number", "round_number >= 1");
            t.HasCheckConstraint("ck_orders_subtotal", "subtotal_minor_units >= 0");
            t.HasCheckConstraint("ck_orders_tax", "tax_minor_units >= 0");
            t.HasCheckConstraint("ck_orders_service_charge", "service_charge_minor_units >= 0");
            t.HasCheckConstraint("ck_orders_total", "total_minor_units >= 0");
            t.HasCheckConstraint("ck_orders_status", "status IN (1, 2, 3, 4, 5, 6)");
            t.HasCheckConstraint("ck_orders_source", "order_source IN (1, 2)");
        });

        builder.HasKey(o => o.Id);

        builder.Property(o => o.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, value => new OrderId(value))
            .IsRequired();

        builder.Property(o => o.TenantId)
            .HasColumnName("tenant_id")
            .HasConversion(id => id.Value, value => new TenantId(value))
            .IsRequired();

        builder.Property(o => o.BranchId)
            .HasColumnName("branch_id")
            .HasConversion(id => id.Value, value => new BranchId(value))
            .IsRequired();

        builder.Property(o => o.DiningSessionId)
            .HasColumnName("dining_session_id")
            .HasConversion(id => id.Value, value => new DiningSessionId(value))
            .IsRequired();

        builder.Property(o => o.TableId)
            .HasColumnName("table_id")
            .HasConversion(id => id.Value, value => new RestaurantTableId(value))
            .IsRequired();

        builder.Property(o => o.RoundNumber)
            .HasColumnName("round_number")
            .IsRequired();

        builder.Property(o => o.OrderSource)
            .HasColumnName("order_source")
            .HasConversion<int>()
            .IsRequired();

        builder.Property(o => o.Currency)
            .HasColumnName("currency")
            .HasMaxLength(3)
            .HasConversion(c => c.Code, value => new Currency(value))
            .IsRequired();

        builder.Property(o => o.Status)
            .HasColumnName("status")
            .HasConversion<int>()
            .IsRequired();

        builder.Property(o => o.SubtotalMinorUnits)
            .HasColumnName("subtotal_minor_units")
            .IsRequired();

        builder.Property(o => o.TaxMinorUnits)
            .HasColumnName("tax_minor_units")
            .IsRequired();

        builder.Property(o => o.ServiceChargeMinorUnits)
            .HasColumnName("service_charge_minor_units")
            .IsRequired();

        builder.Property(o => o.TotalMinorUnits)
            .HasColumnName("total_minor_units")
            .IsRequired();

        builder.Property(o => o.IdempotencyKey)
            .HasColumnName("idempotency_key")
            .HasMaxLength(Order.MaxIdempotencyKeyLength)
            .IsRequired();

        builder.Property(o => o.RequestFingerprint)
            .HasColumnName("request_fingerprint")
            .HasMaxLength(Order.MaxFingerprintLength)
            .IsRequired();

        builder.Property(o => o.ConcurrencyToken)
            .HasColumnName("concurrency_token")
            .IsConcurrencyToken()
            .IsRequired();

        builder.Property(o => o.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .IsRequired();

        builder.Property(o => o.SubmittedAtUtc)
            .HasColumnName("submitted_at_utc")
            .IsRequired(false);

        builder.Property(o => o.UpdatedAtUtc)
            .HasColumnName("updated_at_utc")
            .IsRequired(false);

        builder.Property(o => o.CancellationReason)
            .HasColumnName("cancellation_reason")
            .HasMaxLength(Order.MaxReasonLength)
            .IsRequired(false);

        builder.Property(o => o.CancelledByActorId)
            .HasColumnName("cancelled_by_actor_id")
            .IsRequired(false);

        // Foreign keys to dining session and table
        builder.HasOne<DiningSession>()
            .WithMany()
            .HasForeignKey(o => new { o.TenantId, o.BranchId, o.DiningSessionId })
            .HasPrincipalKey(ds => new { ds.TenantId, ds.BranchId, ds.Id })
            .HasConstraintName("fk_orders_dining_session")
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<RestaurantTable>()
            .WithMany()
            .HasForeignKey(o => new { o.TenantId, o.BranchId, o.TableId })
            .HasPrincipalKey(rt => new { rt.TenantId, rt.BranchId, rt.Id })
            .HasConstraintName("fk_orders_table")
            .OnDelete(DeleteBehavior.Restrict);

        // OrderItems relationship
        builder.HasMany(o => o.Items)
            .WithOne()
            .HasForeignKey(i => i.OrderId)
            .HasPrincipalKey(o => o.Id)
            .HasConstraintName("fk_order_items_order")
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(o => o.Items)
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        // Unique constraints
        builder.HasIndex(o => new { o.TenantId, o.DiningSessionId, o.RoundNumber })
            .HasDatabaseName("ix_orders_tenant_session_round")
            .IsUnique();

        builder.HasIndex(o => new { o.TenantId, o.IdempotencyKey })
            .HasDatabaseName("ix_orders_tenant_idempotency_key")
            .IsUnique();

        // Operational lookup indexes
        builder.HasIndex(o => new { o.TenantId, o.BranchId, o.Status })
            .HasDatabaseName("ix_orders_tenant_branch_status");

        builder.HasIndex(o => new { o.TenantId, o.DiningSessionId })
            .HasDatabaseName("ix_orders_tenant_dining_session");

        builder.HasIndex(o => new { o.TenantId, o.CreatedAtUtc })
            .HasDatabaseName("ix_orders_tenant_created");
    }
}
