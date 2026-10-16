using System.Security.Cryptography;
using System.Text;
using RestaurantOrder.Domain.Branches;
using RestaurantOrder.Domain.Common;
using RestaurantOrder.Domain.Tenants;

namespace RestaurantOrder.Domain.Floor;

/// <summary>
/// Aggregate root representing an in-person customer service request (e.g., calling waiter, requesting water, napkins, or assistance).
/// Governs creation, idempotency replay, sanitization, and customer-side cancellation.
/// </summary>
public class ServiceRequest
{
    public const int MaxNoteLength = 200;
    public const int MaxIdempotencyKeyLength = 100;
    public const int MaxFingerprintLength = 64;

    public ServiceRequestId Id { get; private set; }
    public TenantId TenantId { get; private set; }
    public BranchId BranchId { get; private set; }
    public DiningSessionId DiningSessionId { get; private set; }
    public RestaurantTableId TableId { get; private set; }

    public ServiceRequestType RequestType { get; private set; }
    public ServiceRequestStatus Status { get; private set; }
    public string? Note { get; private set; }

    public string IdempotencyKey { get; private set; } = string.Empty;
    public string RequestFingerprint { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset? AcknowledgedAtUtc { get; private set; }
    public DateTimeOffset? CompletedAtUtc { get; private set; }

    public Guid ConcurrencyToken { get; private set; }
    public DateTimeOffset? UpdatedAtUtc { get; private set; }

    // Parameterless constructor for EF Core persistence materialization
    private ServiceRequest()
    {
    }

    /// <summary>
    /// Creates a new open customer service request.
    /// Validates inputs, sanitizes and verifies note against HTML injection and control characters.
    /// </summary>
    public static ServiceRequest Create(
        TenantId tenantId,
        BranchId branchId,
        DiningSessionId diningSessionId,
        RestaurantTableId tableId,
        ServiceRequestType requestType,
        string? note,
        string idempotencyKey,
        ServiceRequestId? id = null)
    {
        if (!Enum.IsDefined(typeof(ServiceRequestType), requestType))
        {
            throw new DomainException($"Invalid service request type '{requestType}'.");
        }

        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new DomainException("Idempotency key is required.");
        }

        var trimmedKey = idempotencyKey.Trim();
        if (trimmedKey.Length > MaxIdempotencyKeyLength)
        {
            throw new DomainException($"Idempotency key cannot exceed {MaxIdempotencyKeyLength} characters.");
        }

        var sanitizedNote = ValidateAndSanitizeNote(note);
        var fingerprint = ComputeFingerprint(tenantId, branchId, diningSessionId, tableId, requestType, sanitizedNote);
        var now = DateTimeOffset.UtcNow;

        return new ServiceRequest
        {
            Id = id ?? ServiceRequestId.New(),
            TenantId = tenantId,
            BranchId = branchId,
            DiningSessionId = diningSessionId,
            TableId = tableId,
            RequestType = requestType,
            Status = ServiceRequestStatus.Open,
            Note = sanitizedNote,
            IdempotencyKey = trimmedKey,
            RequestFingerprint = fingerprint,
            CreatedAtUtc = now,
            ConcurrencyToken = Guid.NewGuid(),
            UpdatedAtUtc = now
        };
    }

    /// <summary>
    /// Cancels an open service request.
    /// Only Open requests can be cancelled by the customer.
    /// </summary>
    public void Cancel()
    {
        if (Status == ServiceRequestStatus.Cancelled)
        {
            throw new DomainException("Service request is already cancelled.");
        }

        if (Status != ServiceRequestStatus.Open)
        {
            throw new DomainException(
                $"Cannot cancel service request in status '{Status}'. Only Open requests can be cancelled.");
        }

        var now = DateTimeOffset.UtcNow;
        Status = ServiceRequestStatus.Cancelled;
        UpdatedAtUtc = now;
        ConcurrencyToken = Guid.NewGuid();
    }

    /// <summary>
    /// Acknowledges the service request (reserved for Phase 10 Staff/Operations).
    /// </summary>
    public void Acknowledge()
    {
        if (Status != ServiceRequestStatus.Open)
        {
            throw new DomainException(
                $"Cannot acknowledge service request in status '{Status}'. Only Open requests can be acknowledged.");
        }

        var now = DateTimeOffset.UtcNow;
        Status = ServiceRequestStatus.Acknowledged;
        AcknowledgedAtUtc = now;
        UpdatedAtUtc = now;
        ConcurrencyToken = Guid.NewGuid();
    }

    /// <summary>
    /// Completes the service request (reserved for Phase 10 Staff/Operations).
    /// </summary>
    public void Complete()
    {
        if (Status != ServiceRequestStatus.Open && Status != ServiceRequestStatus.Acknowledged)
        {
            throw new DomainException(
                $"Cannot complete service request in status '{Status}'. Only Open or Acknowledged requests can be completed.");
        }

        var now = DateTimeOffset.UtcNow;
        Status = ServiceRequestStatus.Completed;
        CompletedAtUtc = now;
        UpdatedAtUtc = now;
        ConcurrencyToken = Guid.NewGuid();
    }

    /// <summary>
    /// Validates note ensuring it contains no HTML tags or non-printable control characters,
    /// and enforces length constraints.
    /// </summary>
    public static string? ValidateAndSanitizeNote(string? note)
    {
        if (string.IsNullOrWhiteSpace(note))
        {
            return null;
        }

        var trimmed = note.Trim();
        if (trimmed.Length > MaxNoteLength)
        {
            throw new DomainException($"Service request note cannot exceed {MaxNoteLength} characters.");
        }

        // Reject HTML tags
        if (trimmed.Contains('<') || trimmed.Contains('>'))
        {
            throw new DomainException("Service request note cannot contain HTML tags or markup.");
        }

        // Reject non-printable control characters (allow standard spaces/newlines)
        foreach (var c in trimmed)
        {
            if (char.IsControl(c) && c != '\r' && c != '\n' && c != '\t')
            {
                throw new DomainException("Service request note cannot contain control characters.");
            }
        }

        return trimmed;
    }

    /// <summary>
    /// Computes a deterministic SHA256 hex digest of the service request payload for idempotency collision verification.
    /// </summary>
    public static string ComputeFingerprint(
        TenantId tenantId,
        BranchId branchId,
        DiningSessionId diningSessionId,
        RestaurantTableId tableId,
        ServiceRequestType requestType,
        string? note)
    {
        var raw = $"{tenantId.Value:D}|{branchId.Value:D}|{diningSessionId.Value:D}|{tableId.Value:D}|{(int)requestType}|{note ?? string.Empty}";
        var bytes = Encoding.UTF8.GetBytes(raw);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public static bool MatchesFingerprint(string a, string b)
    {
        if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b))
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(a.Trim().ToLowerInvariant()),
            Encoding.UTF8.GetBytes(b.Trim().ToLowerInvariant()));
    }
}
