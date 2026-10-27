import { act, renderHook } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { useRealtimeEvents } from '../useRealtimeEvents.js';

const hub = vi.hoisted(() => ({ handlers: new Map<string, (value: string) => void>() }));
vi.mock('@microsoft/signalr', () => ({
  LogLevel: { None: 0 }, HubConnectionState: { Disconnected: 'Disconnected' },
  NullLogger: { instance: {} }, DefaultHttpClient: class { send = vi.fn(); },
  HubConnectionBuilder: class {
    withUrl() { return this; }
    withAutomaticReconnect() { return this; }
    configureLogging() { return this; }
    build() { return {
      on: (key: string, handler: (value: string) => void) => hub.handlers.set(key, handler),
      onreconnecting: vi.fn(), onreconnected: vi.fn(), onclose: vi.fn(),
      start: async () => {}, stop: async () => {},
    }; }
  },
}));
const response = (cursor: number, events: unknown[] = [], more = false) => ({
  ok: true, status: 200, headers: new Headers(),
  json: async () => ({ events, latest_sequence_number: cursor, has_more: more }),
});
const event = (seq: number, aggregate: string) => ({
  event_id: `event-${seq}`, event_type: 'order.created', aggregate_id: aggregate, sequence_number: seq, data: {},
});

describe('HTTP cursor recovery', () => {
  beforeEach(() => { hub.handlers.clear(); vi.restoreAllMocks(); });

  it('does not let live 102 skip missing 101 from another aggregate', async () => {
    const fetchMock = vi.fn().mockResolvedValueOnce(response(0)).mockResolvedValueOnce(response(102, [event(101, 'A'), event(102, 'B')]));
    vi.stubGlobal('fetch', fetchMock);
    const onEvent = vi.fn();
    const { result } = renderHook(() => useRealtimeEvents({ onEvent }));
    await act(async () => {});
    act(() => hub.handlers.get('ReceiveEvent')!(JSON.stringify(event(102, 'B'))));
    await act(async () => { await result.current.catchUpSync(); });
    expect(fetchMock.mock.calls[1]?.[0]).toContain('since_sequence=0');
    expect(onEvent.mock.calls.map(call => call[0].event_id)).toEqual(['event-102', 'event-101']);
  });

  it('persists a trusted scan cursor even when authorization filters every event', async () => {
    const fetchMock = vi.fn().mockResolvedValue(response(200));
    vi.stubGlobal('fetch', fetchMock);
    const { result } = renderHook(() => useRealtimeEvents({ enabled: false }));
    await act(async () => { await result.current.catchUpSync(); });
    await act(async () => { await result.current.catchUpSync(); });
    expect(fetchMock.mock.calls[1]?.[0]).toContain('since_sequence=200');
  });

  it('discards the previous scope response when branch changes during awaited full resync', async () => {
    let finishSnapshot!: () => void;
    const snapshot = vi.fn(() => new Promise<void>(resolve => { finishSnapshot = resolve; }));
    const old = response(900, [event(900, 'old-branch')]);
    old.headers.set('X-Resync-Required', 'true');
    const fetchMock = vi.fn().mockResolvedValueOnce(old).mockResolvedValue(response(0));
    vi.stubGlobal('fetch', fetchMock);
    const onEvent = vi.fn();
    const { result, rerender } = renderHook(({ branchId }) => useRealtimeEvents({ branchId, onEvent, onResyncRequired: snapshot }),
      { initialProps: { branchId: 'old' } });
    await act(async () => {});
    expect(snapshot).toHaveBeenCalledOnce();
    rerender({ branchId: 'new' });
    await act(async () => { finishSnapshot(); });
    expect(onEvent).not.toHaveBeenCalled();
    await act(async () => { await result.current.catchUpSync(); });
    expect(fetchMock.mock.calls.at(-1)?.[0]).toContain('since_sequence=0');
  });
});
