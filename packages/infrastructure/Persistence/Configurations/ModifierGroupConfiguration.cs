using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RestaurantOrder.Domain.Branches;
using RestaurantOrder.Domain.Catalog;
using RestaurantOrder.Domain.Tenants;

namespace RestaurantOrder.Infrastructure.Persistence.Configurations;

public class ModifierGroupConfiguration : IEntityTypeConfiguration<ModifierGroup>
{
    public void Configure(EntityTypeBuilder<ModifierGroup> builder)
    {
        builder.ToTable("modifier_groups", "tenancy");

        builder.HasKey(m => m.Id);
        builder.HasAlternateKey(m => new { m.TenantId, m.BranchId, m.Id });

        builder.Property(m => m.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, value => new ModifierGroupId(value))
            .IsRequired();

        builder.Property(m => m.TenantId)
            .HasColumnName("tenant_id")
            .HasConversion(id => id.Value, value => new TenantId(value))
            .IsRequired();

        builder.Property(m => m.BranchId)
            .HasColumnName("branch_id")
            .HasConversion(id => id.Value, value => new BranchId(value))
            .IsRequired();

        builder.Property(m => m.Name)
            .HasColumnName("name")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(m => m.MinSelections)
            .HasColumnName("min_selections")
            .IsRequired();

        builder.Property(m => m.MaxSelections)
            .HasColumnName("max_selections")
            .IsRequired();

        builder.Property(m => m.SortOrder)
            .HasColumnName("sort_order")
            .IsRequired();

        builder.Property(m => m.IsActive)
            .HasColumnName("is_active")
            .IsRequired();

        builder.Property(m => m.CreatedAtUtc)
            .HasColumnName("created_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(m => m.UpdatedAtUtc)
            .HasColumnName("updated_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired(false);

        builder.Property(m => m.ConcurrencyToken)
            .HasColumnName("concurrency_token")
            .IsConcurrencyToken()
            .IsRequired();

        builder.HasIndex(m => new { m.TenantId, m.BranchId, m.SortOrder })
            .HasDatabaseName("ix_modifier_groups_tenant_id_branch_id_sort_order");

        builder.HasOne<Branch>()
            .WithMany()
            .HasPrincipalKey(b => new { b.TenantId, b.Id })
            .HasForeignKey(m => new { m.TenantId, m.BranchId })
            .HasConstraintName("fk_modifier_groups_branches_tenant_id_branch_id")
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(m => m.TenantId)
            .HasConstraintName("fk_modifier_groups_tenants_tenant_id")
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(m => m.Options)
            .WithOne()
            .HasForeignKey(o => o.ModifierGroupId)
            .HasConstraintName("fk_modifier_options_modifier_groups_group_id")
            .OnDelete(DeleteBehavior.Cascade);
    }
}
