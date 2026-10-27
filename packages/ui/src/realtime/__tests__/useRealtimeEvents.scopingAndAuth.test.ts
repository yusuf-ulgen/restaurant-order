import { describe, it, expect, vi, beforeEach } from 'vitest';
import { renderHook, act } from '@testing-library/react';
import { useRealtimeEvents } from '../useRealtimeEvents.js';
import type { EventEnvelope } from '@restaurant-order/contracts';

describe('useRealtimeEvents - Scoping, Deduplication, and Authorization', () => {
  beforeEach(() => {
    vi.restoreAllMocks();
  });

  it('prevents stale event for same aggregate from regressing newer state', async () => {
    const processedEvents: EventEnvelope<unknown>[] = [];
    const onEvent = vi.fn((env: EventEnvelope<unknown>) => {
      processedEvents.push(env);
    });

    const page1 = {
      events: [
        {
          event_id: 'ev-new',
          event_type: 'order.item_status_changed',
          aggregate_id: 'order-A',
          sequence_number: 20,
          data: { status: 'Ready' },
        } as EventEnvelope<unknown>,
      ],
      latest_sequence_number: 20,
      has_more: true,
    };

    const page2 = {
      events: [
        {
          event_id: 'ev-stale',
          event_type: 'order.item_status_changed',
          aggregate_id: 'order-A',
          sequence_number: 15,
          data: { status: 'InPreparation' },
        } as EventEnvelope<unknown>,
        {
          event_id: 'ev-other',
          event_type: 'order.item_status_changed',
          aggregate_id: 'order-B',
          sequence_number: 16,
          data: { status: 'InPreparation' },
        } as EventEnvelope<unknown>,
      ],
      latest_sequence_number: 20,
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

    expect(processedEvents).toHaveLength(2);
    expect(processedEvents.map((e) => e.event_id)).toEqual(['ev-new', 'ev-other']);
  });

  it('triggers onSessionClosed when customer own session is closed', async () => {
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
            data: { dining_session_id: 'session-1' },
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
        sessionId: 'session-1',
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

  it('does NOT close session for customer when a different table session is closed', async () => {
    const onSessionClosed = vi.fn();
    const onEvent = vi.fn();

    global.fetch = vi.fn().mockResolvedValue({
      ok: true,
      status: 200,
      headers: new Headers(),
      json: async () => ({
        events: [
          {
            event_id: 'ev-closed-other',
            event_type: 'table.session_closed',
            schema_version: 1,
            occurred_at_utc: new Date().toISOString(),
            tenant_id: 'ten-1',
            branch_id: 'br-1',
            aggregate_id: 'session-other',
            sequence_number: 99,
            data: { dining_session_id: 'session-other' },
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
        sessionId: 'session-mine',
        syncUrl: '/api/v1/events/sync',
        onSessionClosed,
        onEvent,
      })
    );

    await act(async () => {
      await result.current.catchUpSync();
    });

    expect(result.current.status).toBe('disconnected');
    expect(onSessionClosed).not.toHaveBeenCalled();
    expect(onEvent).toHaveBeenCalledTimes(1);
  });

  it('does NOT disconnect staff when table.session_closed occurs', async () => {
    const onSessionClosed = vi.fn();
    const onEvent = vi.fn();

    global.fetch = vi.fn().mockResolvedValue({
      ok: true,
      status: 200,
      headers: new Headers(),
      json: async () => ({
        events: [
          {
            event_id: 'ev-table-close',
            event_type: 'table.session_closed',
            schema_version: 1,
            occurred_at_utc: new Date().toISOString(),
            tenant_id: 'ten-1',
            branch_id: 'br-1',
            aggregate_id: 'table-session-123',
            sequence_number: 55,
            data: { dining_session_id: 'table-session-123' },
          },
        ],
        latest_sequence_number: 55,
        has_more: false,
        synced_at_utc: new Date().toISOString(),
      }),
    });

    const { result } = renderHook(() =>
      useRealtimeEvents({
        enabled: false,
        syncUrl: '/api/v1/events/sync',
        onSessionClosed,
        onEvent,
      })
    );

    await act(async () => {
      await result.current.catchUpSync();
    });

    expect(result.current.status).toBe('disconnected');
    expect(onSessionClosed).not.toHaveBeenCalled();
    expect(onEvent).toHaveBeenCalledTimes(1);
  });

  it('handles live event during catch-up pagination race without losing events or regressing state', async () => {
    const receivedIds: string[] = [];
    const onEvent = vi.fn((env: EventEnvelope<unknown>) => {
      receivedIds.push(env.event_id);
    });

    const page1 = {
      events: [
        {
          event_id: 'p1-initial',
          event_type: 'order.created',
          aggregate_id: 'agg-init',
          sequence_number: 5,
          data: {},
        } as EventEnvelope<unknown>,
      ],
      latest_sequence_number: 5,
      has_more: true,
    };

    const page2 = {
      events: [
        {
          event_id: 'p2-stale-X',
          event_type: 'order.item_status_changed',
          aggregate_id: 'agg-X',
          sequence_number: 10,
          data: { status: 'InPreparation' },
        } as EventEnvelope<unknown>,
        {
          event_id: 'p2-new-Y',
          event_type: 'order.item_status_changed',
          aggregate_id: 'agg-Y',
          sequence_number: 12,
          data: { status: 'Ready' },
        } as EventEnvelope<unknown>,
      ],
      latest_sequence_number: 12,
      has_more: false,
    };

    let fetchCallCount = 0;
    global.fetch = vi.fn().mockImplementation(async () => {
      fetchCallCount++;
      if (fetchCallCount === 1) {
        return {
          ok: true,
          status: 200,
          headers: new Headers(),
          json: async () => page1,
        };
      }
      return {
        ok: true,
        status: 200,
        headers: new Headers(),
        json: async () => page2,
      };
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

    expect(receivedIds).toContain('p1-initial');
    expect(receivedIds).toContain('p2-stale-X');
    expect(receivedIds).toContain('p2-new-Y');
  });
});
