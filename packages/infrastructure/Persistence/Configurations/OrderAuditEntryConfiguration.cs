using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RestaurantOrder.Domain.Floor;
using RestaurantOrder.Domain.Orders;
using RestaurantOrder.Domain.Tenants;

namespace RestaurantOrder.Infrastructure.Persistence.Configurations;

public class OrderAuditEntryConfiguration : IEntityTypeConfiguration<OrderAuditEntry>
{
    public void Configure(EntityTypeBuilder<OrderAuditEntry> builder)
    {
        builder.ToTable("order_audit_entries", "tenancy", t =>
        {
            t.HasCheckConstraint("ck_order_audit_entries_action", "length(action) >= 1");
        });

        builder.HasKey(a => a.Id);

        builder.Property(a => a.Id)
            .HasColumnName("id")
            .IsRequired();

        builder.Property(a => a.TenantId)
            .HasColumnName("tenant_id")
            .HasConversion(id => id.Value, value => new TenantId(value))
            .IsRequired();

        builder.Property(a => a.OrderId)
            .HasColumnName("order_id")
            .HasConversion(id => id.Value, value => new OrderId(value))
            .IsRequired();

        builder.Property(a => a.DiningSessionId)
            .HasColumnName("dining_session_id")
            .HasConversion(id => id.Value, value => new DiningSessionId(value))
            .IsRequired();

        builder.Property(a => a.ActorId)
            .HasColumnName("actor_id")
            .IsRequired();

        builder.Property(a => a.ActorRole)
            .HasColumnName("actor_role")
            .HasMaxLength(OrderAuditEntry.MaxActorRoleLength)
            .IsRequired();

        builder.Property(a => a.Action)
            .HasColumnName("action")
            .HasMaxLength(OrderAuditEntry.MaxActionLength)
            .IsRequired();

        builder.Property(a => a.PreviousStatus)
            .HasColumnName("previous_status")
            .HasConversion<int>()
            .IsRequired(false);

        builder.Property(a => a.NewStatus)
            .HasColumnName("new_status")
            .HasConversion<int>()
            .IsRequired(false);

        builder.Property(a => a.Reason)
            .HasColumnName("reason")
            .HasMaxLength(OrderAuditEntry.MaxReasonLength)
            .IsRequired(false);

        builder.Property(a => a.DetailsJson)
            .HasColumnName("details_json")
            .HasMaxLength(OrderAuditEntry.MaxDetailsLength)
            .IsRequired(false);

        builder.Property(a => a.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .IsRequired();

        // Foreign keys to order
        builder.HasOne<Order>()
            .WithMany()
            .HasForeignKey(a => a.OrderId)
            .HasPrincipalKey(o => o.Id)
            .HasConstraintName("fk_order_audit_entries_order")
            .OnDelete(DeleteBehavior.Restrict);

        // Operational lookup indexes
        builder.HasIndex(a => new { a.TenantId, a.OrderId })
            .HasDatabaseName("ix_order_audit_entries_tenant_order");

        builder.HasIndex(a => new { a.TenantId, a.DiningSessionId })
            .HasDatabaseName("ix_order_audit_entries_tenant_session");

        builder.HasIndex(a => new { a.TenantId, a.CreatedAtUtc })
            .HasDatabaseName("ix_order_audit_entries_tenant_created");
    }
}
