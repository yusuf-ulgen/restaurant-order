using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RestaurantOrder.Application.Auth;
using RestaurantOrder.Application.Orders;
using RestaurantOrder.Application.RestaurantConfig;
using RestaurantOrder.Domain.Auth;
using RestaurantOrder.Domain.Branches;
using RestaurantOrder.Domain.Common;
using RestaurantOrder.Domain.Floor;
using RestaurantOrder.Domain.Orders;
using RestaurantOrder.Domain.Tenants;

namespace RestaurantOrder.Infrastructure.Orders;

public sealed partial class StaffOrderService
{
    public async Task<StaffOrderResponseDto> CancelOrderItemAsync(
        TenantId tenantId,
        BranchId branchId,
        DiningSessionId sessionId,
        OrderId orderId,
        OrderItemId itemId,
        CancelOrderItemRequest request,
        Guid? ifMatchConcurrencyToken,
        AuthenticatedPrincipal actor,
        CancellationToken cancellationToken = default)
    {
        ValidateStaffActor(actor, branchId);

        if (request == null || string.IsNullOrWhiteSpace(request.Reason))
        {
            throw new DomainException("Cancellation reason is required.");
        }

        var effectiveToken = ifMatchConcurrencyToken ?? request.ConcurrencyToken;
        if (!effectiveToken.HasValue || effectiveToken.Value == Guid.Empty)
        {
            throw new ConcurrencyPreconditionException("If-Match header or concurrency token is required.");
        }

        await using var tx = await _dbContext.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);

