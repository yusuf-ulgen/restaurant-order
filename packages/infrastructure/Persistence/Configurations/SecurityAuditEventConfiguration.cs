using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RestaurantOrder.Domain.Auth;
using RestaurantOrder.Domain.Branches;
using RestaurantOrder.Domain.Tenants;

namespace RestaurantOrder.Infrastructure.Persistence.Configurations;

public class SecurityAuditEventConfiguration : IEntityTypeConfiguration<SecurityAuditEvent>
{
    public void Configure(EntityTypeBuilder<SecurityAuditEvent> builder)
    {
        builder.ToTable("security_audit_events", "iam");

        builder.HasKey(a => a.Id);

        builder.Property(a => a.Id)
            .HasColumnName("id")
            .IsRequired();

        builder.Property(a => a.TenantId)
            .HasColumnName("tenant_id")
            .HasConversion(id => id.Value, value => new TenantId(value))
            .IsRequired();

        builder.Property(a => a.EventType)
            .HasColumnName("event_type")
            .HasMaxLength(60)
            .IsRequired();

        builder.Property(a => a.UserId)
            .HasColumnName("user_id")
            .HasConversion(
                id => id.HasValue ? id.Value.Value : (Guid?)null,
                value => value.HasValue ? new UserId(value.Value) : null)
            .IsRequired(false);

        builder.Property(a => a.BranchId)
            .HasColumnName("branch_id")
            .HasConversion(
                id => id.HasValue ? id.Value.Value : (Guid?)null,
                value => value.HasValue ? new BranchId(value.Value) : null)
            .IsRequired(false);

        builder.Property(a => a.IpAddress)
            .HasColumnName("ip_address")
            .HasMaxLength(45)
            .IsRequired(false);

        builder.Property(a => a.UserAgent)
            .HasColumnName("user_agent")
            .HasMaxLength(500)
            .IsRequired(false);

        builder.Property(a => a.DetailsJson)
            .HasColumnName("details_json")
            .HasColumnType("text")
            .IsRequired(false);

        builder.Property(a => a.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.HasIndex(a => a.TenantId)
            .HasDatabaseName("ix_security_audit_events_tenant_id");

        builder.HasIndex(a => a.CreatedAtUtc)
            .HasDatabaseName("ix_security_audit_events_created_at_utc");

        builder.HasIndex(a => a.UserId)
            .HasDatabaseName("ix_security_audit_events_user_id");

        builder.HasIndex(a => a.EventType)
            .HasDatabaseName("ix_security_audit_events_event_type");

        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(a => a.TenantId)
            .HasConstraintName("fk_security_audit_events_tenants_tenant_id")
            .OnDelete(DeleteBehavior.Restrict);
    }
}
