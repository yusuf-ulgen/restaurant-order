using RestaurantOrder.Domain.Common;
using RestaurantOrder.Domain.Tenants;

namespace RestaurantOrder.Domain.Branches;

/// <summary>
/// Operating hours aggregate root for a branch.
/// Stores the complete 7-day schedule with overnight and closure rules.
/// </summary>
public class BranchOperatingHours
{
    public BranchOperatingHoursId Id { get; private set; }
    public TenantId TenantId { get; private set; }
    public BranchId BranchId { get; private set; }

    public string ScheduleJson { get; private set; } = null!;
    public Guid ConcurrencyToken { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? UpdatedAtUtc { get; private set; }

    // Parameterless constructor for EF Core persistence materialization
    private BranchOperatingHours()
    {
    }

    public static BranchOperatingHours Create(
        TenantId tenantId,
        BranchId branchId,
        WeeklySchedule? initialSchedule = null,
        BranchOperatingHoursId? id = null)
    {
        var schedule = initialSchedule ?? WeeklySchedule.CreateDefault();
        var hoursId = id ?? BranchOperatingHoursId.New();

        return new BranchOperatingHours
        {
            Id = hoursId,
            TenantId = tenantId,
            BranchId = branchId,
            ScheduleJson = schedule.ToJson(),
            ConcurrencyToken = Guid.NewGuid(),
            CreatedAtUtc = DateTime.UtcNow
        };
    }

    public void SetSchedule(WeeklySchedule schedule)
    {
        if (schedule == null)
        {
            throw new DomainException("Weekly operating schedule cannot be null.");
        }

        ScheduleJson = schedule.ToJson();
        UpdatedAtUtc = DateTime.UtcNow;
        ConcurrencyToken = Guid.NewGuid();
    }

    public WeeklySchedule GetSchedule() => WeeklySchedule.FromJson(ScheduleJson);
}
