using System.Text.RegularExpressions;
using RestaurantOrder.Domain.Auth;
using RestaurantOrder.Domain.Branches;
using RestaurantOrder.Domain.Common;
using RestaurantOrder.Domain.Tenants;

namespace RestaurantOrder.Domain.Catalog;

/// <summary>
/// Represents branch-scoped temporary inventory availability (Quick 86) for a menu item or specific variant.
/// Decouples operational stock availability (out-of-stock, kitchen capacity) from catalog product lifecycle (IsActive).
/// </summary>
public class BranchItemAvailability
{
    private static readonly Regex HtmlTagRegex = new(@"<[^>]*>", RegexOptions.Compiled);

    public BranchItemAvailabilityId Id { get; private set; }
    public TenantId TenantId { get; private set; }
    public BranchId BranchId { get; private set; }
    public MenuItemId MenuItemId { get; private set; }
    public ItemVariantId? ItemVariantId { get; private set; }
    public bool IsAvailable { get; private set; }
    public AvailabilityReasonCode ReasonCode { get; private set; }
    public string? Note { get; private set; }
    public DateTime? ExpectedAvailableAtUtc { get; private set; }
    public UserId ChangedByUserId { get; private set; }
    public DateTime ChangedAtUtc { get; private set; }
    public Guid ConcurrencyToken { get; private set; }

    // Parameterless constructor for EF Core persistence materialization
    private BranchItemAvailability()
    {
    }

    public static BranchItemAvailability CreateItemUnavailable(
        TenantId tenantId,
        BranchId branchId,
        MenuItemId menuItemId,
        AvailabilityReasonCode reasonCode,
        string? note,
        DateTime? expectedAvailableAtUtc,
        UserId changedByUserId,
        BranchItemAvailabilityId? id = null)
    {
        ValidateUnavailableReasonCode(reasonCode);
        var validatedNote = ValidateNote(note);
        ValidateExpectedAvailableAtUtc(expectedAvailableAtUtc);

        return new BranchItemAvailability
        {
            Id = id ?? BranchItemAvailabilityId.New(),
            TenantId = tenantId,
            BranchId = branchId,
            MenuItemId = menuItemId,
            ItemVariantId = null,
            IsAvailable = false,
            ReasonCode = reasonCode,
            Note = validatedNote,
            ExpectedAvailableAtUtc = expectedAvailableAtUtc,
            ChangedByUserId = changedByUserId,
            ChangedAtUtc = DateTime.UtcNow,
            ConcurrencyToken = Guid.NewGuid()
        };
    }

    public static BranchItemAvailability CreateVariantUnavailable(
        TenantId tenantId,
        BranchId branchId,
        MenuItemId menuItemId,
        ItemVariantId itemVariantId,
        AvailabilityReasonCode reasonCode,
        string? note,
        DateTime? expectedAvailableAtUtc,
        UserId changedByUserId,
        BranchItemAvailabilityId? id = null)
    {
        ValidateUnavailableReasonCode(reasonCode);
        var validatedNote = ValidateNote(note);
        ValidateExpectedAvailableAtUtc(expectedAvailableAtUtc);

        return new BranchItemAvailability
        {
            Id = id ?? BranchItemAvailabilityId.New(),
            TenantId = tenantId,
            BranchId = branchId,
            MenuItemId = menuItemId,
            ItemVariantId = itemVariantId,
            IsAvailable = false,
            ReasonCode = reasonCode,
            Note = validatedNote,
            ExpectedAvailableAtUtc = expectedAvailableAtUtc,
            ChangedByUserId = changedByUserId,
            ChangedAtUtc = DateTime.UtcNow,
            ConcurrencyToken = Guid.NewGuid()
        };
    }

    public void MarkUnavailable(
        AvailabilityReasonCode reasonCode,
        string? note,
        DateTime? expectedAvailableAtUtc,
        UserId changedByUserId)
    {
        ValidateUnavailableReasonCode(reasonCode);
        var validatedNote = ValidateNote(note);
        ValidateExpectedAvailableAtUtc(expectedAvailableAtUtc);

        IsAvailable = false;
        ReasonCode = reasonCode;
        Note = validatedNote;
        ExpectedAvailableAtUtc = expectedAvailableAtUtc;
        ChangedByUserId = changedByUserId;
        ChangedAtUtc = DateTime.UtcNow;
        ConcurrencyToken = Guid.NewGuid();
    }

    public void Restock(
        string? note,
        UserId changedByUserId)
    {
        var validatedNote = ValidateNote(note);

        IsAvailable = true;
        ReasonCode = AvailabilityReasonCode.Restocked;
        Note = validatedNote;
        ExpectedAvailableAtUtc = null;
        ChangedByUserId = changedByUserId;
        ChangedAtUtc = DateTime.UtcNow;
        ConcurrencyToken = Guid.NewGuid();
    }

    private static void ValidateUnavailableReasonCode(AvailabilityReasonCode reasonCode)
    {
        if (reasonCode == AvailabilityReasonCode.Restocked)
        {
            throw new DomainException("ReasonCode 'Restocked' cannot be used when marking an item or variant unavailable. Use Restock action instead.");
        }

        if (!Enum.IsDefined(typeof(AvailabilityReasonCode), reasonCode))
        {
            throw new DomainException($"Invalid AvailabilityReasonCode: {(int)reasonCode}.");
        }
    }

    public static string? ValidateNote(string? note)
    {
        if (string.IsNullOrWhiteSpace(note))
        {
            return null;
        }

        var trimmed = note.Trim();
        if (trimmed.Length > 500)
        {
            throw new DomainException("Availability note cannot exceed 500 characters.");
        }

        if (HtmlTagRegex.IsMatch(trimmed))
        {
            throw new DomainException("Availability note cannot contain HTML or markup tags.");
        }

        return trimmed;
    }

    public static void ValidateExpectedAvailableAtUtc(DateTime? expectedAt)
    {
        if (!expectedAt.HasValue)
        {
            return;
        }

        // Allow 1 minute clock tolerance for slight client-server clock skew
        if (expectedAt.Value < DateTime.UtcNow.AddMinutes(-1))
        {
            throw new DomainException("ExpectedAvailableAtUtc cannot be in the past.");
        }
    }
}
