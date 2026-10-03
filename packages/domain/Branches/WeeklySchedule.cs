using System.Text.Json;
using System.Text.Json.Serialization;
using RestaurantOrder.Domain.Common;

namespace RestaurantOrder.Domain.Branches;

/// <summary>
/// Domain model for a branch's full 7-day operating schedule.
/// Validates complete coverage of all 7 days, intra-day slot ordering,
/// and cross-day overnight spillover boundary collisions.
/// </summary>
public sealed class WeeklySchedule
{
    private readonly Dictionary<DayOfWeek, OperatingDaySchedule> _days;

    public IReadOnlyCollection<OperatingDaySchedule> Days => _days.Values;

    public OperatingDaySchedule this[DayOfWeek day] => _days[day];

    public WeeklySchedule(IEnumerable<OperatingDaySchedule> daySchedules)
    {
        if (daySchedules == null)
        {
            throw new DomainException("Weekly schedule cannot be null.");
        }

        _days = new Dictionary<DayOfWeek, OperatingDaySchedule>();
        foreach (var day in daySchedules)
        {
            if (!_days.TryAdd(day.DayOfWeek, day))
            {
                throw new DomainException($"Duplicate schedule entry detected for {day.DayOfWeek}.");
            }
        }

        // Must cover all 7 days of the week
        foreach (var dow in Enum.GetValues<DayOfWeek>())
        {
            if (!_days.ContainsKey(dow))
            {
                throw new DomainException($"Weekly schedule is missing configuration for {dow}. All 7 days must be configured.");
            }
        }

        ValidateCrossDayOvernightSpillovers();
    }

    private void ValidateCrossDayOvernightSpillovers()
    {
        // For each day, if it has an overnight slot ending at CloseTime in the morning of the next day,
        // verify that the next day's slots do not start before that CloseTime.
        foreach (var (currentDow, currentSchedule) in _days)
        {
            if (currentSchedule.IsClosed)
            {
                continue;
            }

            var overnightSlots = currentSchedule.TimeSlots.Where(s => s.IsOvernight).ToList();
            if (overnightSlots.Count == 0)
            {
                continue;
            }

            var nextDow = (DayOfWeek)(((int)currentDow + 1) % 7);
            var nextSchedule = _days[nextDow];

            foreach (var slot in overnightSlots)
            {
                // slot on current day occupies [00:00, slot.CloseTime) on next day
                var spilloverEnd = slot.CloseTime;

                // Check against next day's slots
                foreach (var nextSlot in nextSchedule.TimeSlots)
                {
                    // Touching boundary nextSlot.OpenTime == spilloverEnd is permitted.
                    // If nextSlot starts before spilloverEnd, conflict!
                    if (nextSlot.OpenTime < spilloverEnd)
                    {
                        throw new DomainException(
                            $"Overnight time slot on {currentDow} ({slot}) spills over into {nextDow} until {spilloverEnd:HH:mm} and conflicts with {nextDow} slot ({nextSlot}).");
                    }
                }
            }
        }
    }

    public static WeeklySchedule CreateDefault()
    {
        var days = new List<OperatingDaySchedule>();
        var standardSlot = new TimeSlot(new TimeOnly(9, 0), new TimeOnly(22, 0));

        foreach (var dow in Enum.GetValues<DayOfWeek>())
        {
            days.Add(OperatingDaySchedule.Open(dow, standardSlot));
        }

        return new WeeklySchedule(days);
    }

    public string ToJson()
    {
        var rawDays = _days.Values.Select(d => new RawDayScheduleDto
        {
            DayOfWeek = (int)d.DayOfWeek,
            IsClosed = d.IsClosed,
            Slots = d.TimeSlots.Select(s => new RawTimeSlotDto
            {
                OpenTime = s.OpenTime.ToString("HH:mm"),
                CloseTime = s.CloseTime.ToString("HH:mm")
            }).ToList()
        }).ToList();

        return JsonSerializer.Serialize(rawDays, JsonOptions);
    }

    public static WeeklySchedule FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return CreateDefault();
        }

        try
        {
            var rawDays = JsonSerializer.Deserialize<List<RawDayScheduleDto>>(json, JsonOptions);
            if (rawDays == null || rawDays.Count == 0)
            {
                return CreateDefault();
            }

            var daySchedules = new List<OperatingDaySchedule>();
            foreach (var rawDay in rawDays)
            {
                var dow = (DayOfWeek)rawDay.DayOfWeek;
                if (rawDay.IsClosed)
                {
                    daySchedules.Add(OperatingDaySchedule.Closed(dow));
                }
                else
                {
                    var slots = (rawDay.Slots ?? Enumerable.Empty<RawTimeSlotDto>())
                        .Select(s => TimeSlot.FromStrings(s.OpenTime, s.CloseTime))
                        .ToList();

                    daySchedules.Add(new OperatingDaySchedule(dow, isClosed: false, slots));
                }
            }

            return new WeeklySchedule(daySchedules);
        }
        catch (DomainException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new DomainException($"Operating hours schedule format is invalid: {ex.Message}");
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private sealed class RawDayScheduleDto
    {
        public int DayOfWeek { get; set; }
        public bool IsClosed { get; set; }
        public List<RawTimeSlotDto>? Slots { get; set; }
    }

    private sealed class RawTimeSlotDto
    {
        public string OpenTime { get; set; } = null!;
        public string CloseTime { get; set; } = null!;
    }
}
