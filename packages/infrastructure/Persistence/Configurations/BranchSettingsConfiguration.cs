using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RestaurantOrder.Domain.Branches;
using RestaurantOrder.Domain.Common;
using RestaurantOrder.Domain.Tenants;

namespace RestaurantOrder.Infrastructure.Persistence.Configurations;

public class BranchSettingsConfiguration : IEntityTypeConfiguration<BranchSettings>
{
    public void Configure(EntityTypeBuilder<BranchSettings> builder)
    {
        builder.ToTable("branch_settings", "tenancy");

        builder.HasKey(bs => bs.Id);

        builder.Property(bs => bs.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, value => new BranchSettingsId(value))
            .IsRequired();

        builder.Property(bs => bs.TenantId)
            .HasColumnName("tenant_id")
            .HasConversion(id => id.Value, value => new TenantId(value))
            .IsRequired();

        builder.Property(bs => bs.BranchId)
            .HasColumnName("branch_id")
            .HasConversion(id => id.Value, value => new BranchId(value))
            .IsRequired();

        builder.Property(bs => bs.Timezone)
            .HasColumnName("timezone")
            .HasMaxLength(100)
            .HasConversion(tz => tz.Id, value => new Timezone(value))
            .IsRequired();

        builder.Property(bs => bs.Currency)
            .HasColumnName("currency")
            .HasMaxLength(3)
            .HasConversion(c => c.Code, value => new Currency(value))
            .IsRequired();

        builder.Property(bs => bs.DefaultLocale)
            .HasColumnName("default_locale")
            .HasMaxLength(10)
            .IsRequired();

        builder.Property(bs => bs.SupportedLocalesJson)
            .HasColumnName("supported_locales")
            .HasMaxLength(1000)
            .IsRequired();

        builder.Property(bs => bs.PricesIncludeTax)
            .HasColumnName("prices_include_tax")
            .IsRequired();

        builder.Property(bs => bs.DefaultTaxRateBps)
            .HasColumnName("default_tax_rate_bps")
            .IsRequired();

        builder.Property(bs => bs.IsServiceChargeEnabled)
            .HasColumnName("is_service_charge_enabled")
            .IsRequired();

        builder.Property(bs => bs.ServiceChargeRateBps)
            .HasColumnName("service_charge_rate_bps")
            .IsRequired();

        builder.Property(bs => bs.IsOrderTakingEnabled)
            .HasColumnName("is_order_taking_enabled")
            .IsRequired();

        builder.Property(bs => bs.DisplayName)
            .HasColumnName("display_name")
            .HasMaxLength(200)
            .IsRequired(false);

        builder.Property(bs => bs.PhoneNumber)
            .HasColumnName("phone_number")
            .HasMaxLength(50)
            .IsRequired(false);

        builder.Property(bs => bs.Email)
            .HasColumnName("email")
            .HasMaxLength(100)
            .IsRequired(false);

        builder.Property(bs => bs.Address)
            .HasColumnName("address")
            .HasMaxLength(500)
            .IsRequired(false);

        builder.Property(bs => bs.CreatedAtUtc)
            .HasColumnName("created_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(bs => bs.UpdatedAtUtc)
            .HasColumnName("updated_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired(false);

        builder.Property(bs => bs.ConcurrencyToken)
            .HasColumnName("concurrency_token")
            .IsConcurrencyToken()
            .IsRequired();

        // 1:1 relationship per branch inside a tenant
        builder.HasIndex(bs => new { bs.TenantId, bs.BranchId })
            .IsUnique()
            .HasDatabaseName("ix_branch_settings_tenant_id_branch_id");

        // Composite foreign key to branches(tenant_id, id)
        builder.HasOne<Branch>()
            .WithMany()
            .HasPrincipalKey(b => new { b.TenantId, b.Id })
            .HasForeignKey(bs => new { bs.TenantId, bs.BranchId })
            .HasConstraintName("fk_branch_settings_branches_tenant_id_branch_id")
            .OnDelete(DeleteBehavior.Cascade);

        // Foreign key to tenant
        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(bs => bs.TenantId)
            .HasConstraintName("fk_branch_settings_tenants_tenant_id")
            .OnDelete(DeleteBehavior.Restrict);
    }
}
