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
}
