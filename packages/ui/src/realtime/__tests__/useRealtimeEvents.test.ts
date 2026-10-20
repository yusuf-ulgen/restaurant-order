import { describe, it, expect, vi, beforeEach } from 'vitest';
import { renderHook, act } from '@testing-library/react';
import { useRealtimeEvents } from '../useRealtimeEvents.js';
import type { EventEnvelope } from '@restaurant-order/contracts';

describe('useRealtimeEvents', () => {
  beforeEach(() => {
    vi.restoreAllMocks();
  });

  it('sets disconnected status when disabled', () => {
    const { result } = renderHook(() =>
      useRealtimeEvents({
        enabled: false,
      })
    );

    expect(result.current.status).toBe('disconnected');
    expect(result.current.lastSequenceNumber).toBe(0);
  });

  it('performs catch-up sync via HTTP when catchUpSync is invoked', async () => {
    const onEvent = vi.fn();
    const syncResponse = {
      events: [
        {
          event_id: 'ev-1',
          event_type: 'menu.item_availability_changed',
          schema_version: 1,
          occurred_at_utc: new Date().toISOString(),
          tenant_id: 'ten-1',
          branch_id: 'br-1',
          aggregate_id: 'agg-1',
          sequence_number: 42,
          data: { is_available: false },
        } as EventEnvelope<unknown>,
      ],
      latest_sequence_number: 42,
      has_more: false,
      synced_at_utc: new Date().toISOString(),
    };

    global.fetch = vi.fn().mockResolvedValue({
      ok: true,
      status: 200,
      headers: new Headers(),
      json: async () => syncResponse,
    });

    const { result } = renderHook(() =>
      useRealtimeEvents({
        enabled: false, // Don't establish SignalR connection in unit test, test sync manually
        syncUrl: '/api/v1/events/sync',
        onEvent,
      })
    );

    await act(async () => {
      await result.current.catchUpSync();
    });

    expect(onEvent).toHaveBeenCalledTimes(1);
    expect(result.current.lastSequenceNumber).toBe(42);

    // Calling again with duplicate event must NOT invoke onEvent a second time
    await act(async () => {
      await result.current.catchUpSync();
    });

    expect(onEvent).toHaveBeenCalledTimes(1);
  });

  it('detects X-Resync-Required header and notifies caller', async () => {
    const onResyncRequired = vi.fn();

    const headers = new Headers();
    headers.set('X-Resync-Required', 'true');

    global.fetch = vi.fn().mockResolvedValue({
      ok: true,
      status: 200,
      headers,
      json: async () => ({
        events: [],
        latest_sequence_number: 100,
        has_more: false,
        synced_at_utc: new Date().toISOString(),
      }),
    });

    const { result } = renderHook(() =>
      useRealtimeEvents({
        enabled: false,
        syncUrl: '/api/v1/events/sync',
        onResyncRequired,
      })
    );

    await act(async () => {
      await result.current.catchUpSync();
    });

    expect(result.current.resyncRequired).toBe(true);
    expect(onResyncRequired).toHaveBeenCalledTimes(1);
  });

  it('triggers onSessionClosed and sets status on session closed event during sync', async () => {
    const onSessionClosed = vi.fn();

    global.fetch = vi.fn().mockResolvedValue({
      ok: true,
      status: 200,
      headers: new Headers(),
      json: async () => ({
        events: [
          {
            event_id: 'ev-closed',
            event_type: 'table.session_closed',
            schema_version: 1,
            occurred_at_utc: new Date().toISOString(),
            tenant_id: 'ten-1',
            branch_id: 'br-1',
            aggregate_id: 'session-1',
            sequence_number: 99,
            data: {},
          },
        ],
        latest_sequence_number: 99,
        has_more: false,
        synced_at_utc: new Date().toISOString(),
      }),
    });

    const { result } = renderHook(() =>
      useRealtimeEvents({
        enabled: false,
        syncUrl: '/api/v1/events/sync',
        onSessionClosed,
      })
    );

    await act(async () => {
      await result.current.catchUpSync();
    });

    expect(result.current.status).toBe('session_closed');
    expect(onSessionClosed).toHaveBeenCalledTimes(1);
  });
});
