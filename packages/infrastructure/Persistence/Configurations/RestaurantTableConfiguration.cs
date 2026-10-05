using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RestaurantOrder.Domain.Branches;
using RestaurantOrder.Domain.Floor;
using RestaurantOrder.Domain.Tenants;

namespace RestaurantOrder.Infrastructure.Persistence.Configurations;

public class RestaurantTableConfiguration : IEntityTypeConfiguration<RestaurantTable>
{
    public void Configure(EntityTypeBuilder<RestaurantTable> builder)
    {
        builder.ToTable("restaurant_tables", "tenancy", t =>
        {
            t.HasCheckConstraint(
                "ck_restaurant_tables_capacity",
                "capacity >= 1 AND capacity <= 100");

            t.HasCheckConstraint(
                "ck_restaurant_tables_coordinates",
                "position_x >= 0 AND position_x <= 10000 AND position_y >= 0 AND position_y <= 10000");

            t.HasCheckConstraint(
                "ck_restaurant_tables_dimensions",
                "width >= 10 AND width <= 5000 AND height >= 10 AND height <= 5000");

            t.HasCheckConstraint(
                "ck_restaurant_tables_rotation",
                "rotation_degrees >= 0 AND rotation_degrees < 360");

            t.HasCheckConstraint(
                "ck_restaurant_tables_qr_version",
                "qr_version >= 1");
        });

        builder.HasKey(rt => rt.Id);
        builder.HasAlternateKey(rt => new { rt.TenantId, rt.BranchId, rt.Id });

        builder.Property(rt => rt.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, value => new RestaurantTableId(value))
            .IsRequired();

        builder.Property(rt => rt.TenantId)
            .HasColumnName("tenant_id")
            .HasConversion(id => id.Value, value => new TenantId(value))
            .IsRequired();

        builder.Property(rt => rt.BranchId)
            .HasColumnName("branch_id")
            .HasConversion(id => id.Value, value => new BranchId(value))
            .IsRequired();

        builder.Property(rt => rt.DiningAreaId)
            .HasColumnName("dining_area_id")
            .HasConversion(id => id.Value, value => new DiningAreaId(value))
            .IsRequired();

        builder.Property(rt => rt.TableNumber)
            .HasColumnName("table_number")
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(rt => rt.Name)
            .HasColumnName("name")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(rt => rt.Capacity)
            .HasColumnName("capacity")
            .IsRequired();

        builder.Property(rt => rt.PositionX)
            .HasColumnName("position_x")
            .IsRequired();

        builder.Property(rt => rt.PositionY)
            .HasColumnName("position_y")
            .IsRequired();

        builder.Property(rt => rt.Width)
            .HasColumnName("width")
            .IsRequired();

        builder.Property(rt => rt.Height)
            .HasColumnName("height")
            .IsRequired();

        builder.Property(rt => rt.RotationDegrees)
            .HasColumnName("rotation_degrees")
            .IsRequired();

        builder.Property(rt => rt.Shape)
            .HasColumnName("shape")
            .HasConversion<int>()
            .IsRequired();

        builder.Property(rt => rt.IsActive)
            .HasColumnName("is_active")
            .IsRequired();

        builder.Property(rt => rt.QrVersion)
            .HasColumnName("qr_version")
            .IsRequired();

        builder.Property(rt => rt.PublicCode)
            .HasColumnName("public_code")
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(rt => rt.ConcurrencyToken)
            .HasColumnName("concurrency_token")
            .IsConcurrencyToken()
            .IsRequired();

        builder.Property(rt => rt.CreatedAtUtc)
            .HasColumnName("created_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(rt => rt.UpdatedAtUtc)
            .HasColumnName("updated_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired(false);

        // Unique table number per branch in tenant
        builder.HasIndex(rt => new { rt.TenantId, rt.BranchId, rt.TableNumber })
            .IsUnique()
            .HasDatabaseName("ix_restaurant_tables_tenant_id_branch_id_table_number");

        // Unique public code per branch in tenant
        builder.HasIndex(rt => new { rt.TenantId, rt.BranchId, rt.PublicCode })
            .IsUnique()
            .HasDatabaseName("ix_restaurant_tables_tenant_branch_public_code");

        builder.HasIndex(rt => new { rt.TenantId, rt.BranchId, rt.DiningAreaId })
            .HasDatabaseName("ix_restaurant_tables_tenant_id_branch_id_dining_area_id");

        // Composite foreign key to dining_areas(tenant_id, branch_id, id)
        builder.HasOne<DiningArea>()
            .WithMany()
            .HasPrincipalKey(da => new { da.TenantId, da.BranchId, da.Id })
            .HasForeignKey(rt => new { rt.TenantId, rt.BranchId, rt.DiningAreaId })
            .HasConstraintName("fk_restaurant_tables_dining_areas_tenant_branch_area")
            .OnDelete(DeleteBehavior.Restrict);

        // Composite foreign key to branches(tenant_id, id)
        builder.HasOne<Branch>()
            .WithMany()
            .HasPrincipalKey(b => new { b.TenantId, b.Id })
            .HasForeignKey(rt => new { rt.TenantId, rt.BranchId })
            .HasConstraintName("fk_restaurant_tables_branches_tenant_id_branch_id")
            .OnDelete(DeleteBehavior.Cascade);

        // Foreign key to tenant
        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(rt => rt.TenantId)
            .HasConstraintName("fk_restaurant_tables_tenants_tenant_id")
            .OnDelete(DeleteBehavior.Restrict);
    }
}
