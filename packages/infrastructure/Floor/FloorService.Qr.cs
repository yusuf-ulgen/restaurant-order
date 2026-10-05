using Microsoft.EntityFrameworkCore;
using RestaurantOrder.Application.Floor;
using RestaurantOrder.Application.RestaurantConfig;
using RestaurantOrder.Domain.Auth;
using RestaurantOrder.Domain.Branches;
using RestaurantOrder.Domain.Common;
using RestaurantOrder.Domain.Floor;
using RestaurantOrder.Domain.Tenants;

namespace RestaurantOrder.Infrastructure.Floor;

public partial class FloorService
{
    public async Task<TableQrCodeDto> GenerateTableStaticQrAsync(
        TenantId tenantId,
        BranchId branchId,
        RestaurantTableId tableId,
        AuthenticatedPrincipal actor,
        CancellationToken ct = default)
    {
        EnsureBranchAccess(tenantId, branchId, actor);

        var table = await _dbContext.RestaurantTables
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.TenantId == tenantId && t.BranchId == branchId && t.Id == tableId, ct)
            ?? throw new ResourceNotFoundException($"Table '{tableId.Value}' was not found.");

        if (!table.IsActive)
        {
            throw new DomainException($"Cannot generate QR code for inactive table '{table.TableNumber}'.");
        }

        var payload = QrPayload.CreateStatic(
            keyId: _qrSecurity.CurrentKeyId,
            tenantId: tenantId,
            branchId: branchId,
            publicTableCode: table.PublicCode,
            qrVersion: table.QrVersion);

        var token = _qrSecurity.GenerateToken(payload);
        var svg = _qrSecurity.GenerateSvg(token);

        return new TableQrCodeDto(
            TableId: table.Id.Value,
            PublicCode: table.PublicCode,
            QrVersion: table.QrVersion,
            Mode: "static",
            Token: token,
            Svg: svg);
    }

    public async Task<SessionDynamicQrDto> GenerateSessionDynamicQrAsync(
        TenantId tenantId,
        BranchId branchId,
        DiningSessionId sessionId,
        AuthenticatedPrincipal actor,
        CancellationToken ct = default)
    {
        EnsureBranchAccess(tenantId, branchId, actor);
        EnsureSessionsManagePermission(actor);

        var session = await _dbContext.DiningSessions
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.TenantId == tenantId && s.BranchId == branchId && s.Id == sessionId, ct)
            ?? throw new ResourceNotFoundException($"Dining session '{sessionId.Value}' was not found.");

        if (session.Status == DiningSessionStatus.Closed)
        {
            throw new DomainException("Cannot generate dynamic QR code for a closed dining session.");
        }

        var table = await _dbContext.RestaurantTables
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.TenantId == tenantId && t.BranchId == branchId && t.Id == session.TableId, ct)
            ?? throw new ResourceNotFoundException($"Table '{session.TableId.Value}' was not found.");

        if (!table.IsActive)
        {
            throw new DomainException($"Cannot generate dynamic QR code for inactive table '{table.TableNumber}'.");
        }

        var now = DateTimeOffset.UtcNow;
        var lifetimeMinutes = _qrOptions.DynamicQrLifetimeMinutes <= 0 ? 30 : _qrOptions.DynamicQrLifetimeMinutes;
        var expiresAt = now.AddMinutes(lifetimeMinutes);

        var payload = QrPayload.CreateDynamic(
            keyId: _qrSecurity.CurrentKeyId,
            tenantId: tenantId,
            branchId: branchId,
            publicTableCode: table.PublicCode,
            qrVersion: table.QrVersion,
            tableSessionId: session.Id,
            expiresAtUnix: expiresAt.ToUnixTimeSeconds());

        var token = _qrSecurity.GenerateToken(payload);
        var svg = _qrSecurity.GenerateSvg(token);

        return new SessionDynamicQrDto(
            SessionId: session.Id.Value,
            TableId: table.Id.Value,
            PublicCode: table.PublicCode,
            Mode: "dynamic",
            Token: token,
            Svg: svg,
            ExpiresAt: expiresAt);
    }

    public async Task<RestaurantTableDto> RotateTableQrVersionAsync(
        TenantId tenantId,
        BranchId branchId,
        RestaurantTableId tableId,
        Guid? concurrencyToken,
        AuthenticatedPrincipal actor,
        CancellationToken ct = default)
    {
        EnsureBranchAccess(tenantId, branchId, actor);
        EnsureTablesManagePermission(actor);

        var table = await _dbContext.RestaurantTables
            .FirstOrDefaultAsync(t => t.TenantId == tenantId && t.BranchId == branchId && t.Id == tableId, ct)
            ?? throw new ResourceNotFoundException($"Table '{tableId.Value}' was not found.");

        VerifyConcurrencyToken(table.ConcurrencyToken, concurrencyToken);

        table.BumpQrVersion();

        await ExecuteInTenantTransactionAsync(tenantId, async () =>
        {
            AddAuditEvent(tenantId, SecurityAuditEventType.TableQrRotated, actor, branchId, new
            {
                tableId = table.Id.Value,
                tableNumber = table.TableNumber,
                newQrVersion = table.QrVersion
            });

            await _dbContext.SaveChangesAsync(ct);
        }, ct);

        return MapTable(table);
    }

    public async Task<TableQrMetadataDto> GetTableQrMetadataAsync(
        TenantId tenantId,
        BranchId branchId,
        RestaurantTableId tableId,
        AuthenticatedPrincipal actor,
        CancellationToken ct = default)
    {
        EnsureBranchAccess(tenantId, branchId, actor);

        var table = await _dbContext.RestaurantTables
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.TenantId == tenantId && t.BranchId == branchId && t.Id == tableId, ct)
            ?? throw new ResourceNotFoundException($"Table '{tableId.Value}' was not found.");

        return new TableQrMetadataDto(
            TableId: table.Id.Value,
            TableNumber: table.TableNumber,
            Name: table.Name,
            PublicCode: table.PublicCode,
            QrVersion: table.QrVersion,
            IsActive: table.IsActive,
            KeyId: _qrSecurity.CurrentKeyId);
    }
}
