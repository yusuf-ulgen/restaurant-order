/**
 * Resolves the CSRF token from browser cookies (readable cookie set by server).
 */
export function getCsrfToken(): string | null {
  if (typeof document === 'undefined' || !document.cookie) {
    return null;
  }
  const match = document.cookie.match(/(?:^|;\s*)restaurant_csrf_token=([^;]+)/);
  const token = match?.[1];
  return token ? decodeURIComponent(token) : null;
}

/**
 * Clears client-accessible authentication and CSRF cookies on logout.
 */
export function clearClientCookies(): void {
  if (typeof document === 'undefined') return;
  document.cookie = 'restaurant_csrf_token=; Path=/; Expires=Thu, 01 Jan 1970 00:00:00 GMT; SameSite=Strict';
}

const MUTATION_METHODS = new Set(['POST', 'PUT', 'PATCH', 'DELETE']);

export interface FetchWithCsrfOptions extends RequestInit {
  csrfToken?: string | null;
}

/**
 * Unified, secure API fetch client enforcing:
 * 1. Default `credentials: 'same-origin'` (ensuring HttpOnly auth cookies are reliably transmitted)
 * 2. Automatic attachment of `X-CSRF-Token` header for mutation requests (POST/PUT/PATCH/DELETE)
 * 3. Default JSON Content-Type and Accept headers
 */
export async function fetchWithCsrf(
  input: RequestInfo | URL,
  init?: FetchWithCsrfOptions
): Promise<Response> {
  const method = (init?.method || 'GET').toUpperCase();
  const headers = new Headers(init?.headers || {});

  if (!headers.has('Accept')) {
    headers.set('Accept', 'application/json');
  }

  if (MUTATION_METHODS.has(method)) {
    const token = init?.csrfToken !== undefined ? init.csrfToken : getCsrfToken();
    if (token && !headers.has('X-CSRF-Token')) {
      headers.set('X-CSRF-Token', token);
    }
  }

  if (init?.body && typeof init.body === 'string' && !headers.has('Content-Type')) {
    headers.set('Content-Type', 'application/json');
  }

  const finalInit: RequestInit = {
    ...init,
    method,
    headers,
    credentials: init?.credentials ?? 'same-origin',
  };

  return fetch(input, finalInit);
}
