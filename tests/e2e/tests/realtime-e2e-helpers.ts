import * as signalR from '@microsoft/signalr';
import crypto from 'node:crypto';

export interface RealtimeEnvelope {
  event_id: string;
  event_type: string;
  tenant_id: string;
  branch_id: string;
  sequence_number?: number;
  occurred_at_utc: string;
  data: Record<string, any>;
}

export function mintDevCustomerToken(
  tenantId: string,
  branchId: string,
  sessionId: string
): string {
  const secret = process.env.E2E_JWT_SECRET || process.env.JWT_SECRET;
  if (!secret) {
    throw new Error('E2E_JWT_SECRET or JWT_SECRET environment variable is required');
  }
  const header = Buffer.from(JSON.stringify({ alg: 'HS256', typ: 'JWT', kid: 'k1' })).toString('base64url');
  const now = Math.floor(Date.now() / 1000);
  const exp = now + 900;
  const claims = {
    sub: sessionId,
    sid: sessionId,
    jti: crypto.randomUUID().replace(/-/g, ''),
    principal_type: 'customer',
    role: 'Customer',
    auth_method: 'customer_qr_session',
    security_version: '0',
    tenant_id: tenantId,
    branch_id: branchId,
    table_session_id: sessionId,
    iss: 'restaurant-order',
    aud: 'restaurant-order-clients',
    iat: now,
    nbf: now,
    exp: exp,
  };
  const payload = Buffer.from(JSON.stringify(claims)).toString('base64url');
  const signature = crypto.createHmac('sha256', secret).update(`${header}.${payload}`).digest('base64url');
  return `${header}.${payload}.${signature}`;
}

export function createStaffSignalRConnection(
  token: string,
  branchId?: string,
  baseUrl = 'http://127.0.0.1:5000'
): signalR.HubConnection {
  const url = branchId
    ? `${baseUrl}/hubs/events?branchId=${encodeURIComponent(branchId)}`
    : `${baseUrl}/hubs/events`;

  return new signalR.HubConnectionBuilder()
    .withUrl(url, {
      accessTokenFactory: () => token,
      transport: signalR.HttpTransportType.WebSockets | signalR.HttpTransportType.LongPolling,
    })
    .withAutomaticReconnect()
    .configureLogging(signalR.LogLevel.Warning)
    .build();
}

export function createCustomerSignalRConnection(
  token: string,
  baseUrl = 'http://127.0.0.1:5000'
): signalR.HubConnection {
  return new signalR.HubConnectionBuilder()
    .withUrl(`${baseUrl}/hubs/events`, {
      accessTokenFactory: () => token,
      transport: signalR.HttpTransportType.WebSockets | signalR.HttpTransportType.LongPolling,
    })
    .withAutomaticReconnect()
    .configureLogging(signalR.LogLevel.Warning)
    .build();
}

export function waitForEvent(
  connection: signalR.HubConnection,
  predicate: (envelope: RealtimeEnvelope) => boolean,
  timeoutMs = 15000
): Promise<RealtimeEnvelope> {
  return new Promise((resolve, reject) => {
    const timer = setTimeout(() => {
      connection.off('ReceiveEvent', handler);
      reject(new Error(`Timed out waiting for realtime event after ${timeoutMs}ms`));
    }, timeoutMs);

    const handler = (rawJson: string | object) => {
      try {
        const envelope: RealtimeEnvelope =
          typeof rawJson === 'string' ? JSON.parse(rawJson) : (rawJson as RealtimeEnvelope);

        if (predicate(envelope)) {
          clearTimeout(timer);
          connection.off('ReceiveEvent', handler);
          resolve(envelope);
        }
      } catch {
        // Ignore unparseable or irrelevant envelopes
      }
    };

    connection.on('ReceiveEvent', handler);
  });
}

export function waitForEventType(
  connection: signalR.HubConnection,
  eventType: string,
  timeoutMs = 15000
): Promise<RealtimeEnvelope> {
  return waitForEvent(connection, (env) => env.event_type === eventType, timeoutMs);
}
