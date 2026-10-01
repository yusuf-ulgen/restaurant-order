using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RestaurantOrder.Domain.Auth;
using RestaurantOrder.Domain.Branches;
using RestaurantOrder.Domain.Tenants;

namespace RestaurantOrder.Infrastructure.Persistence.Configurations;

public class UserMembershipConfiguration : IEntityTypeConfiguration<UserMembership>
{
    public void Configure(EntityTypeBuilder<UserMembership> builder)
    {
        builder.ToTable("memberships", "iam");

        builder.HasKey(m => m.Id);

        builder.Property(m => m.Id)
            .HasColumnName("id")
            .IsRequired();

        builder.Property(m => m.TenantId)
            .HasColumnName("tenant_id")
            .HasConversion(id => id.Value, value => new TenantId(value))
            .IsRequired();

        builder.Property(m => m.UserId)
            .HasColumnName("user_id")
            .HasConversion(id => id.Value, value => new UserId(value))
            .IsRequired();

        builder.Property(m => m.Role)
            .HasColumnName("role")
            .HasConversion<int>()
            .IsRequired();

        builder.Property(m => m.BranchId)
            .HasColumnName("branch_id")
            .HasConversion(
                id => id.HasValue ? id.Value.Value : (Guid?)null,
                value => value.HasValue ? new BranchId(value.Value) : null)
            .IsRequired(false);

        builder.Property(m => m.Status)
            .HasColumnName("status")
            .HasConversion<int>()
            .HasDefaultValue(UserMembershipStatus.Active)
            .IsRequired();

        builder.Property(m => m.IsActive)
            .HasColumnName("is_active")
            .HasDefaultValue(true)
            .IsRequired();

        builder.Property(m => m.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(m => m.UpdatedAtUtc)
            .HasColumnName("updated_at_utc")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(m => m.ConcurrencyToken)
            .HasColumnName("concurrency_token")
            .IsConcurrencyToken()
            .IsRequired();

        builder.HasIndex(m => m.TenantId)
            .HasDatabaseName("ix_memberships_tenant_id");

        builder.HasIndex(m => m.UserId)
            .HasDatabaseName("ix_memberships_user_id");

        builder.HasIndex(m => new { m.TenantId, m.BranchId })
            .HasDatabaseName("ix_memberships_tenant_branch");

        // Foreign key to Tenant
        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(m => m.TenantId)
            .HasConstraintName("fk_memberships_tenants_tenant_id")
            .OnDelete(DeleteBehavior.Restrict);

        // Foreign key to User
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(m => m.UserId)
            .HasConstraintName("fk_memberships_users_user_id")
            .OnDelete(DeleteBehavior.Cascade);

        // Composite foreign key to Branch (tenant_id, branch_id)
        builder.HasOne<Branch>()
            .WithMany()
            .HasPrincipalKey(br => new { br.TenantId, br.Id })
            .HasForeignKey(m => new { m.TenantId, m.BranchId })
            .HasConstraintName("fk_memberships_branches_tenant_id_branch_id")
            .OnDelete(DeleteBehavior.Restrict);
    }
}
