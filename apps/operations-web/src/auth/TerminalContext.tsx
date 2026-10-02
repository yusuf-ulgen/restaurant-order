import React, { createContext, useContext, useState, useEffect, useCallback } from 'react';
import {
  TerminalContextDto,
  UserPrincipalDto,
  ActivateTerminalResultDto,
  fetchWithCsrf,
  clearClientCookies,
} from '@restaurant-order/contracts';

export type TerminalState = 'UNENROLLED' | 'LOCKED' | 'AUTHENTICATED';

interface TerminalContextValue {
  terminal: TerminalContextDto | null;
  state: TerminalState;
  isEnrolled: boolean;
  staffUser: UserPrincipalDto | null;
  isLoading: boolean;
  error: string | null;
  activateTerminal: (enrollmentCode: string, terminalName: string, deviceIdentifier?: string) => Promise<boolean>;
  loginWithPin: (pin: string, userId?: string, email?: string) => Promise<boolean>;
  logoutStaff: () => Promise<void>;
  deactivateTerminal: () => Promise<void>;
}

const TerminalContext = createContext<TerminalContextValue | undefined>(undefined);

const LEGACY_STORAGE_KEYS = [
  'ro_device_secret',
  'device_secret',
  'ro_terminal_secret',
  'deviceSecret',
];

function scrubLegacyStorage(): void {
  try {
    for (const key of LEGACY_STORAGE_KEYS) {
      localStorage.removeItem(key);
      sessionStorage.removeItem(key);
    }
  } catch {
    // Fail silently if browser storage access is restricted
  }
}

export const TerminalProvider: React.FC<{
  children: React.ReactNode;
  initialTerminal?: TerminalContextDto | null;
  initialStaff?: UserPrincipalDto | null;
}> = ({ children, initialTerminal, initialStaff = null }) => {
  const [terminal, setTerminal] = useState<TerminalContextDto | null>(initialTerminal ?? null);
  const [staffUser, setStaffUser] = useState<UserPrincipalDto | null>(initialStaff);
  const [isLoading, setIsLoading] = useState<boolean>(false);
  const [error, setError] = useState<string | null>(null);

  // Determine current lifecycle state
  const state: TerminalState = !terminal
    ? 'UNENROLLED'
    : !staffUser
      ? 'LOCKED'
      : 'AUTHENTICATED';

  useEffect(() => {
    // Scrub any legacy plain secrets from browser storage on mount
    scrubLegacyStorage();

    if (initialTerminal !== undefined) return;

    // Probe backend for active terminal credentials via HttpOnly cookie
    fetchWithCsrf('/api/v1/terminals/current')
      .then((res) => (res.ok ? res.json() : null))
      .then((data: TerminalContextDto | null) => {
        if (data && data.isActive) {
          setTerminal(data);
        } else {
          setTerminal(null);
          setStaffUser(null);
        }
      })
      .catch(() => {
        setTerminal(null);
      });
  }, [initialTerminal]);

  const activateTerminal = useCallback(async (
    enrollmentCode: string,
    terminalName: string,
    deviceIdentifier?: string
  ): Promise<boolean> => {
    setIsLoading(true);
    setError(null);
    try {
      const response = await fetchWithCsrf('/api/v1/terminals/activate', {
        method: 'POST',
        body: JSON.stringify({
          enrollmentCode,
          terminalName,
          deviceIdentifier: deviceIdentifier || `dev-${window.navigator.userAgent.slice(0, 20)}`,
        }),
      });

      if (!response.ok) {
        const data = await response.json().catch(() => ({}));
        setError(data.detail || 'Geçersiz veya süresi dolmuş aktivasyon kodu.');
        return false;
      }

      const result: ActivateTerminalResultDto = await response.json();

      // Ensure zero secrets ever touch localStorage or sessionStorage
      scrubLegacyStorage();

      setTerminal({
        terminalId: result.terminalId,
        tenantId: result.tenantId,
        branchId: result.branchId,
        terminalName: result.terminalName,
        deviceIdentifier: deviceIdentifier || 'device',
        isActive: true,
      });

      return true;
    } catch {
      setError('Aktivasyon sırasında bağlantı hatası oluştu.');
      return false;
    } finally {
      setIsLoading(false);
    }
  }, []);

  const loginWithPin = useCallback(async (pin: string, userId?: string, email?: string): Promise<boolean> => {
    if (!terminal) {
      setError('Cihaz aktif bir terminal olarak kayıtlı değil.');
      return false;
    }

    setIsLoading(true);
    setError(null);
    try {
      const response = await fetchWithCsrf('/api/v1/auth/pin/login', {
        method: 'POST',
        body: JSON.stringify({
          terminalId: terminal.terminalId,
          userId,
          email,
          pin,
        }),
      });

      if (!response.ok) {
        if (response.status === 401) {
          // Check if terminal itself was revoked
          const verifyRes = await fetchWithCsrf('/api/v1/terminals/current').catch(() => null);
          if (verifyRes && !verifyRes.ok) {
            setTerminal(null);
            setStaffUser(null);
            setError('Terminal yetkisi sonlandırılmış. Yeniden aktivasyon gerekli.');
            return false;
          }
        }

        const data = await response.json().catch(() => ({}));
        setError(data.detail || 'Geçersiz PIN veya personel yetkisi yok.');
        return false;
      }

      const result = await response.json();
      setStaffUser(result.user);
      return true;
    } catch {
      setError('PIN girişi sırasında bağlantı hatası oluştu.');
      return false;
    } finally {
      setIsLoading(false);
    }
  }, [terminal]);

  const logoutStaff = useCallback(async (): Promise<void> => {
    try {
      await fetchWithCsrf('/api/v1/auth/pin/logout', {
        method: 'POST',
      });
    } catch {
      // Ignore network errors on logout
    } finally {
      // Note: do not clear client cookies here; terminal credentials and terminal CSRF cookie are preserved
      setStaffUser(null);
    }
  }, []);

  const deactivateTerminal = useCallback(async (): Promise<void> => {
    try {
      await fetchWithCsrf('/api/v1/terminals/deactivate', {
        method: 'POST',
      });
    } catch {
      // Ignore network errors
    } finally {
      clearClientCookies();
      scrubLegacyStorage();
      setTerminal(null);
      setStaffUser(null);
    }
  }, []);

  return (
    <TerminalContext.Provider
      value={{
        terminal,
        state,
        isEnrolled: !!terminal,
        staffUser,
        isLoading,
        error,
        activateTerminal,
        loginWithPin,
        logoutStaff,
        deactivateTerminal,
      }}
    >
      {children}
    </TerminalContext.Provider>
  );
};

export const useTerminal = (): TerminalContextValue => {
  const context = useContext(TerminalContext);
  if (!context) {
    throw new Error('useTerminal must be used within a TerminalProvider');
  }
  return context;
};
