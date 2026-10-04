using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RestaurantOrder.Domain.Auth;
using RestaurantOrder.Domain.Branches;
using RestaurantOrder.Domain.Catalog;
using RestaurantOrder.Domain.Tenants;

namespace RestaurantOrder.Infrastructure.Persistence.Configurations;

public class BranchItemAvailabilityConfiguration : IEntityTypeConfiguration<BranchItemAvailability>
{
    public void Configure(EntityTypeBuilder<BranchItemAvailability> builder)
    {
        builder.ToTable("branch_item_availabilities", "tenancy");

        builder.HasKey(a => a.Id);

        builder.Property(a => a.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, value => new BranchItemAvailabilityId(value))
            .IsRequired();

        builder.Property(a => a.TenantId)
            .HasColumnName("tenant_id")
            .HasConversion(id => id.Value, value => new TenantId(value))
            .IsRequired();

        builder.Property(a => a.BranchId)
            .HasColumnName("branch_id")
            .HasConversion(id => id.Value, value => new BranchId(value))
            .IsRequired();

        builder.Property(a => a.MenuItemId)
            .HasColumnName("menu_item_id")
            .HasConversion(id => id.Value, value => new MenuItemId(value))
            .IsRequired();

        builder.Property(a => a.ItemVariantId)
            .HasColumnName("item_variant_id")
            .HasConversion(id => id.HasValue ? id.Value.Value : (Guid?)null, value => value.HasValue ? new ItemVariantId(value.Value) : null)
            .IsRequired(false);

        builder.Property(a => a.IsAvailable)
            .HasColumnName("is_available")
            .IsRequired();

        builder.Property(a => a.ReasonCode)
            .HasColumnName("reason_code")
            .HasConversion(r => (int)r, value => (AvailabilityReasonCode)value)
            .IsRequired();

        builder.Property(a => a.Note)
            .HasColumnName("note")
            .HasMaxLength(500)
            .IsRequired(false);

        builder.Property(a => a.ExpectedAvailableAtUtc)
            .HasColumnName("expected_available_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired(false);

        builder.Property(a => a.ChangedByUserId)
            .HasColumnName("changed_by_user_id")
            .HasConversion(id => id.Value, value => new UserId(value))
            .IsRequired();

        builder.Property(a => a.ChangedAtUtc)
            .HasColumnName("changed_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(a => a.ConcurrencyToken)
            .HasColumnName("concurrency_token")
            .IsConcurrencyToken()
            .IsRequired();

        // Partial unique index for item-level availability
        builder.HasIndex(a => new { a.TenantId, a.BranchId, a.MenuItemId })
            .HasFilter("item_variant_id IS NULL")
            .IsUnique()
            .HasDatabaseName("ix_branch_item_availabilities_item_unique");

        // Partial unique index for variant-level availability
        builder.HasIndex(a => new { a.TenantId, a.BranchId, a.MenuItemId, a.ItemVariantId })
            .HasFilter("item_variant_id IS NOT NULL")
            .IsUnique()
            .HasDatabaseName("ix_branch_item_availabilities_variant_unique");

        // Composite FK to branches
        builder.HasOne<Branch>()
            .WithMany()
            .HasPrincipalKey(b => new { b.TenantId, b.Id })
            .HasForeignKey(a => new { a.TenantId, a.BranchId })
            .HasConstraintName("fk_branch_item_availabilities_branches_tenant_id_branch_id")
            .OnDelete(DeleteBehavior.Cascade);

        // FK to menu_items
        builder.HasOne<MenuItem>()
            .WithMany()
            .HasForeignKey(a => a.MenuItemId)
            .HasConstraintName("fk_branch_item_availabilities_menu_items_item_id")
            .OnDelete(DeleteBehavior.Cascade);

        // FK to item_variants
        builder.HasOne<ItemVariant>()
            .WithMany()
            .HasForeignKey(a => a.ItemVariantId)
            .HasConstraintName("fk_branch_item_availabilities_item_variants_variant_id")
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