        if (_dbContext.Database.IsNpgsql())
        {
            var lockKey = $"order_session_{sessionId.Value:D}";
            await _dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock(hashtext({lockKey}))",
                cancellationToken);
        }

        var order = await _dbContext.Orders
            .Include(o => o.Items)
                .ThenInclude(i => i.Modifiers)
            .FirstOrDefaultAsync(
                o => o.TenantId == tenantId &&
                     o.BranchId == branchId &&
                     o.DiningSessionId == sessionId &&
                     o.Id == orderId,
                cancellationToken);

        if (order == null)
        {
            throw new ResourceNotFoundException("Order not found.");
        }

        if (order.ConcurrencyToken != effectiveToken.Value)
        {
            throw new ConcurrencyConflictException("The order has been modified by another process. Concurrency conflict detected.");
        }

        var item = order.Items.FirstOrDefault(i => i.Id == itemId)
            ?? throw new ResourceNotFoundException("Order item not found.");

        if (item.Status == OrderItemStatus.Cancelled)
        {
            throw new DomainException("OrderItem is already cancelled.");
        }

        var previousItemStatus = item.Status;
        var previousOrderStatus = order.Status;
        var nowUtc = DateTimeOffset.UtcNow;
        bool isSupervisorVoid = false;
        Guid auditActorId = actor.SubjectId;
        string auditActorRole = actor.Role.ToString();

        if (item.Status == OrderItemStatus.Submitted)
        {
            if (!_permissionRegistry.IsPermitted(actor, Permissions.OrdersItemsCancelPrePrep))
            {
                throw new InvalidAuthorizationScopeException("Missing required permission 'orders.items.cancel_pre_prep'.");
            }

            order.CancelItemPrePrep(itemId, actor.SubjectId, actor.Role.ToString(), request.Reason.Trim(), nowUtc);
        }
        else if (item.Status is OrderItemStatus.InPreparation or OrderItemStatus.Ready or OrderItemStatus.Served)
        {
            // Supervisor void required
            if (_permissionRegistry.IsPermitted(actor, Permissions.OrdersItemsVoidInPrep))
            {
                isSupervisorVoid = true;
                auditActorId = actor.SubjectId;
                auditActorRole = actor.Role.ToString();
            }
            else
            {
                // Verify supervisor PIN authorization
                var (verifiedSupervisorId, verifiedRole) = await VerifySupervisorAsync(
                    tenantId,
                    branchId,
                    request.SupervisorUserId,
                    request.SupervisorPin,
                    cancellationToken);

                isSupervisorVoid = true;
                auditActorId = verifiedSupervisorId;
                auditActorRole = verifiedRole;
            }

            order.VoidItemBySupervisor(itemId, auditActorId, auditActorRole, request.Reason.Trim(), nowUtc);
        }
        else
        {
            throw new DomainException($"Cannot cancel OrderItem in status '{item.Status}'.");
        }

        // Record immutable audit entry with snapshot minor units (WITHOUT customer notes)
        var auditDetails = new
        {
            itemId = item.Id.Value,
            itemName = item.ItemName,
            variantName = item.VariantName,
            quantity = item.Quantity,
            lineTotalMinorUnits = item.LineTotalMinorUnits,
            previousItemStatus = previousItemStatus.ToString(),
            newItemStatus = item.Status.ToString(),
            isSupervisorVoid,
            orderTotalMinorUnits = order.TotalMinorUnits,
            currency = order.Currency.Code
        };

        var audit = OrderAuditEntry.Record(
            tenantId,
            order.Id,
            sessionId,
            auditActorId,
            auditActorRole,
            action: isSupervisorVoid ? "WASTE_CANCEL" : "ItemCancelled",
            previousStatus: previousOrderStatus,
            newStatus: order.Status,
            reason: request.Reason.Trim(),
            detailsJson: JsonSerializer.Serialize(auditDetails),
            nowUtc: nowUtc);

        _dbContext.OrderAuditEntries.Add(audit);

        await _dbContext.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);

        return MapToDto(order);
    }

    private async Task<(Guid SupervisorId, string SupervisorRole)> VerifySupervisorAsync(
        TenantId tenantId,
        BranchId branchId,
        Guid? supervisorUserId,
        string? supervisorPin,
        CancellationToken cancellationToken)
    {
        if (!supervisorUserId.HasValue || string.IsNullOrWhiteSpace(supervisorPin))
        {
            throw new InvalidAuthorizationScopeException("Item is in preparation or ready/served. Supervisor authorization (orders.items.void_in_prep) is required.");
        }

        var supervisorUser = new UserId(supervisorUserId.Value);
        var pinCred = await _dbContext.PinCredentials
            .FirstOrDefaultAsync(p => p.TenantId == tenantId && p.BranchId == branchId && p.UserId == supervisorUser, cancellationToken);

        if (pinCred == null || pinCred.IsLocked(DateTimeOffset.UtcNow))
        {
            throw new InvalidAuthorizationScopeException("Supervisor verification failed: Invalid PIN or locked credential.");
        }

        var verifyResult = _pinHasher.VerifyPin(supervisorPin.Trim(), pinCred.PinHash, pinCred.PepperKeyId);
        if (verifyResult == PinVerificationResult.Failed)
        {
            pinCred.RecordFailedAttempt(5, TimeSpan.FromMinutes(15), DateTimeOffset.UtcNow);
            await _dbContext.SaveChangesAsync(cancellationToken);
            throw new InvalidAuthorizationScopeException("Supervisor verification failed: Incorrect PIN.");
        }

        pinCred.RecordSuccessfulAttempt(DateTimeOffset.UtcNow);

        // Verify supervisor user has manager/admin role
        var membership = await _dbContext.Memberships
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.TenantId == tenantId && m.UserId == supervisorUser && m.IsActive, cancellationToken);

        if (membership == null)
        {
            throw new InvalidAuthorizationScopeException("Supervisor does not have active membership in this tenant.");
        }

        var principal = AuthenticatedPrincipal.CreateStaff(
            supervisorUser.Value,
            membership.Role,
            AuthorizationScope.ForBranch(tenantId, branchId),
            sessionId: Guid.NewGuid(),
            authMethod: AuthenticationMethod.Pin,
            securityVersion: 1);

        if (!_permissionRegistry.IsPermitted(principal, Permissions.OrdersItemsVoidInPrep))
        {
            throw new InvalidAuthorizationScopeException("Specified supervisor does not hold 'orders.items.void_in_prep' authority.");
        }

        return (supervisorUser.Value, membership.Role.ToString());
    }
}
