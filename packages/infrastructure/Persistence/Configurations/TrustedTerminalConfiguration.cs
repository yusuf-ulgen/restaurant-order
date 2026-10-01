using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RestaurantOrder.Domain.Auth;
using RestaurantOrder.Domain.Branches;
using RestaurantOrder.Domain.Tenants;

namespace RestaurantOrder.Infrastructure.Persistence.Configurations;

public class TrustedTerminalConfiguration : IEntityTypeConfiguration<TrustedTerminal>
{
    public void Configure(EntityTypeBuilder<TrustedTerminal> builder)
    {
        builder.ToTable("trusted_terminals", "iam");

        builder.HasKey(t => t.Id);

        builder.Property(t => t.Id)
            .HasColumnName("id")
            .IsRequired();

        builder.Property(t => t.TenantId)
            .HasColumnName("tenant_id")
            .HasConversion(id => id.Value, value => new TenantId(value))
            .IsRequired();

        builder.Property(t => t.BranchId)
            .HasColumnName("branch_id")
            .HasConversion(id => id.Value, value => new BranchId(value))
            .IsRequired();

        builder.Property(t => t.DeviceIdentifier)
            .HasColumnName("device_identifier")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(t => t.TerminalName)
            .HasColumnName("terminal_name")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(t => t.SecretHash)
            .HasColumnName("secret_hash")
            .HasMaxLength(255)
            .IsRequired();

        builder.Property(t => t.IsActive)
            .HasColumnName("is_active")
            .HasDefaultValue(true)
            .IsRequired();

        builder.Property(t => t.LastHeartbeatAtUtc)
            .HasColumnName("last_heartbeat_at_utc")
            .HasColumnType("timestamp with time zone")
            .IsRequired(false);

        builder.Property(t => t.EnrolledAtUtc)
            .HasColumnName("enrolled_at_utc")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(t => t.RevokedAtUtc)
            .HasColumnName("revoked_at_utc")
            .HasColumnType("timestamp with time zone")
            .IsRequired(false);

        builder.HasIndex(t => new { t.TenantId, t.BranchId, t.DeviceIdentifier })
            .IsUnique()
            .HasDatabaseName("ix_trusted_terminals_tenant_branch_device");

        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(t => t.TenantId)
            .HasConstraintName("fk_trusted_terminals_tenants_tenant_id")
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Branch>()
            .WithMany()
            .HasPrincipalKey(br => new { br.TenantId, br.Id })
            .HasForeignKey(t => new { t.TenantId, t.BranchId })
            .HasConstraintName("fk_trusted_terminals_branches_tenant_id_branch_id")
            .OnDelete(DeleteBehavior.Restrict);
    }
}
