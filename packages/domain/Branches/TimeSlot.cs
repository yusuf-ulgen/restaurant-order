using RestaurantOrder.Domain.Common;

namespace RestaurantOrder.Domain.Branches;

/// <summary>
/// Immutable value object representing a branch operating time slot during a day.
/// Preserved as wall-clock local time without timezone skew.
/// Supports overnight operating windows crossing midnight (e.g. 20:00 - 02:00).
/// </summary>
public readonly record struct TimeSlot : IComparable<TimeSlot>
{
    public TimeOnly OpenTime { get; }
    public TimeOnly CloseTime { get; }

    public bool IsOvernight => CloseTime < OpenTime;

    public TimeSlot(TimeOnly openTime, TimeOnly closeTime)
    {
        if (openTime == closeTime)
        {
            throw new DomainException($"Time slot open time and close time cannot be identical ({openTime:HH:mm}). Zero-duration intervals are not permitted.");
        }

        OpenTime = openTime;
        CloseTime = closeTime;
    }

    public static TimeSlot FromStrings(string openTimeStr, string closeTimeStr)
    {
        if (!TimeOnly.TryParse(openTimeStr, out var openTime))
        {
            throw new DomainException($"Invalid open time format '{openTimeStr}'. Expected 'HH:mm'.");
        }

        if (!TimeOnly.TryParse(closeTimeStr, out var closeTime))
        {
            throw new DomainException($"Invalid close time format '{closeTimeStr}'. Expected 'HH:mm'.");
        }

        return new TimeSlot(openTime, closeTime);
    }

    /// <summary>
    /// Evaluates whether this time slot overlaps with another slot within the same day context.
    /// Half-open interval convention [Open, Close) is used. Touching boundaries (e.g., 08:00-14:00 and 14:00-22:00)
    /// do not overlap and are explicitly permitted.
    /// </summary>
    public bool OverlapsWith(TimeSlot other)
    {
        // Decompose each slot into same-day standard components:
        // Regular slot [O, C): interval from O to C
        // Overnight slot [O, C): interval [O, 24:00) on current day, plus [00:00, C) next day
        var thisIntervals = GetSubIntervals();
        var otherIntervals = other.GetSubIntervals();

        foreach (var (startA, endA) in thisIntervals)
        {
            foreach (var (startB, endB) in otherIntervals)
            {
                // [startA, endA) overlaps with [startB, endB) if startA < endB && startB < endA
                if (startA < endB && startB < endA)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private (TimeOnly Start, TimeOnly End)[] GetSubIntervals()
    {
        if (!IsOvernight)
        {
            return new[] { (OpenTime, CloseTime) };
        }

        // Overnight slot on day D occupies [OpenTime, 23:59:59.999] on day D, and spills into next day.
        // Within same-day slot collection, the [OpenTime, 24:00) part is evaluated.
        return new[]
        {
            (OpenTime, TimeOnly.MaxValue),
            (TimeOnly.MinValue, CloseTime)
        };
    }

    public int CompareTo(TimeSlot other) => OpenTime.CompareTo(other.OpenTime);

    public override string ToString() => $"{OpenTime:HH:mm} - {CloseTime:HH:mm}{(IsOvernight ? " (+1)" : "")}";
}
