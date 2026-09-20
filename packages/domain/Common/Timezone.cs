namespace RestaurantOrder.Domain.Common;

/// <summary>
/// Immutable value object representing a validated IANA timezone identifier.
/// </summary>
public readonly record struct Timezone
{
    public const string DefaultId = "Europe/Istanbul";

    public string Id { get; }

    public Timezone(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new DomainException("Timezone ID cannot be null, empty, or whitespace.");
        }

        var trimmed = id.Trim();

        try
        {
            _ = TimeZoneInfo.FindSystemTimeZoneById(trimmed);
        }
        catch (TimeZoneNotFoundException)
        {
            throw new DomainException($"Timezone identifier '{trimmed}' is not a recognized system or IANA timezone.");
        }
        catch (InvalidTimeZoneException)
        {
            throw new DomainException($"Timezone identifier '{trimmed}' data is corrupted or invalid.");
        }

        Id = trimmed;
    }

    public static Timezone Default => new(DefaultId);

    public static Timezone From(string id) => new(id);

    public override string ToString() => Id;

    public static implicit operator string(Timezone timezone) => timezone.Id;
}
