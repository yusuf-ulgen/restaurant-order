using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RestaurantOrder.Domain.Branches;
using RestaurantOrder.Domain.Tenants;

namespace RestaurantOrder.Infrastructure.Persistence.Configurations;

public class BranchOperatingHoursConfiguration : IEntityTypeConfiguration<BranchOperatingHours>
{
    public void Configure(EntityTypeBuilder<BranchOperatingHours> builder)
    {
        builder.ToTable("branch_operating_hours", "tenancy");

        builder.HasKey(boh => boh.Id);

        builder.Property(boh => boh.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, value => new BranchOperatingHoursId(value))
            .IsRequired();

        builder.Property(boh => boh.TenantId)
            .HasColumnName("tenant_id")
            .HasConversion(id => id.Value, value => new TenantId(value))
            .IsRequired();

        builder.Property(boh => boh.BranchId)
            .HasColumnName("branch_id")
            .HasConversion(id => id.Value, value => new BranchId(value))
            .IsRequired();

        builder.Property(boh => boh.ScheduleJson)
            .HasColumnName("schedule_json")
            .HasMaxLength(8000)
            .IsRequired();

        builder.Property(boh => boh.CreatedAtUtc)
            .HasColumnName("created_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(boh => boh.UpdatedAtUtc)
            .HasColumnName("updated_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired(false);

        builder.Property(boh => boh.ConcurrencyToken)
            .HasColumnName("concurrency_token")
            .IsConcurrencyToken()
            .IsRequired();

        // 1:1 relationship per branch inside a tenant
        builder.HasIndex(boh => new { boh.TenantId, boh.BranchId })
            .IsUnique()
            .HasDatabaseName("ix_branch_operating_hours_tenant_id_branch_id");

        // Composite foreign key to branches(tenant_id, id)
        builder.HasOne<Branch>()
            .WithMany()
            .HasPrincipalKey(b => new { b.TenantId, b.Id })
            .HasForeignKey(boh => new { boh.TenantId, boh.BranchId })
            .HasConstraintName("fk_branch_operating_hours_branches_tenant_id_branch_id")
            .OnDelete(DeleteBehavior.Cascade);

        // Foreign key to tenant
        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(boh => boh.TenantId)
            .HasConstraintName("fk_branch_operating_hours_tenants_tenant_id")
            .OnDelete(DeleteBehavior.Restrict);
    }
}
