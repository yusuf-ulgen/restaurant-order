using RestaurantOrder.Domain.Branches;
using RestaurantOrder.Domain.Common;
using RestaurantOrder.Domain.Tenants;

namespace RestaurantOrder.Domain.Floor;

/// <summary>
/// Cryptographically signed, canonical QR payload representation conforming to Phase 6.3 specifications.
/// Encapsulates schema versioning, tenant/branch scope, opaque table code, QR mode, and session bindings.
/// </summary>
public sealed record QrPayload
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; }
    public string KeyId { get; init; }
    public QrMode Mode { get; init; }
    public TenantId TenantId { get; init; }
    public BranchId BranchId { get; init; }
    public string PublicTableCode { get; init; }
    public int QrVersion { get; init; }
    public DiningSessionId? TableSessionId { get; init; }
    public long? ExpiresAtUnix { get; init; }
    public string? Nonce { get; init; }

    public QrPayload(
        int schemaVersion,
        string keyId,
        QrMode mode,
        TenantId tenantId,
        BranchId branchId,
        string publicTableCode,
        int qrVersion,
        DiningSessionId? tableSessionId = null,
        long? expiresAtUnix = null,
        string? nonce = null)
    {
        if (schemaVersion != CurrentSchemaVersion)
        {
            throw new DomainException($"Unsupported QR payload schema version '{schemaVersion}'. Expected {CurrentSchemaVersion}.");
        }

        if (string.IsNullOrWhiteSpace(keyId))
        {
            throw new DomainException("KeyId cannot be empty.");
        }

        if (tenantId.Value == Guid.Empty)
        {
            throw new DomainException("TenantId cannot be empty in QR payload.");
        }

        if (branchId.Value == Guid.Empty)
        {
            throw new DomainException("BranchId cannot be empty in QR payload.");
        }

        if (string.IsNullOrWhiteSpace(publicTableCode))
        {
            throw new DomainException("PublicTableCode cannot be empty.");
        }

        if (qrVersion < 1)
        {
            throw new DomainException("QrVersion must be >= 1.");
        }

        if (!Enum.IsDefined(typeof(QrMode), mode))
        {
            throw new DomainException($"Invalid QrMode '{mode}'.");
        }

        if (mode == QrMode.Dynamic)
        {
            if (!tableSessionId.HasValue || tableSessionId.Value.Value == Guid.Empty)
            {
                throw new DomainException("Dynamic QR payload requires a non-empty TableSessionId.");
            }

            if (!expiresAtUnix.HasValue || expiresAtUnix.Value <= 0)
            {
                throw new DomainException("Dynamic QR payload requires a positive unix expiration timestamp.");
            }
        }
        else
        {
            if (tableSessionId.HasValue)
            {
                throw new DomainException("Static QR payload must not contain TableSessionId.");
            }

            if (expiresAtUnix.HasValue)
            {
                throw new DomainException("Static QR payload must not contain ExpiresAtUnix.");
            }
        }

        SchemaVersion = schemaVersion;
        KeyId = keyId.Trim();
        Mode = mode;
        TenantId = tenantId;
        BranchId = branchId;
        PublicTableCode = publicTableCode.Trim();
        QrVersion = qrVersion;
        TableSessionId = tableSessionId;
        ExpiresAtUnix = expiresAtUnix;
        Nonce = string.IsNullOrWhiteSpace(nonce) ? null : nonce.Trim();
    }

    public static QrPayload CreateStatic(
        string keyId,
        TenantId tenantId,
        BranchId branchId,
        string publicTableCode,
        int qrVersion)
    {
        return new QrPayload(
            schemaVersion: CurrentSchemaVersion,
            keyId: keyId,
            mode: QrMode.Static,
            tenantId: tenantId,
            branchId: branchId,
            publicTableCode: publicTableCode,
            qrVersion: qrVersion);
    }

    public static QrPayload CreateDynamic(
        string keyId,
        TenantId tenantId,
        BranchId branchId,
        string publicTableCode,
        int qrVersion,
        DiningSessionId tableSessionId,
        long expiresAtUnix,
        string? nonce = null)
    {
        return new QrPayload(
            schemaVersion: CurrentSchemaVersion,
            keyId: keyId,
            mode: QrMode.Dynamic,
            tenantId: tenantId,
            branchId: branchId,
            publicTableCode: publicTableCode,
            qrVersion: qrVersion,
            tableSessionId: tableSessionId,
            expiresAtUnix: expiresAtUnix,
            nonce: nonce);
    }
}
