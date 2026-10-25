using System.Text.Json;
using RestaurantOrder.Application.Events;
using Xunit;

namespace RestaurantOrder.UnitTests.Events;

/// <summary>
/// Verifies bidirectional contract alignment between C# EventEnvelope / EventPayloads
/// and @restaurant-order/contracts definitions without drift.
/// Ensures all properties serialize to exact snake_case JSON format per ADR-0004.
/// </summary>
public class EventContractDriftTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public void EventEnvelope_SerializesToExpectedSnakeCaseStructure()
    {
        var eventId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var tenantId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var branchId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var occurredAt = new DateTimeOffset(2026, 10, 20, 14, 0, 0, TimeSpan.Zero);

        var data = new OrderCreatedEventData(
            OrderId: Guid.Parse("44444444-4444-4444-4444-444444444444"),
            DiningSessionId: Guid.Parse("55555555-5555-5555-5555-555555555555"),
            TableId: Guid.Parse("66666666-6666-6666-6666-666666666666"),
            RoundNumber: 1,
            OrderSource: "Customer",
            ItemCount: 3,
            Currency: "TRY",
            CreatedAtUtc: occurredAt
        );

        var envelope = new EventEnvelope<OrderCreatedEventData>(
            EventId: eventId,
            EventType: RealtimeEventTypes.OrderCreated,
            SchemaVersion: 1,
            OccurredAtUtc: occurredAt,
            TenantId: tenantId,
            BranchId: branchId,
            AggregateId: "44444444-4444-4444-4444-444444444444",
            SequenceNumber: 1042,
            Data: data
        );

        var json = JsonSerializer.Serialize(envelope, JsonOptions);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        // Envelope properties (snake_case)
        Assert.Equal("11111111-1111-1111-1111-111111111111", root.GetProperty("event_id").GetString());
        Assert.Equal("order.created", root.GetProperty("event_type").GetString());
        Assert.Equal(1, root.GetProperty("schema_version").GetInt32());
        Assert.Equal("22222222-2222-2222-2222-222222222222", root.GetProperty("tenant_id").GetString());
        Assert.Equal("33333333-3333-3333-3333-333333333333", root.GetProperty("branch_id").GetString());
        Assert.Equal("44444444-4444-4444-4444-444444444444", root.GetProperty("aggregate_id").GetString());
        Assert.Equal(1042, root.GetProperty("sequence_number").GetInt64());

        // Data payload properties (snake_case)
        var payload = root.GetProperty("data");
        Assert.Equal("44444444-4444-4444-4444-444444444444", payload.GetProperty("order_id").GetString());
        Assert.Equal("55555555-5555-5555-5555-555555555555", payload.GetProperty("dining_session_id").GetString());
        Assert.Equal("66666666-6666-6666-6666-666666666666", payload.GetProperty("table_id").GetString());
        Assert.Equal(1, payload.GetProperty("round_number").GetInt32());
        Assert.Equal("Customer", payload.GetProperty("order_source").GetString());
        Assert.Equal(3, payload.GetProperty("item_count").GetInt32());
        Assert.Equal("TRY", payload.GetProperty("currency").GetString());

        // Round-trip deserialization
        var deserialized = JsonSerializer.Deserialize<EventEnvelope<OrderCreatedEventData>>(json, JsonOptions);
        Assert.NotNull(deserialized);
        Assert.Equal(envelope.EventId, deserialized.EventId);
        Assert.Equal(envelope.Data.OrderId, deserialized.Data.OrderId);
        Assert.Equal(envelope.SequenceNumber, deserialized.SequenceNumber);
    }

    [Fact]
    public void OrderItemStatusChangedEventData_SerializesCorrectly()
    {
        var data = new OrderItemStatusChangedEventData(
            OrderId: Guid.Parse("11111111-1111-1111-1111-111111111111"),
            DiningSessionId: Guid.Parse("33333333-3333-3333-3333-333333333333"),
            ItemId: Guid.Parse("22222222-2222-2222-2222-222222222222"),
            NewStatus: "Ready",
            ChangedAtUtc: DateTimeOffset.UtcNow
        );

        var json = JsonSerializer.Serialize(data, JsonOptions);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal("11111111-1111-1111-1111-111111111111", root.GetProperty("order_id").GetString());
        Assert.Equal("33333333-3333-3333-3333-333333333333", root.GetProperty("dining_session_id").GetString());
        Assert.Equal("22222222-2222-2222-2222-222222222222", root.GetProperty("item_id").GetString());
        Assert.Equal("Ready", root.GetProperty("new_status").GetString());
        Assert.True(root.TryGetProperty("changed_at_utc", out _));
    }

    [Fact]
    public void TableServiceRequestEvents_SerializeCorrectly()
    {
        var requested = new TableServiceRequestedEventData(
            ServiceRequestId: Guid.Parse("11111111-1111-1111-1111-111111111111"),
            TableId: Guid.Parse("22222222-2222-2222-2222-222222222222"),
            DiningSessionId: Guid.Parse("33333333-3333-3333-3333-333333333333"),
            RequestType: "WAITER",
            RequestedAtUtc: DateTimeOffset.UtcNow
        );

        var jsonReq = JsonSerializer.Serialize(requested, JsonOptions);
        using var docReq = JsonDocument.Parse(jsonReq);
        Assert.Equal("WAITER", docReq.RootElement.GetProperty("request_type").GetString());

        var statusChanged = new TableServiceRequestStatusChangedEventData(
            ServiceRequestId: Guid.Parse("11111111-1111-1111-1111-111111111111"),
            TableId: Guid.Parse("22222222-2222-2222-2222-222222222222"),
            DiningSessionId: Guid.Parse("33333333-3333-3333-3333-333333333333"),
            NewStatus: "Acknowledged",
            UpdatedAtUtc: DateTimeOffset.UtcNow
        );

        var jsonStatus = JsonSerializer.Serialize(statusChanged, JsonOptions);
        using var docStatus = JsonDocument.Parse(jsonStatus);
        Assert.Equal("Acknowledged", docStatus.RootElement.GetProperty("new_status").GetString());
    }

    [Fact]
    public void MenuItemAvailabilityAndPolicyEvents_SerializeCorrectly()
    {
        var avail = new MenuItemAvailabilityChangedEventData(
            MenuItemId: Guid.Parse("11111111-1111-1111-1111-111111111111"),
            ItemVariantId: null,
            IsAvailable: false,
            ReasonCode: "OutOfStock",
            ChangedAtUtc: DateTimeOffset.UtcNow
        );

        var jsonAvail = JsonSerializer.Serialize(avail, JsonOptions);
        using var docAvail = JsonDocument.Parse(jsonAvail);
        Assert.False(docAvail.RootElement.GetProperty("is_available").GetBoolean());
        Assert.Equal("OutOfStock", docAvail.RootElement.GetProperty("reason_code").GetString());

        var policy = new CatalogPolicyInvalidatedEventData(
            PolicyFingerprint: "fp_abc123456789",
            Reason: "TaxRateChanged",
            InvalidatedAtUtc: DateTimeOffset.UtcNow
        );

        var jsonPolicy = JsonSerializer.Serialize(policy, JsonOptions);
        using var docPolicy = JsonDocument.Parse(jsonPolicy);
        Assert.Equal("fp_abc123456789", docPolicy.RootElement.GetProperty("policy_fingerprint").GetString());
        Assert.Equal("TaxRateChanged", docPolicy.RootElement.GetProperty("reason").GetString());
    }

    [Fact]
    public void TableSessionClosedAndKdsEvents_SerializeCorrectly()
    {
        var sessionClosed = new TableSessionClosedEventData(
            DiningSessionId: Guid.Parse("11111111-1111-1111-1111-111111111111"),
            TableId: Guid.Parse("22222222-2222-2222-2222-222222222222"),
            ClosedAtUtc: DateTimeOffset.UtcNow
        );

        var jsonSession = JsonSerializer.Serialize(sessionClosed, JsonOptions);
        using var docSession = JsonDocument.Parse(jsonSession);
        Assert.Equal("11111111-1111-1111-1111-111111111111", docSession.RootElement.GetProperty("dining_session_id").GetString());

        var kds = new KdsTicketBumpedEventData(
            TicketId: Guid.Parse("33333333-3333-3333-3333-333333333333"),
            TableId: Guid.Parse("22222222-2222-2222-2222-222222222222"),
            Station: "Kitchen",
            BumpedAtUtc: DateTimeOffset.UtcNow
        );

        var jsonKds = JsonSerializer.Serialize(kds, JsonOptions);
        using var docKds = JsonDocument.Parse(jsonKds);
        Assert.Equal("Kitchen", docKds.RootElement.GetProperty("station").GetString());
    }

    [Fact]
    public void SyncAndDeviceRegistrationDtos_SerializeCorrectly()
    {
        var syncResponse = new EventsSyncResponse(
            Events: new List<EventEnvelope<object>>(),
            LatestSequenceNumber: 50,
            HasMore: false,
            SyncedAtUtc: DateTimeOffset.UtcNow
        );

        var jsonSync = JsonSerializer.Serialize(syncResponse, JsonOptions);
        using var docSync = JsonDocument.Parse(jsonSync);
        Assert.Equal(50, docSync.RootElement.GetProperty("latest_sequence_number").GetInt64());
        Assert.False(docSync.RootElement.GetProperty("has_more").GetBoolean());

        var deviceReg = new DeviceRegistrationResponse(
            Id: Guid.Parse("11111111-1111-1111-1111-111111111111"),
            InstallationId: "inst_test_abc123",
            Platform: "web",
            RegisteredAtUtc: DateTimeOffset.UtcNow,
            ExpiresAtUtc: DateTimeOffset.UtcNow.AddDays(30)
        );

        var jsonDevice = JsonSerializer.Serialize(deviceReg, JsonOptions);
        using var docDevice = JsonDocument.Parse(jsonDevice);
        Assert.Equal("inst_test_abc123", docDevice.RootElement.GetProperty("installation_id").GetString());
        Assert.Equal("web", docDevice.RootElement.GetProperty("platform").GetString());
    }
}
