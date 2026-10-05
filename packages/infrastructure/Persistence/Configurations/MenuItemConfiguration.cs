using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RestaurantOrder.Domain.Branches;
using RestaurantOrder.Domain.Catalog;
using RestaurantOrder.Domain.Tenants;

namespace RestaurantOrder.Infrastructure.Persistence.Configurations;

public class MenuItemConfiguration : IEntityTypeConfiguration<MenuItem>
{
    public void Configure(EntityTypeBuilder<MenuItem> builder)
    {
        builder.ToTable("menu_items", "tenancy");

        builder.HasKey(m => m.Id);
        builder.HasAlternateKey(m => new { m.TenantId, m.MenuId, m.Id });
        builder.HasAlternateKey(m => new { m.TenantId, m.BranchId, m.MenuId, m.Id });
        builder.HasAlternateKey(m => new { m.TenantId, m.BranchId, m.Id });

        builder.Property(m => m.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, value => new MenuItemId(value))
            .IsRequired();

        builder.Property(m => m.TenantId)
            .HasColumnName("tenant_id")
            .HasConversion(id => id.Value, value => new TenantId(value))
            .IsRequired();

        builder.Property(m => m.BranchId)
            .HasColumnName("branch_id")
            .HasConversion(id => id.Value, value => new BranchId(value))
            .IsRequired();

        builder.Property(m => m.MenuId)
            .HasColumnName("menu_id")
            .HasConversion(id => id.Value, value => new MenuId(value))
            .IsRequired();

        builder.Property(m => m.CategoryId)
            .HasColumnName("category_id")
            .HasConversion(id => id.Value, value => new MenuCategoryId(value))
            .IsRequired();

        builder.Property(m => m.Name)
            .HasColumnName("name")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(m => m.Slug)
            .HasColumnName("slug")
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(m => m.ShortDescription)
            .HasColumnName("short_description")
            .HasMaxLength(200)
            .IsRequired(false);

        builder.Property(m => m.FullDescription)
            .HasColumnName("full_description")
            .HasMaxLength(2000)
            .IsRequired(false);

        builder.Property(m => m.ImageUrl)
            .HasColumnName("image_url")
            .HasMaxLength(500)
            .IsRequired(false);

        builder.Property(m => m.BasePriceMinorUnits)
            .HasColumnName("base_price_minor_units")
            .HasConversion(p => p.MinorUnits, value => PriceAmount.FromMinorUnits(value))
            .IsRequired();

        builder.Property(m => m.SortOrder)
            .HasColumnName("sort_order")
            .IsRequired();

        builder.Property(m => m.IsActive)
            .HasColumnName("is_active")
            .IsRequired();

        builder.Property(m => m.PreparationStationId)
            .HasColumnName("preparation_station_id")
            .HasConversion(id => id.HasValue ? id.Value.Value : (Guid?)null, value => value.HasValue ? new PreparationStationId(value.Value) : null)
            .IsRequired(false);

        builder.Property(m => m.SpicyLevel)
            .HasColumnName("spicy_level")
            .HasConversion(s => s.Value, value => new SpicyLevel(value))
            .HasDefaultValue(SpicyLevel.None)
            .IsRequired();

        var dietaryComparer = new Microsoft.EntityFrameworkCore.ChangeTracking.ValueComparer<IReadOnlySet<DietaryTag>>(
            (c1, c2) => (c1 == null && c2 == null) || (c1 != null && c2 != null && c1.SetEquals(c2)),
            c => c.Aggregate(0, (a, v) => HashCode.Combine(a, v.GetHashCode())),
            c => new HashSet<DietaryTag>(c));

        builder.Property(m => m.DietaryTags)
            .HasField("_dietaryTags")
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .HasColumnName("dietary_tags")
            .HasConversion(
                tags => System.Text.Json.JsonSerializer.Serialize(tags.Select(t => t.ToString()).ToList(), (System.Text.Json.JsonSerializerOptions?)null),
                json => string.IsNullOrWhiteSpace(json)
                    ? new HashSet<DietaryTag>()
                    : System.Text.Json.JsonSerializer.Deserialize<List<string>>(json, (System.Text.Json.JsonSerializerOptions?)null)!
                        .Select(s => Enum.Parse<DietaryTag>(s, true))
                        .ToHashSet())
            .Metadata.SetValueComparer(dietaryComparer);

        var allergenComparer = new Microsoft.EntityFrameworkCore.ChangeTracking.ValueComparer<IReadOnlySet<AllergenTag>>(
            (c1, c2) => (c1 == null && c2 == null) || (c1 != null && c2 != null && c1.SetEquals(c2)),
            c => c.Aggregate(0, (a, v) => HashCode.Combine(a, v.GetHashCode())),
            c => new HashSet<AllergenTag>(c));

        builder.Property(m => m.AllergenTags)
            .HasField("_allergenTags")
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .HasColumnName("allergen_tags")
            .HasConversion(
                tags => System.Text.Json.JsonSerializer.Serialize(tags.Select(t => t.ToString()).ToList(), (System.Text.Json.JsonSerializerOptions?)null),
                json => string.IsNullOrWhiteSpace(json)
                    ? new HashSet<AllergenTag>()
                    : System.Text.Json.JsonSerializer.Deserialize<List<string>>(json, (System.Text.Json.JsonSerializerOptions?)null)!
                        .Select(s => Enum.Parse<AllergenTag>(s, true))
                        .ToHashSet())
            .Metadata.SetValueComparer(allergenComparer);

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

        // Unique slug per menu in tenant
        builder.HasIndex(m => new { m.TenantId, m.MenuId, m.Slug })
            .IsUnique()
            .HasDatabaseName("ix_menu_items_tenant_id_menu_id_slug");

        builder.HasIndex(m => new { m.TenantId, m.CategoryId, m.SortOrder })
            .HasDatabaseName("ix_menu_items_tenant_id_category_id_sort_order");

        // Composite FK to menus (tenant_id, branch_id, id)
        builder.HasOne<Menu>()
            .WithMany()
            .HasPrincipalKey(m => new { m.TenantId, m.BranchId, m.Id })
            .HasForeignKey(m => new { m.TenantId, m.BranchId, m.MenuId })
            .HasConstraintName("fk_menu_items_menus_tenant_branch_menu")
            .OnDelete(DeleteBehavior.Cascade);

        // Composite FK to menu_categories (tenant_id, branch_id, menu_id, id)
        builder.HasOne<MenuCategory>()
            .WithMany()
            .HasPrincipalKey(mc => new { mc.TenantId, mc.BranchId, mc.MenuId, mc.Id })
            .HasForeignKey(m => new { m.TenantId, m.BranchId, m.MenuId, m.CategoryId })
            .HasConstraintName("fk_menu_items_categories_tenant_branch_menu_cat")
            .OnDelete(DeleteBehavior.Cascade);

        // Composite FK to branches
        builder.HasOne<Branch>()
            .WithMany()
            .HasPrincipalKey(b => new { b.TenantId, b.Id })
            .HasForeignKey(m => new { m.TenantId, m.BranchId })
            .HasConstraintName("fk_menu_items_branches_tenant_id_branch_id")
            .OnDelete(DeleteBehavior.Cascade);

        // FK to tenant
        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(m => m.TenantId)
            .HasConstraintName("fk_menu_items_tenants_tenant_id")
            .OnDelete(DeleteBehavior.Restrict);

        // Composite FK to preparation_stations (tenant_id, branch_id, id)
        builder.HasOne<PreparationStation>()
            .WithMany()
            .HasPrincipalKey(ps => new { ps.TenantId, ps.BranchId, ps.Id })
            .HasForeignKey(m => new { m.TenantId, m.BranchId, m.PreparationStationId })
            .HasConstraintName("fk_menu_items_prep_stations_tenant_branch_station")
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);

        // 1-to-many relationship with ItemVariant
        builder.HasMany(m => m.Variants)
            .WithOne()
            .HasPrincipalKey(m => new { m.TenantId, m.BranchId, m.Id })
            .HasForeignKey(v => new { v.TenantId, v.BranchId, v.MenuItemId })
            .HasConstraintName("fk_item_variants_menu_items_tenant_branch_item")
            .OnDelete(DeleteBehavior.Cascade);

        // 1-to-many relationship with MenuItemModifierGroupAssignment
        builder.HasMany(m => m.ModifierGroupAssignments)
            .WithOne(a => a.MenuItem)
            .HasPrincipalKey(m => new { m.TenantId, m.BranchId, m.Id })
            .HasForeignKey(a => new { a.TenantId, a.BranchId, a.MenuItemId })
            .HasConstraintName("fk_item_modifier_assignments_menu_items_tenant_branch_item")
            .OnDelete(DeleteBehavior.Cascade);
    }
}
