import { useEffect, useRef, useState, useCallback } from 'react';
import * as signalR from '@microsoft/signalr';
import type { EventEnvelope, EventsSyncResponse } from '@restaurant-order/contracts';
import type {
  EventMeta,
  RealtimeConnectionStatus,
  RealtimeEventsOptions,
  RealtimeEventsState,
} from './types.js';

function resolveHubUrl(url: string): string {
  if (url.startsWith('http://') || url.startsWith('https://') || url.startsWith('ws://') || url.startsWith('wss://')) {
    return url;
  }
  if (typeof window !== 'undefined' && window.location) {
    const origin = window.location.origin;
    if (origin && origin !== 'null' && !origin.startsWith('about:')) {
      return `${origin}${url.startsWith('/') ? '' : '/'}${url}`;
    }
  }
  return `http://localhost:5000${url.startsWith('/') ? '' : '/'}${url}`;
}

export function useRealtimeEvents({
  hubUrl = '/hubs/events',
  syncUrl = '/api/v1/events/sync',
  enabled = true,
  tenantId,
  branchId,
  sessionId,
  onEvent,
  onSessionClosed,
  onResyncRequired,
}: RealtimeEventsOptions): RealtimeEventsState {
  const [status, setStatus] = useState<RealtimeConnectionStatus>('disconnected');
  const [lastSequenceNumber, setLastSequenceNumber] = useState<number>(0);
  const [resyncRequired, setResyncRequired] = useState<boolean>(false);

  const connectionRef = useRef<signalR.HubConnection | null>(null);
  const processedEventIdsRef = useRef<Set<string>>(new Set());
  const aggregateVersionsRef = useRef<Map<string, number>>(new Map());
  const lastSequenceRef = useRef<number>(0);
  const isDisposedRef = useRef<boolean>(false);
  const currentFetchAbortRef = useRef<AbortController | null>(null);
  const reconnectTimeoutRef = useRef<ReturnType<typeof setTimeout> | null>(null);

  const onEventRef = useRef(onEvent);
  onEventRef.current = onEvent;
  const onSessionClosedRef = useRef(onSessionClosed);
  onSessionClosedRef.current = onSessionClosed;
  const onResyncRequiredRef = useRef(onResyncRequired);
  onResyncRequiredRef.current = onResyncRequired;

  const handleEnvelope = useCallback(
    (envelope: EventEnvelope<unknown>, meta: EventMeta = { isCatchUp: false, isLive: true }) => {
      if (!envelope || !envelope.event_id) return;

      // Deduplicate identical event deliveries
      if (processedEventIdsRef.current.has(envelope.event_id)) {
        return;
      }

      // Bound processed IDs set to prevent memory growth
      if (processedEventIdsRef.current.size > 500) {
        const firstEntries = Array.from(processedEventIdsRef.current).slice(0, 100);
        for (const id of firstEntries) {
          processedEventIdsRef.current.delete(id);
        }
      }
      processedEventIdsRef.current.add(envelope.event_id);

      // Per-aggregate sequence deduplication:
      // Older event for the same aggregate arriving late must not regress state.
      if (envelope.aggregate_id && envelope.sequence_number) {
        const prevSeq = aggregateVersionsRef.current.get(envelope.aggregate_id);
        if (prevSeq !== undefined && envelope.sequence_number <= prevSeq) {
          return;
        }
        aggregateVersionsRef.current.set(envelope.aggregate_id, envelope.sequence_number);

        if (aggregateVersionsRef.current.size > 1000) {
          const keys = Array.from(aggregateVersionsRef.current.keys()).slice(0, 200);
          for (const k of keys) {
            aggregateVersionsRef.current.delete(k);
          }
        }
      }

      // Update monotonic cursor
      if (envelope.sequence_number && envelope.sequence_number > lastSequenceRef.current) {
        lastSequenceRef.current = envelope.sequence_number;
        setLastSequenceNumber(envelope.sequence_number);
      }

      // Continuous authorization: check for session closure
      if (envelope.event_type === 'table.session_closed') {
        const closedSessionId =
          (envelope.data as Record<string, unknown> | undefined)?.dining_session_id ??
          envelope.aggregate_id;

        if (sessionId) {
          // Customer scope: only disconnect if own session closed
          if (closedSessionId === sessionId) {
            setStatus('session_closed');
            onSessionClosedRef.current?.();
            connectionRef.current?.stop().catch(() => {});
            return;
          }
          // Different session closed, ignore or handle as normal event
          onEventRef.current?.(envelope, meta);
          return;
        } else {
          // Staff scope: table session closure is an operational event, never disconnects staff
          onEventRef.current?.(envelope, meta);
          return;
        }
      }

      onEventRef.current?.(envelope, meta);
    },
    [sessionId]
  );

  const catchUpSync = useCallback(async () => {
    if (!syncUrl || isDisposedRef.current) return;

    let hasMore = true;
    let currentSinceSeq = lastSequenceRef.current;
    let pageCount = 0;
    const MAX_PAGES = 50;

    while (hasMore && pageCount < MAX_PAGES && !isDisposedRef.current) {
      pageCount++;
      const abortController = new AbortController();
      currentFetchAbortRef.current = abortController;

      try {
        const url = `${syncUrl}?since_sequence=${currentSinceSeq}&limit=50`;
        const response = await fetch(url, {
          method: 'GET',
          headers: {
            Accept: 'application/json',
          },
          credentials: 'include',
          signal: abortController.signal,
        });

        if (isDisposedRef.current) return;

        if (!response.ok) {
          if (response.status === 401 || response.status === 403) {
            setStatus('session_closed');
            onSessionClosedRef.current?.();
            connectionRef.current?.stop().catch(() => {});
          }
          return;
        }

        if (response.headers.get('X-Resync-Required') === 'true') {
          setResyncRequired(true);
          onResyncRequiredRef.current?.();
        }

        const data = (await response.json()) as EventsSyncResponse;
        if (!data || !Array.isArray(data.events)) {
          break;
        }

        for (const env of data.events) {
          if (isDisposedRef.current) return;
          handleEnvelope(env as EventEnvelope<unknown>, { isCatchUp: true, isLive: false });
        }

        hasMore = Boolean(data.has_more);
        if (data.latest_sequence_number && data.latest_sequence_number > currentSinceSeq) {
          currentSinceSeq = data.latest_sequence_number;
        } else if (data.events.length > 0) {
          const maxSeq = Math.max(...data.events.map((e) => e.sequence_number || 0));
          if (maxSeq > currentSinceSeq) {
            currentSinceSeq = maxSeq;
          } else {
            break;
          }
        } else {
          break;
        }
      } catch (err: unknown) {
        if ((err as { name?: string })?.name === 'AbortError') {
          return;
        }
        break;
      } finally {
        currentFetchAbortRef.current = null;
      }
    }
  }, [syncUrl, handleEnvelope]);

  useEffect(() => {
    // Reset tracking state on session, branch, or tenant changes
    lastSequenceRef.current = 0;
    setLastSequenceNumber(0);
    processedEventIdsRef.current.clear();
    aggregateVersionsRef.current.clear();
    setResyncRequired(false);

    if (!enabled) {
      if (connectionRef.current) {
        connectionRef.current.stop().catch(() => {});
        connectionRef.current = null;
      }
      setStatus('disconnected');
      return;
    }

    isDisposedRef.current = false;
    let isDisposed = false;
    setStatus('connecting');

    let connection: signalR.HubConnection;
    try {
      connection = new signalR.HubConnectionBuilder()
        .withUrl(resolveHubUrl(hubUrl), {
          withCredentials: true,
        })
        .withAutomaticReconnect([1000, 2000, 4000, 8000, 15000])
        .configureLogging(signalR.LogLevel.None)
        .build();
    } catch {
      setStatus('disconnected');
      return;
    }

    connectionRef.current = connection;

    connection.onreconnecting(() => {
      if (!isDisposed) {
        setStatus('reconnecting');
      }
    });

    connection.onreconnected(async () => {
      if (!isDisposed) {
        setStatus('connected');
        await catchUpSync();
      }
    });

    connection.onclose(() => {
      if (!isDisposed) {
        setStatus((prev) => (prev === 'session_closed' ? 'session_closed' : 'disconnected'));
        // Controlled reconnection retry when reconnects are exhausted
        if (reconnectTimeoutRef.current) clearTimeout(reconnectTimeoutRef.current);
        reconnectTimeoutRef.current = setTimeout(() => {
          if (!isDisposed && enabled && connectionRef.current?.state === signalR.HubConnectionState.Disconnected) {
            connect();
          }
        }, 5000);
      }
    });

    connection.on('ReceiveEvent', (envelopeJson: string) => {
      if (isDisposed) return;
      try {
        const envelope = JSON.parse(envelopeJson) as EventEnvelope<unknown>;
        handleEnvelope(envelope, { isCatchUp: false, isLive: true });
      } catch {
        // Discard malformed JSON
      }
    });

    connection.on('SessionClosed', (payload: { session_id?: string }) => {
      if (isDisposed) return;
      if (!sessionId || !payload?.session_id || payload.session_id === sessionId) {
        setStatus('session_closed');
        onSessionClosedRef.current?.();
        connection.stop().catch(() => {});
      }
    });

    connection.on('AccessRevoked', () => {
      if (isDisposed) return;
      setStatus('session_closed');
      onSessionClosedRef.current?.();
      connection.stop().catch(() => {});
    });

    connection.on('TokenExpired', () => {
      if (isDisposed) return;
      setStatus('session_closed');
      onSessionClosedRef.current?.();
      connection.stop().catch(() => {});
    });

    const connect = () => {
      if (isDisposed || !enabled) return;
      connection
        .start()
        .then(async () => {
          if (isDisposed) {
            connection.stop().catch(() => {});
            return;
          }
          setStatus('connected');
          await catchUpSync();
        })
        .catch(() => {
          if (!isDisposed) {
            setStatus('disconnected');
            if (reconnectTimeoutRef.current) clearTimeout(reconnectTimeoutRef.current);
            reconnectTimeoutRef.current = setTimeout(() => {
              if (!isDisposed && enabled) {
                connect();
              }
            }, 5000);
          }
        });
    };

    connect();

    return () => {
      isDisposed = true;
      isDisposedRef.current = true;
      currentFetchAbortRef.current?.abort();
      if (reconnectTimeoutRef.current) {
        clearTimeout(reconnectTimeoutRef.current);
      }
      connection.stop().catch(() => {});
      connectionRef.current = null;
    };
  }, [hubUrl, syncUrl, enabled, tenantId, branchId, sessionId, catchUpSync, handleEnvelope]);

  return {
    status,
    lastSequenceNumber,
    resyncRequired,
    catchUpSync,
  };
}
