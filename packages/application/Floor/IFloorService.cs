using RestaurantOrder.Domain.Auth;
using RestaurantOrder.Domain.Branches;
using RestaurantOrder.Domain.Floor;
using RestaurantOrder.Domain.Tenants;

namespace RestaurantOrder.Application.Floor;

/// <summary>
/// Service interface governing floor plan and dining table management operations.
/// Enforces fail-closed multi-tenant RBAC, concurrency verification, and layout consistency.
/// </summary>
public interface IFloorService
{
    Task<IReadOnlyList<RestaurantTableDto>> ListTablesAsync(
        TenantId tenantId,
        BranchId branchId,
        Guid? diningAreaId,
        bool? isActive,
        AuthenticatedPrincipal actor,
        CancellationToken ct = default);

    Task<RestaurantTableDto> GetTableAsync(
        TenantId tenantId,
        BranchId branchId,
        RestaurantTableId tableId,
        AuthenticatedPrincipal actor,
        CancellationToken ct = default);

    Task<RestaurantTableDto> CreateTableAsync(
        TenantId tenantId,
        BranchId branchId,
        CreateTableRequest request,
        AuthenticatedPrincipal actor,
        CancellationToken ct = default);

    Task<RestaurantTableDto> UpdateTableAsync(
        TenantId tenantId,
        BranchId branchId,
        RestaurantTableId tableId,
        UpdateTableRequest request,
        Guid? concurrencyToken,
        AuthenticatedPrincipal actor,
        CancellationToken ct = default);

    Task<RestaurantTableDto> UpdateTableLayoutAsync(
        TenantId tenantId,
        BranchId branchId,
        RestaurantTableId tableId,
        UpdateTableLayoutRequest request,
        Guid? concurrencyToken,
        AuthenticatedPrincipal actor,
        CancellationToken ct = default);

    Task<RestaurantTableDto> ActivateTableAsync(
        TenantId tenantId,
        BranchId branchId,
        RestaurantTableId tableId,
        Guid? concurrencyToken,
        AuthenticatedPrincipal actor,
        CancellationToken ct = default);

    Task<RestaurantTableDto> DeactivateTableAsync(
        TenantId tenantId,
        BranchId branchId,
        RestaurantTableId tableId,
        Guid? concurrencyToken,
        AuthenticatedPrincipal actor,
        CancellationToken ct = default);

    Task<BranchFloorStatusDto> GetBranchFloorStatusAsync(
        TenantId tenantId,
        BranchId branchId,
        AuthenticatedPrincipal actor,
        CancellationToken ct = default);

    Task<DiningSessionDto?> GetActiveSessionForTableAsync(
        TenantId tenantId,
        BranchId branchId,
        RestaurantTableId tableId,
        AuthenticatedPrincipal actor,
        CancellationToken ct = default);

    Task<IReadOnlyList<DiningSessionDto>> ListSessionsForTableAsync(
        TenantId tenantId,
        BranchId branchId,
        RestaurantTableId tableId,
        AuthenticatedPrincipal actor,
        CancellationToken ct = default);

    Task<DiningSessionDto> GetSessionAsync(
        TenantId tenantId,
        BranchId branchId,
        DiningSessionId sessionId,
        AuthenticatedPrincipal actor,
        CancellationToken ct = default);

    Task<DiningSessionDto> OpenSessionAsync(
        TenantId tenantId,
        BranchId branchId,
        RestaurantTableId tableId,
        OpenDiningSessionRequest request,
        AuthenticatedPrincipal actor,
        CancellationToken ct = default);

    Task<DiningSessionDto> ActivateSessionAsync(
        TenantId tenantId,
        BranchId branchId,
        DiningSessionId sessionId,
        Guid? concurrencyToken,
        AuthenticatedPrincipal actor,
        CancellationToken ct = default);

    Task<DiningSessionDto> RequestBillAsync(
        TenantId tenantId,
        BranchId branchId,
        DiningSessionId sessionId,
        Guid? concurrencyToken,
        AuthenticatedPrincipal actor,
        CancellationToken ct = default);

    Task<DiningSessionDto> CloseSessionAsync(
        TenantId tenantId,
        BranchId branchId,
        DiningSessionId sessionId,
        CloseDiningSessionRequest request,
        Guid? concurrencyToken,
        AuthenticatedPrincipal actor,
        CancellationToken ct = default);
}
