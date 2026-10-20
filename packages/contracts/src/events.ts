/**
 * Standard real-time event envelope and typed domain payloads.
 * Conforms to ADR-0004 and docs/REALTIME-AND-NOTIFICATIONS.md with snake_case wire format.
 */

export type RealtimeEventType =
  | 'order.created'
  | 'order.item_status_changed'
  | 'table.service_requested'
  | 'table.service_request_status_changed'
  | 'menu.item_availability_changed'
  | 'table.session_closed'
  | 'catalog.policy_invalidated'
  // Phase 11 forward contract:
  | 'kds.ticket_bumped';

/**
 * Versioned, strongly-typed JSON envelope for all real-time events.
 */
export interface EventEnvelope<T = unknown> {
  event_id: string;
  event_type: RealtimeEventType;
  schema_version: number;
  occurred_at_utc: string;
  tenant_id: string;
  branch_id: string;
  aggregate_id: string;
  sequence_number: number;
  data: T;
}

/**
 * Payload for 'order.created' event.
 * Data-minimized: No customer notes or payment credentials included.
 */
export interface OrderCreatedEventData {
  order_id: string;
  dining_session_id: string;
  table_id: string;
  round_number: number;
  order_source: 'Customer' | 'Staff';
  item_count: number;
  currency: string;
  created_at_utc: string;
}

/**
 * Payload for 'order.item_status_changed' event.
 */
export interface OrderItemStatusChangedEventData {
  order_id: string;
  item_id: string;
  new_status: 'InPreparation' | 'Ready' | 'Served' | 'Cancelled';
  changed_at_utc: string;
}

/**
 * Payload for 'table.service_requested' event.
 */
export interface TableServiceRequestedEventData {
  service_request_id: string;
  table_id: string;
  dining_session_id: string;
  request_type: 'WAITER' | 'WATER' | 'NAPKIN' | 'ASSISTANCE' | 'BILL';
  requested_at_utc: string;
}

/**
 * Payload for 'table.service_request_status_changed' event.
 */
export interface TableServiceRequestStatusChangedEventData {
  service_request_id: string;
  table_id: string;
  dining_session_id: string;
  new_status: 'Open' | 'Cancelled' | 'Acknowledged' | 'Completed';
  updated_at_utc: string;
}

/**
 * Payload for 'menu.item_availability_changed' (Quick 86 / Restocked) event.
 */
export interface MenuItemAvailabilityChangedEventData {
  menu_item_id: string;
  item_variant_id: string | null;
  is_available: boolean;
  reason_code: string | null;
  changed_at_utc: string;
}

/**
 * Payload for 'table.session_closed' event.
 */
export interface TableSessionClosedEventData {
  dining_session_id: string;
  table_id: string;
  closed_at_utc: string;
}

/**
 * Payload for 'catalog.policy_invalidated' event.
 * Emitted when tax, service charge, or catalog version changes invalidate active pricing.
 */
export interface CatalogPolicyInvalidatedEventData {
  policy_fingerprint: string | null;
  reason: string;
  invalidated_at_utc: string;
}

/**
 * Forward contract for Phase 11: 'kds.ticket_bumped'.
 * Strictly scoped for Phase 11 KDS station routing; do NOT emit in Phase 9.
 */
export interface KdsTicketBumpedEventData {
  ticket_id: string;
  table_id: string;
  station: 'Kitchen' | 'Bar';
  bumped_at_utc: string;
}

/**
 * Request DTO for HTTP reconciliation / catch-up sync.
 */
export interface EventsSyncRequest {
  since_sequence_number?: number;
  limit?: number;
}

/**
 * Response DTO for HTTP reconciliation / catch-up sync.
 */
export interface EventsSyncResponse {
  events: EventEnvelope<unknown>[];
  latest_sequence_number: number;
  has_more: boolean;
  synced_at_utc: string;
}

/**
 * Request to register an FCM device / installation ID.
 */
export interface RegisterDeviceRequest {
  installation_id: string;
  platform: 'web' | 'android' | 'ios';
}

/**
 * Response for a device registration.
 */
export interface DeviceRegistrationResponse {
  id: string;
  installation_id: string;
  platform: string;
  registered_at_utc: string;
  expires_at_utc: string;
}
