using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RestaurantOrder.Domain.Branches;
using RestaurantOrder.Domain.Floor;
using RestaurantOrder.Domain.Tenants;

namespace RestaurantOrder.Infrastructure.Persistence.Configurations;

public class ServiceRequestConfiguration : IEntityTypeConfiguration<ServiceRequest>
{
    public void Configure(EntityTypeBuilder<ServiceRequest> builder)
    {
        builder.ToTable("service_requests", "tenancy", t =>
        {
            t.HasCheckConstraint(
                "ck_service_requests_type",
                "request_type IN (1, 2, 3, 4)");

            t.HasCheckConstraint(
                "ck_service_requests_status",
                "status IN (1, 2, 3, 4)");
        });

        builder.HasKey(sr => sr.Id);

        builder.Property(sr => sr.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, value => new ServiceRequestId(value))
            .IsRequired();

        builder.Property(sr => sr.TenantId)
            .HasColumnName("tenant_id")
            .HasConversion(id => id.Value, value => new TenantId(value))
            .IsRequired();

        builder.Property(sr => sr.BranchId)
            .HasColumnName("branch_id")
            .HasConversion(id => id.Value, value => new BranchId(value))
            .IsRequired();

        builder.Property(sr => sr.DiningSessionId)
            .HasColumnName("dining_session_id")
            .HasConversion(id => id.Value, value => new DiningSessionId(value))
            .IsRequired();

        builder.Property(sr => sr.TableId)
            .HasColumnName("table_id")
            .HasConversion(id => id.Value, value => new RestaurantTableId(value))
            .IsRequired();

        builder.Property(sr => sr.RequestType)
            .HasColumnName("request_type")
            .HasConversion<int>()
            .IsRequired();

        builder.Property(sr => sr.Status)
            .HasColumnName("status")
            .HasConversion<int>()
            .IsRequired();

        builder.Property(sr => sr.Note)
            .HasColumnName("note")
            .HasMaxLength(ServiceRequest.MaxNoteLength)
            .IsRequired(false);

        builder.Property(sr => sr.IdempotencyKey)
            .HasColumnName("idempotency_key")
            .HasMaxLength(ServiceRequest.MaxIdempotencyKeyLength)
            .IsRequired();

        builder.Property(sr => sr.RequestFingerprint)
            .HasColumnName("request_fingerprint")
            .HasMaxLength(ServiceRequest.MaxFingerprintLength)
            .IsRequired();

        builder.Property(sr => sr.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .IsRequired();

        builder.Property(sr => sr.AcknowledgedAtUtc)
            .HasColumnName("acknowledged_at_utc")
            .IsRequired(false);

        builder.Property(sr => sr.CompletedAtUtc)
            .HasColumnName("completed_at_utc")
            .IsRequired(false);

        builder.Property(sr => sr.ConcurrencyToken)
            .HasColumnName("concurrency_token")
            .IsConcurrencyToken()
            .IsRequired();

        builder.Property(sr => sr.UpdatedAtUtc)
            .HasColumnName("updated_at_utc")
            .IsRequired(false);

        // Foreign keys to tables and dining sessions
        builder.HasOne<RestaurantTable>()
            .WithMany()
            .HasForeignKey(sr => new { sr.TenantId, sr.BranchId, sr.TableId })
            .HasPrincipalKey(rt => new { rt.TenantId, rt.BranchId, rt.Id })
            .HasConstraintName("fk_service_requests_table")
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<DiningSession>()
            .WithMany()
            .HasForeignKey(sr => new { sr.TenantId, sr.BranchId, sr.DiningSessionId })
            .HasPrincipalKey(ds => new { ds.TenantId, ds.BranchId, ds.Id })
            .HasConstraintName("fk_service_requests_session")
            .OnDelete(DeleteBehavior.Restrict);

        // Partial unique index: at most one OPEN request of a specific type per dining session
        builder.HasIndex(sr => new { sr.TenantId, sr.DiningSessionId, sr.RequestType })
            .HasDatabaseName("ix_service_requests_tenant_session_type_open")
            .HasFilter("status = 1")
            .IsUnique();

        // Idempotency unique index per tenant, branch and idempotency key
        builder.HasIndex(sr => new { sr.TenantId, sr.BranchId, sr.IdempotencyKey })
            .HasDatabaseName("ix_service_requests_tenant_branch_idempotency_key")
            .IsUnique();

        // Operational lookup indexes
        builder.HasIndex(sr => new { sr.TenantId, sr.DiningSessionId, sr.Status })
            .HasDatabaseName("ix_service_requests_tenant_session_status");

        builder.HasIndex(sr => new { sr.TenantId, sr.BranchId, sr.CreatedAtUtc })
            .HasDatabaseName("ix_service_requests_tenant_branch_created");
    }
}
