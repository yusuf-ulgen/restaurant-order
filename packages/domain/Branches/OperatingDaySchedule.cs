using RestaurantOrder.Domain.Common;

namespace RestaurantOrder.Domain.Branches;

/// <summary>
/// Domain model for a single day of the week's operating schedule.
/// Enforces invariants: closed days cannot have slots, overlapping slots are rejected,
/// touching intervals [08:00-14:00)[14:00-22:00) are permitted and sorted by open time.
/// </summary>
public sealed class OperatingDaySchedule
{
    public DayOfWeek DayOfWeek { get; }
    public bool IsClosed { get; }
    public IReadOnlyList<TimeSlot> TimeSlots { get; }

    public OperatingDaySchedule(DayOfWeek dayOfWeek, bool isClosed, IEnumerable<TimeSlot>? timeSlots = null)
    {
        DayOfWeek = dayOfWeek;
        IsClosed = isClosed;

        var slots = (timeSlots ?? Enumerable.Empty<TimeSlot>())
            .OrderBy(s => s.OpenTime)
            .ToList();

        if (isClosed && slots.Count > 0)
        {
            throw new DomainException($"Cannot add time slots to closed day {dayOfWeek}. A closed day must have zero time slots.");
        }

        // Validate intra-day overlaps
        for (var i = 0; i < slots.Count; i++)
        {
            for (var j = i + 1; j < slots.Count; j++)
            {
                if (slots[i].OverlapsWith(slots[j]))
                {
                    throw new DomainException(
                        $"Overlapping time slots detected on {dayOfWeek}: slot {slots[i]} overlaps with slot {slots[j]}.");
                }
            }
        }

        TimeSlots = slots.AsReadOnly();
    }

    public static OperatingDaySchedule Closed(DayOfWeek dayOfWeek) =>
        new(dayOfWeek, isClosed: true);

    public static OperatingDaySchedule Open(DayOfWeek dayOfWeek, params TimeSlot[] slots) =>
        new(dayOfWeek, isClosed: false, slots);
}
