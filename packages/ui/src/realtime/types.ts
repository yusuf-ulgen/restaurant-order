import type { EventEnvelope } from '@restaurant-order/contracts';

export type RealtimeConnectionStatus =
  | 'connecting'
  | 'connected'
  | 'reconnecting'
  | 'disconnected'
  | 'session_closed';

export interface RealtimeEventsOptions {
  hubUrl?: string;
  syncUrl?: string;
  enabled?: boolean;
  tenantId?: string;
  branchId?: string;
  sessionId?: string;
  onEvent?: (event: EventEnvelope<unknown>) => void;
  onSessionClosed?: () => void;
  onResyncRequired?: () => void;
}

export interface RealtimeEventsState {
  status: RealtimeConnectionStatus;
  lastSequenceNumber: number;
  resyncRequired: boolean;
  catchUpSync: () => Promise<void>;
}
