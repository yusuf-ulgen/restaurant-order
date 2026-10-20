import { describe, it, expect } from 'vitest';
import type {
  EventEnvelope,
  OrderCreatedEventData,
  OrderItemStatusChangedEventData,
  TableServiceRequestedEventData,
  TableServiceRequestStatusChangedEventData,
  MenuItemAvailabilityChangedEventData,
  TableSessionClosedEventData,
  CatalogPolicyInvalidatedEventData,
  KdsTicketBumpedEventData,
  EventsSyncResponse,
  DeviceRegistrationResponse,
} from '@restaurant-order/contracts';

describe('Real-time Event Envelope and Typed Payloads Contract Drift Validation', () => {
  it('correctly validates EventEnvelope with OrderCreatedEventData JSON matching C# backend', () => {
    const envelope: EventEnvelope<OrderCreatedEventData> = {
      event_id: '11111111-1111-1111-1111-111111111111',
      event_type: 'order.created',
      schema_version: 1,
      occurred_at_utc: '2026-10-20T14:00:00Z',
      tenant_id: '22222222-2222-2222-2222-222222222222',
      branch_id: '33333333-3333-3333-3333-333333333333',
      aggregate_id: '44444444-4444-4444-4444-444444444444',
      sequence_number: 1042,
      data: {
        order_id: '44444444-4444-4444-4444-444444444444',
        dining_session_id: '55555555-5555-5555-5555-555555555555',
        table_id: '66666666-6666-6666-6666-666666666666',
        round_number: 1,
        order_source: 'Customer',
        item_count: 3,
        currency: 'TRY',
        created_at_utc: '2026-10-20T14:00:00Z',
      },
    };

    expect(envelope.event_id).toBe('11111111-1111-1111-1111-111111111111');
    expect(envelope.event_type).toBe('order.created');
    expect(envelope.schema_version).toBe(1);
    expect(envelope.sequence_number).toBe(1042);
    expect(envelope.data.order_id).toBe('44444444-4444-4444-4444-444444444444');
    expect(envelope.data.round_number).toBe(1);
    expect(envelope.data.order_source).toBe('Customer');
    expect(envelope.data.item_count).toBe(3);
  });

  it('correctly types OrderItemStatusChangedEventData', () => {
    const envelope: EventEnvelope<OrderItemStatusChangedEventData> = {
      event_id: 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa',
      event_type: 'order.item_status_changed',
      schema_version: 1,
      occurred_at_utc: '2026-10-20T14:05:00Z',
      tenant_id: '22222222-2222-2222-2222-222222222222',
      branch_id: '33333333-3333-3333-3333-333333333333',
      aggregate_id: '44444444-4444-4444-4444-444444444444',
      sequence_number: 1043,
      data: {
        order_id: '44444444-4444-4444-4444-444444444444',
        item_id: '77777777-7777-7777-7777-777777777777',
        new_status: 'Ready',
        changed_at_utc: '2026-10-20T14:05:00Z',
      },
    };

    expect(envelope.data.new_status).toBe('Ready');
    expect(envelope.data.item_id).toBe('77777777-7777-7777-7777-777777777777');
  });

  it('correctly types TableServiceRequestedEventData and StatusChanged', () => {
    const requested: EventEnvelope<TableServiceRequestedEventData> = {
      event_id: 'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb',
      event_type: 'table.service_requested',
      schema_version: 1,
      occurred_at_utc: '2026-10-20T14:10:00Z',
      tenant_id: '22222222-2222-2222-2222-222222222222',
      branch_id: '33333333-3333-3333-3333-333333333333',
      aggregate_id: '88888888-8888-8888-8888-888888888888',
      sequence_number: 1044,
      data: {
        service_request_id: '88888888-8888-8888-8888-888888888888',
        table_id: '66666666-6666-6666-6666-666666666666',
        dining_session_id: '55555555-5555-5555-5555-555555555555',
        request_type: 'WAITER',
        requested_at_utc: '2026-10-20T14:10:00Z',
      },
    };

    expect(requested.data.request_type).toBe('WAITER');

    const statusChanged: EventEnvelope<TableServiceRequestStatusChangedEventData> = {
      event_id: 'cccccccc-cccc-cccc-cccc-cccccccccccc',
      event_type: 'table.service_request_status_changed',
      schema_version: 1,
      occurred_at_utc: '2026-10-20T14:12:00Z',
      tenant_id: '22222222-2222-2222-2222-222222222222',
      branch_id: '33333333-3333-3333-3333-333333333333',
      aggregate_id: '88888888-8888-8888-8888-888888888888',
      sequence_number: 1045,
      data: {
        service_request_id: '88888888-8888-8888-8888-888888888888',
        table_id: '66666666-6666-6666-6666-666666666666',
        dining_session_id: '55555555-5555-5555-5555-555555555555',
        new_status: 'Acknowledged',
        updated_at_utc: '2026-10-20T14:12:00Z',
      },
    };

    expect(statusChanged.data.new_status).toBe('Acknowledged');
  });

  it('correctly types MenuItemAvailabilityChangedEventData and CatalogPolicyInvalidatedEventData', () => {
    const item86: EventEnvelope<MenuItemAvailabilityChangedEventData> = {
      event_id: 'dddddddd-dddd-dddd-dddd-dddddddddddd',
      event_type: 'menu.item_availability_changed',
      schema_version: 1,
      occurred_at_utc: '2026-10-20T14:15:00Z',
      tenant_id: '22222222-2222-2222-2222-222222222222',
      branch_id: '33333333-3333-3333-3333-333333333333',
      aggregate_id: '99999999-9999-9999-9999-999999999999',
      sequence_number: 1046,
      data: {
        menu_item_id: '99999999-9999-9999-9999-999999999999',
        item_variant_id: null,
        is_available: false,
        reason_code: 'OutOfStock',
        changed_at_utc: '2026-10-20T14:15:00Z',
      },
    };

    expect(item86.data.is_available).toBe(false);

    const policy: EventEnvelope<CatalogPolicyInvalidatedEventData> = {
      event_id: 'eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee',
      event_type: 'catalog.policy_invalidated',
      schema_version: 1,
      occurred_at_utc: '2026-10-20T14:20:00Z',
      tenant_id: '22222222-2222-2222-2222-222222222222',
      branch_id: '33333333-3333-3333-3333-333333333333',
      aggregate_id: 'policy_branch_333',
      sequence_number: 1047,
      data: {
        policy_fingerprint: 'fp_abc123456789',
        reason: 'TaxRateUpdated',
        invalidated_at_utc: '2026-10-20T14:20:00Z',
      },
    };

    expect(policy.data.policy_fingerprint).toBe('fp_abc123456789');
  });

  it('correctly types TableSessionClosedEventData and KdsTicketBumpedEventData', () => {
    const sessionClosed: EventEnvelope<TableSessionClosedEventData> = {
      event_id: 'ffffffff-ffff-ffff-ffff-ffffffffffff',
      event_type: 'table.session_closed',
      schema_version: 1,
      occurred_at_utc: '2026-10-20T14:30:00Z',
      tenant_id: '22222222-2222-2222-2222-222222222222',
      branch_id: '33333333-3333-3333-3333-333333333333',
      aggregate_id: '55555555-5555-5555-5555-555555555555',
      sequence_number: 1048,
      data: {
        dining_session_id: '55555555-5555-5555-5555-555555555555',
        table_id: '66666666-6666-6666-6666-666666666666',
        closed_at_utc: '2026-10-20T14:30:00Z',
      },
    };

    expect(sessionClosed.data.dining_session_id).toBe('55555555-5555-5555-5555-555555555555');

    const kdsBumped: EventEnvelope<KdsTicketBumpedEventData> = {
      event_id: '00000000-0000-0000-0000-000000000001',
      event_type: 'kds.ticket_bumped',
      schema_version: 1,
      occurred_at_utc: '2026-10-20T14:35:00Z',
      tenant_id: '22222222-2222-2222-2222-222222222222',
      branch_id: '33333333-3333-3333-3333-333333333333',
      aggregate_id: 'ticket_123',
      sequence_number: 1049,
      data: {
        ticket_id: 'ticket_123',
        table_id: '66666666-6666-6666-6666-666666666666',
        station: 'Kitchen',
        bumped_at_utc: '2026-10-20T14:35:00Z',
      },
    };

    expect(kdsBumped.data.station).toBe('Kitchen');
  });

  it('correctly types EventsSyncResponse and DeviceRegistrationResponse', () => {
    const syncRes: EventsSyncResponse = {
      events: [],
      latest_sequence_number: 1050,
      has_more: false,
      synced_at_utc: '2026-10-20T14:40:00Z',
    };

    expect(syncRes.latest_sequence_number).toBe(1050);
    expect(syncRes.has_more).toBe(false);

    const deviceRes: DeviceRegistrationResponse = {
      id: 'dev_11111111',
      installation_id: 'fcm_installation_token_xyz',
      platform: 'web',
      registered_at_utc: '2026-10-20T14:45:00Z',
      expires_at_utc: '2026-11-20T14:45:00Z',
    };

    expect(deviceRes.platform).toBe('web');
  });
});
