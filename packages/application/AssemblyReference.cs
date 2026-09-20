namespace RestaurantOrder.Application;

/// <summary>
/// Marker class for assembly scanning and architecture verification.
/// Explicitly references Domain to preserve layer dependency in compiled metadata.
/// </summary>
public static class AssemblyReference
{
    public static readonly Type DomainMarker = typeof(Domain.AssemblyReference);
}
