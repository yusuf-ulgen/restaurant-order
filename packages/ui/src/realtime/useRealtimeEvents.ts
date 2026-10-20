import { useEffect, useRef, useState, useCallback } from 'react';
import * as signalR from '@microsoft/signalr';
import type { EventEnvelope, EventsSyncResponse } from '@restaurant-order/contracts';
import type { RealtimeConnectionStatus, RealtimeEventsOptions, RealtimeEventsState } from './types.js';

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
  onEvent,
  onSessionClosed,
  onResyncRequired,
}: RealtimeEventsOptions): RealtimeEventsState {
  const [status, setStatus] = useState<RealtimeConnectionStatus>('disconnected');
  const [lastSequenceNumber, setLastSequenceNumber] = useState<number>(0);
  const [resyncRequired, setResyncRequired] = useState<boolean>(false);

  const connectionRef = useRef<signalR.HubConnection | null>(null);
  const processedEventIdsRef = useRef<Set<string>>(new Set());
  const lastSequenceRef = useRef<number>(0);
  const onEventRef = useRef(onEvent);
  onEventRef.current = onEvent;
  const onSessionClosedRef = useRef(onSessionClosed);
  onSessionClosedRef.current = onSessionClosed;
  const onResyncRequiredRef = useRef(onResyncRequired);
  onResyncRequiredRef.current = onResyncRequired;

  const handleEnvelope = useCallback((envelope: EventEnvelope<unknown>) => {
    if (!envelope || !envelope.event_id) return;

    // Deduplicate against duplicate delivery
    if (processedEventIdsRef.current.has(envelope.event_id)) {
      return;
    }

    // Keep set bounded
    if (processedEventIdsRef.current.size > 500) {
      const firstEntries = Array.from(processedEventIdsRef.current).slice(0, 100);
      for (const id of firstEntries) {
        processedEventIdsRef.current.delete(id);
      }
    }
    processedEventIdsRef.current.add(envelope.event_id);

    // Update monotonic sequence
    if (envelope.sequence_number && envelope.sequence_number > lastSequenceRef.current) {
      lastSequenceRef.current = envelope.sequence_number;
      setLastSequenceNumber(envelope.sequence_number);
    }

    // Continuous authorization: check for session closure
    if (envelope.event_type === 'table.session_closed') {
      setStatus('session_closed');
      onSessionClosedRef.current?.();
      connectionRef.current?.stop().catch(() => {});
      return;
    }

    onEventRef.current?.(envelope);
  }, []);

  const catchUpSync = useCallback(async () => {
    if (!syncUrl) return;

    try {
      const sinceSeq = lastSequenceRef.current;
      const url = `${syncUrl}?since_sequence=${sinceSeq}&limit=50`;
      const response = await fetch(url, {
        method: 'GET',
        headers: {
          Accept: 'application/json',
        },
        credentials: 'include',
      });

      if (!response.ok) {
        if (response.status === 401 || response.status === 403) {
          setStatus('session_closed');
          onSessionClosedRef.current?.();
        }
        return;
      }

      if (response.headers.get('X-Resync-Required') === 'true') {
        setResyncRequired(true);
        onResyncRequiredRef.current?.();
      }

      const data = (await response.json()) as EventsSyncResponse;
      if (data && Array.isArray(data.events)) {
        for (const env of data.events) {
          handleEnvelope(env as EventEnvelope<unknown>);
        }
      }
    } catch {
      // Reconnect/sync retry handled gracefully
    }
  }, [syncUrl, handleEnvelope]);

  useEffect(() => {
    if (!enabled) {
      if (connectionRef.current) {
        connectionRef.current.stop().catch(() => {});
        connectionRef.current = null;
      }
      setStatus('disconnected');
      return;
    }

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
      }
    });

    connection.on('ReceiveEvent', (envelopeJson: string) => {
      if (isDisposed) return;
      try {
        const envelope = JSON.parse(envelopeJson) as EventEnvelope<unknown>;
        handleEnvelope(envelope);
      } catch {
        // Discard malformed JSON
      }
    });

    connection
      .start()
      .then(async () => {
        if (isDisposed) {
          connection.stop().catch(() => {});
          return;
        }
        setStatus('connected');
        // Initial catch up sync to prevent missing events during connection negotiation
        await catchUpSync();
      })
      .catch(() => {
        if (!isDisposed) {
          setStatus('disconnected');
        }
      });

    return () => {
      isDisposed = true;
      connection.stop().catch(() => {});
      connectionRef.current = null;
    };
  }, [hubUrl, enabled, catchUpSync, handleEnvelope]);

  return {
    status,
    lastSequenceNumber,
    resyncRequired,
    catchUpSync,
  };
}
