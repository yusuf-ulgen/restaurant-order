using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RestaurantOrder.Domain.Branches;
using RestaurantOrder.Domain.Catalog;
using RestaurantOrder.Domain.Tenants;

namespace RestaurantOrder.Infrastructure.Persistence.Configurations;

public class ModifierOptionConfiguration : IEntityTypeConfiguration<ModifierOption>
{
    public void Configure(EntityTypeBuilder<ModifierOption> builder)
    {
        builder.ToTable("modifier_options", "tenancy");

        builder.HasKey(o => o.Id);

        builder.Property(o => o.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, value => new ModifierOptionId(value))
            .IsRequired();

        builder.Property(o => o.TenantId)
            .HasColumnName("tenant_id")
            .HasConversion(id => id.Value, value => new TenantId(value))
            .IsRequired();

        builder.Property(o => o.BranchId)
            .HasColumnName("branch_id")
            .HasConversion(id => id.Value, value => new BranchId(value))
            .IsRequired();

        builder.Property(o => o.ModifierGroupId)
            .HasColumnName("modifier_group_id")
            .HasConversion(id => id.Value, value => new ModifierGroupId(value))
            .IsRequired();

        builder.Property(o => o.Name)
            .HasColumnName("name")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(o => o.PriceDeltaMinorUnits)
            .HasColumnName("price_delta_minor_units")
            .HasConversion(p => p.MinorUnits, value => PriceAmount.FromMinorUnits(value))
            .IsRequired();

        builder.Property(o => o.SortOrder)
            .HasColumnName("sort_order")
            .IsRequired();

        builder.Property(o => o.IsDefault)
            .HasColumnName("is_default")
            .IsRequired();

        builder.Property(o => o.IsActive)
            .HasColumnName("is_active")
            .IsRequired();

        builder.Property(o => o.CreatedAtUtc)
            .HasColumnName("created_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(o => o.UpdatedAtUtc)
            .HasColumnName("updated_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired(false);

        builder.Property(o => o.ConcurrencyToken)
            .HasColumnName("concurrency_token")
            .IsConcurrencyToken()
            .IsRequired();

        // Unique option name per modifier group in tenant
        builder.HasIndex(o => new { o.TenantId, o.ModifierGroupId, o.Name })
            .IsUnique()
            .HasDatabaseName("ix_modifier_options_tenant_id_group_id_name");

        builder.HasIndex(o => new { o.TenantId, o.ModifierGroupId, o.SortOrder })
            .HasDatabaseName("ix_modifier_options_tenant_id_group_id_sort_order");

        builder.HasOne<ModifierGroup>()
            .WithMany(mg => mg.Options)
            .HasForeignKey(o => o.ModifierGroupId)
            .HasConstraintName("fk_modifier_options_modifier_groups_group_id")
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(o => o.TenantId)
            .HasConstraintName("fk_modifier_options_tenants_tenant_id")
            .OnDelete(DeleteBehavior.Restrict);
    }
}
