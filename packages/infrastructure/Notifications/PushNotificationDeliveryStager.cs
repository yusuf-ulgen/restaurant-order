using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using RestaurantOrder.Application.Notifications;
using RestaurantOrder.Domain.Branches;
using RestaurantOrder.Domain.Floor;
using RestaurantOrder.Domain.Notifications;
using RestaurantOrder.Domain.Tenants;
using RestaurantOrder.Infrastructure.Persistence;

namespace RestaurantOrder.Infrastructure.Notifications;

public sealed class PushNotificationDeliveryStager : IPushNotificationDeliveryStager
{
    private static readonly TimeSpan EventLifetimeTtl = TimeSpan.FromMinutes(5);

    private readonly RestaurantOrderDbContext _dbContext;
    private readonly ILogger<PushNotificationDeliveryStager> _logger;

    public PushNotificationDeliveryStager(
        RestaurantOrderDbContext dbContext,
        ILogger<PushNotificationDeliveryStager> logger)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<int> StageDeliveriesForEventAsync(
        Guid tenantId,
        Guid branchId,
        Guid eventId,
        string eventType,
        string aggregateId,
        string payloadJson,
        DateTimeOffset occurredAtUtc,
        CancellationToken ct = default)
    {
        if (eventId == Guid.Empty || string.IsNullOrWhiteSpace(eventType))
        {
            return 0;
        }

        var now = DateTimeOffset.UtcNow;
        var tenantIdTyped = new TenantId(tenantId);
        var branchIdTyped = new BranchId(branchId);

        // Parse optional dining_session_id from payload
        DiningSessionId? diningSessionId = null;
        try
        {
            using var doc = JsonDocument.Parse(payloadJson);
            var root = doc.RootElement;
            if (root.TryGetProperty("data", out var dataProp))
            {
                if (dataProp.TryGetProperty("dining_session_id", out var dsProp) && dsProp.TryGetGuid(out var parsedSessionId))
                {
                    diningSessionId = new DiningSessionId(parsedSessionId);
                }
            }
            else if (root.TryGetProperty("dining_session_id", out var directDsProp) && directDsProp.TryGetGuid(out var parsedDirectSessionId))
            {
                diningSessionId = new DiningSessionId(parsedDirectSessionId);
            }
        }
        catch (JsonException)
        {
            _logger.LogWarning("Failed to parse event payload JSON when staging push deliveries for Event {EventId}.", eventId);
        }

        // Fetch candidate active devices for this branch
        var activeDevices = await _dbContext.DeviceRegistrations
            .IgnoreQueryFilters()
            .Where(d => d.TenantId == tenantIdTyped && d.BranchId == branchIdTyped && d.IsActive && d.ExpiresAtUtc > now)
            .ToListAsync(ct);

        if (activeDevices.Count == 0)
        {
            return 0;
        }

        var targetDevices = new List<DeviceRegistration>();

        switch (eventType)
        {
            case "order.created":
                // Send to all active staff devices in branch
                targetDevices.AddRange(activeDevices.Where(d => d.UserId != null));
                break;

            case "order.item_status_changed":
                // Send to customer session device if present
                if (diningSessionId != null)
                {
                    targetDevices.AddRange(activeDevices.Where(d => d.DiningSessionId == diningSessionId));
                }
                // Also send to staff devices (waiters/managers)
                targetDevices.AddRange(activeDevices.Where(d => d.UserId != null));
                break;

            case "table.service_requested":
                // Send to active staff devices
                targetDevices.AddRange(activeDevices.Where(d => d.UserId != null));
                break;

            case "table.service_request_status_changed":
                // Send to customer session device
                if (diningSessionId != null)
                {
                    targetDevices.AddRange(activeDevices.Where(d => d.DiningSessionId == diningSessionId));
                }
                break;

            case "table.session_closed":
                // Send to customer session device
                if (diningSessionId != null)
                {
                    targetDevices.AddRange(activeDevices.Where(d => d.DiningSessionId == diningSessionId));
                }
                break;

            case "menu.item_availability_changed":
                // Send to active staff devices
                targetDevices.AddRange(activeDevices.Where(d => d.UserId != null));
                break;

            default:
                // Other events: no push delivery required
                return 0;
        }

        if (targetDevices.Count == 0)
        {
            return 0;
        }

        // Distinct devices
        var uniqueDevices = targetDevices.GroupBy(d => d.Id).Select(g => g.First()).ToList();

        // Check already staged deliveries for this event
        var existingDeliveryDeviceIds = await _dbContext.NotificationDeliveries
            .IgnoreQueryFilters()
            .Where(d => d.TenantId == tenantIdTyped && d.EventId == eventId)
            .Select(d => d.DeviceRegistrationId)
            .ToListAsync(ct);

        var existingSet = new HashSet<Guid>(existingDeliveryDeviceIds);
        var stagedCount = 0;

        foreach (var device in uniqueDevices)
        {
            if (existingSet.Contains(device.Id))
            {
                continue;
            }

            var recipientType = device.DiningSessionId != null ? "Customer" : "Staff";
            var recipientId = device.DiningSessionId?.Value ?? device.UserId!.Value;

            var delivery = NotificationDelivery.Create(
                tenantId: tenantIdTyped,
                branchId: branchIdTyped,
                eventId: eventId,
                eventType: eventType,
                deviceRegistrationId: device.Id,
                installationId: device.InstallationId,
                recipientType: recipientType,
                recipientId: recipientId,
                nowUtc: now,
                eventTtl: EventLifetimeTtl,
                maxAttempts: 3);

            _dbContext.NotificationDeliveries.Add(delivery);
            stagedCount++;
        }

        if (stagedCount > 0)
        {
            await _dbContext.SaveChangesAsync(ct);
            _logger.LogInformation(
                "Staged {Count} persistent push deliveries for Event {EventId} ({EventType}).",
                stagedCount, eventId, eventType);
        }

        return stagedCount;
    }
}
