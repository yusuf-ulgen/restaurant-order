import React, { createContext, useContext, useState, useEffect, useCallback, useRef } from 'react';
import type { CustomerSessionDto } from '@restaurant-order/contracts';
import {
  customerSessionApi,
  CustomerSessionApiError,
  setInMemorySession,
  clearInMemorySession,
} from '../api/customerSessionApi';

export type CustomerSessionStatus =
  | 'bootstrapping'
  | 'unauthenticated'
  | 'active'
  | 'expired'
  | 'loading'
  | 'error';

export interface CustomerSessionErrorState {
  type: string;
  message: string;
  retryAfterSeconds?: number;
}

export interface CustomerSessionContextValue {
  status: CustomerSessionStatus;
  session: CustomerSessionDto | null;
  error: CustomerSessionErrorState | null;
  setSession: (session: CustomerSessionDto | null) => void;
  clearSession: () => void;
  resetToUnauthenticated: () => void;
  refreshSession: () => Promise<CustomerSessionDto | null>;
  retry: () => void;
  registerCleanup: (callback: () => void) => () => void;
}

const CustomerSessionContext = createContext<CustomerSessionContextValue | null>(null);

export interface CustomerSessionProviderProps {
  children: React.ReactNode;
  initialSession?: CustomerSessionDto | null;
  skipBootstrap?: boolean;
}

export const CustomerSessionProvider: React.FC<CustomerSessionProviderProps> = ({
  children,
  initialSession = null,
  skipBootstrap = false,
}) => {
  const [session, setSessionInternal] = useState<CustomerSessionDto | null>(initialSession);
  const [status, setStatus] = useState<CustomerSessionStatus>(
    initialSession
      ? initialSession.sessionStatus === 'Closed'
        ? 'expired'
        : 'active'
      : skipBootstrap
      ? 'unauthenticated'
      : 'bootstrapping'
  );
  const [error, setError] = useState<CustomerSessionErrorState | null>(null);

  const cleanupCallbacksRef = useRef<Set<() => void>>(new Set());
  const previousSessionIdRef = useRef<string | null>(initialSession?.sessionId ?? null);

  const registerCleanup = useCallback((callback: () => void) => {
    cleanupCallbacksRef.current.add(callback);
    return () => {
      cleanupCallbacksRef.current.delete(callback);
    };
  }, []);

  const triggerCleanups = useCallback(() => {
    cleanupCallbacksRef.current.forEach((cb) => {
      try {
        cb();
      } catch {
        // Suppress cleanup callback errors to preserve stability
      }
    });
  }, []);

  const clearSession = useCallback(() => {
    clearInMemorySession();
    setSessionInternal(null);
    setStatus('expired');
    setError(null);
    triggerCleanups();
  }, [triggerCleanups]);

  const resetToUnauthenticated = useCallback(() => {
    clearInMemorySession();
    setSessionInternal(null);
    setStatus('unauthenticated');
    setError(null);
    triggerCleanups();
  }, [triggerCleanups]);

  const setSession = useCallback((newSession: CustomerSessionDto | null) => {
    if (newSession) {
      setInMemorySession(newSession);
      setSessionInternal(newSession);
      setStatus('active');
      setError(null);
      previousSessionIdRef.current = newSession.sessionId;
    } else {
      clearSession();
    }
  }, [clearSession]);

  const refreshSession = useCallback(async (): Promise<CustomerSessionDto | null> => {
    setStatus((prev) => (prev === 'active' ? 'loading' : prev));
    setError(null);

    try {
      const recovered = await customerSessionApi.getCurrentSession();
      if (recovered) {
        setInMemorySession(recovered);
        setSessionInternal(recovered);
        setStatus('active');
        previousSessionIdRef.current = recovered.sessionId;
        return recovered;
      } else {
        // No valid session cookie found or session expired
        if (session != null) {
          // It was previously active, now expired
          clearInMemorySession();
          setSessionInternal(null);
          setStatus('expired');
          triggerCleanups();
        } else {
          clearInMemorySession();
          setSessionInternal(null);
          setStatus('unauthenticated');
        }
        return null;
      }
    } catch (err: unknown) {
      if (err instanceof CustomerSessionApiError) {
        if (err.errorType === 'session_expired') {
          clearInMemorySession();
          setSessionInternal(null);
          setStatus('expired');
          triggerCleanups();
          return null;
        }
        setError({
          type: err.errorType,
          message: err.message,
          retryAfterSeconds: err.retryAfterSeconds,
        });
      } else {
        setError({
          type: 'generic',
          message: 'Oturum durumu kontrol edilirken bir hata oluştu.',
        });
      }
      setStatus('error');
      return null;
    }
  }, [session, triggerCleanups]);

  const retry = useCallback(() => {
    refreshSession();
  }, [refreshSession]);

  useEffect(() => {
    if (skipBootstrap || initialSession) {
      return;
    }

    let isMounted = true;
    customerSessionApi.getCurrentSession()
      .then((rec) => {
        if (!isMounted) return;
        if (rec) {
          setSessionInternal(rec);
          setStatus('active');
          previousSessionIdRef.current = rec.sessionId;
        } else {
          setStatus('unauthenticated');
        }
      })
      .catch((err: unknown) => {
        if (!isMounted) return;
        if (err instanceof CustomerSessionApiError) {
          if (err.errorType === 'session_expired') {
            clearInMemorySession();
            setSessionInternal(null);
            setStatus('expired');
            triggerCleanups();
            return;
          }
          setError({
            type: err.errorType,
            message: err.message,
            retryAfterSeconds: err.retryAfterSeconds,
          });
        } else {
          setError({
            type: 'generic',
            message: 'Oturum durumu kontrol edilirken bir hata oluştu.',
          });
        }
        setStatus('error');
      });

    return () => {
      isMounted = false;
    };
  }, [skipBootstrap, initialSession, triggerCleanups]);

  const value: CustomerSessionContextValue = {
    status,
    session,
    error,
    setSession,
    clearSession,
    resetToUnauthenticated,
    refreshSession,
    retry,
    registerCleanup,
  };

  return (
    <CustomerSessionContext.Provider value={value}>
      {children}
    </CustomerSessionContext.Provider>
  );
};

export const useCustomerSession = (): CustomerSessionContextValue => {
  const context = useContext(CustomerSessionContext);
  if (!context) {
    throw new Error('useCustomerSession must be used within a CustomerSessionProvider');
  }
  return context;
};

/**
 * Hook to automatically clear session-dependent state (such as cart, draft selections, or guest notes)
 * when the dining session expires or transitions to a different session.
 */
export const useSessionStateCleanup = (cleanupFn: () => void): void => {
  const { registerCleanup, status } = useCustomerSession();
  const cleanupFnRef = useRef(cleanupFn);
  cleanupFnRef.current = cleanupFn;

  useEffect(() => {
    return registerCleanup(() => {
      cleanupFnRef.current();
    });
  }, [registerCleanup]);

  useEffect(() => {
    if (status === 'expired') {
      cleanupFnRef.current();
    }
  }, [status]);
};
