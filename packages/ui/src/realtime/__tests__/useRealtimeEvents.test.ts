import { describe, it, expect, vi, beforeEach } from 'vitest';
import { renderHook, act } from '@testing-library/react';
import { useRealtimeEvents } from '../useRealtimeEvents.js';
import type { EventEnvelope } from '@restaurant-order/contracts';

describe('useRealtimeEvents - Core Connection and Pagination', () => {
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

  it('performs catch-up sync via HTTP when catchUpSync is invoked and passes isCatchUp meta', async () => {
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
        enabled: false,
        syncUrl: '/api/v1/events/sync',
        onEvent,
      })
    );

    await act(async () => {
      await result.current.catchUpSync();
    });

    expect(onEvent).toHaveBeenCalledTimes(1);
    expect(onEvent).toHaveBeenCalledWith(
      syncResponse.events[0],
      { isCatchUp: true, isLive: false }
    );
    expect(result.current.lastSequenceNumber).toBe(42);

    // Duplicate delivery should not re-invoke onEvent
    await act(async () => {
      await result.current.catchUpSync();
    });

    expect(onEvent).toHaveBeenCalledTimes(1);
  });

  it('consumes all pages when has_more is true across multiple pages', async () => {
    const receivedEvents: EventEnvelope<unknown>[] = [];
    const onEvent = vi.fn((env: EventEnvelope<unknown>) => {
      receivedEvents.push(env);
    });

    const page1 = {
      events: [
        {
          event_id: 'p1-ev1',
          event_type: 'order.created',
          aggregate_id: 'order-1',
          sequence_number: 10,
          data: {},
        } as EventEnvelope<unknown>,
      ],
      latest_sequence_number: 10,
      has_more: true,
    };

    const page2 = {
      events: [
        {
          event_id: 'p2-ev1',
          event_type: 'order.item_status_changed',
          aggregate_id: 'order-1',
          sequence_number: 11,
          data: {},
        } as EventEnvelope<unknown>,
      ],
      latest_sequence_number: 11,
      has_more: false,
    };

    global.fetch = vi
      .fn()
      .mockResolvedValueOnce({
        ok: true,
        status: 200,
        headers: new Headers(),
        json: async () => page1,
      })
      .mockResolvedValueOnce({
        ok: true,
        status: 200,
        headers: new Headers(),
        json: async () => page2,
      });

    const { result } = renderHook(() =>
      useRealtimeEvents({
        enabled: false,
        syncUrl: '/api/v1/events/sync',
        onEvent,
      })
    );

    await act(async () => {
      await result.current.catchUpSync();
    });

    expect(global.fetch).toHaveBeenCalledTimes(2);
    expect(receivedEvents).toHaveLength(2);
    expect(receivedEvents[0]?.event_id).toBe('p1-ev1');
    expect(receivedEvents[1]?.event_id).toBe('p2-ev1');
    expect(result.current.lastSequenceNumber).toBe(11);
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
});
