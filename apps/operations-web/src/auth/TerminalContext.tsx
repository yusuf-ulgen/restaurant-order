import React, { createContext, useContext, useState, useEffect } from 'react';
import {
  TerminalContextDto,
  UserPrincipalDto,
  ActivateTerminalResultDto,
} from '@restaurant-order/contracts';

interface TerminalContextValue {
  terminal: TerminalContextDto | null;
  isEnrolled: boolean;
  staffUser: UserPrincipalDto | null;
  isLoading: boolean;
  error: string | null;
  activateTerminal: (enrollmentCode: string, terminalName: string, deviceIdentifier?: string) => Promise<boolean>;
  loginWithPin: (pin: string, userId?: string, email?: string) => Promise<boolean>;
  logoutStaff: () => Promise<void>;
}

const TerminalContext = createContext<TerminalContextValue | undefined>(undefined);

const STORAGE_KEYS = {
  TERMINAL_ID: 'ro_terminal_id',
  DEVICE_SECRET: 'ro_device_secret',
};

export const TerminalProvider: React.FC<{
  children: React.ReactNode;
  initialTerminal?: TerminalContextDto | null;
  initialStaff?: UserPrincipalDto | null;
}> = ({ children, initialTerminal = null, initialStaff = null }) => {
  const [terminal, setTerminal] = useState<TerminalContextDto | null>(initialTerminal);
  const [staffUser, setStaffUser] = useState<UserPrincipalDto | null>(initialStaff);
  const [isLoading, setIsLoading] = useState<boolean>(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (initialTerminal) return;

    const storedTerminalId = localStorage.getItem(STORAGE_KEYS.TERMINAL_ID);
    const storedSecret = localStorage.getItem(STORAGE_KEYS.DEVICE_SECRET);

    if (storedTerminalId && storedSecret) {
      fetch('/api/v1/terminals/current', {
        headers: {
          'X-Terminal-Id': storedTerminalId,
          'X-Device-Secret': storedSecret,
        },
      })
        .then((res) => (res.ok ? res.json() : null))
        .then((data: TerminalContextDto | null) => {
          if (data && data.isActive) {
            setTerminal(data);
          } else {
            localStorage.removeItem(STORAGE_KEYS.TERMINAL_ID);
            localStorage.removeItem(STORAGE_KEYS.DEVICE_SECRET);
          }
        })
        .catch(() => {});
    }
  }, [initialTerminal]);

  const activateTerminal = async (
    enrollmentCode: string,
    terminalName: string,
    deviceIdentifier?: string
  ): Promise<boolean> => {
    setIsLoading(true);
    setError(null);
    try {
      const response = await fetch('/api/v1/terminals/activate', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
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
      localStorage.setItem(STORAGE_KEYS.TERMINAL_ID, result.terminalId);
      localStorage.setItem(STORAGE_KEYS.DEVICE_SECRET, result.deviceSecret);

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
  };

  const loginWithPin = async (pin: string, userId?: string, email?: string): Promise<boolean> => {
    const storedTerminalId = terminal?.terminalId || localStorage.getItem(STORAGE_KEYS.TERMINAL_ID);
    const storedSecret = localStorage.getItem(STORAGE_KEYS.DEVICE_SECRET);

    if (!storedTerminalId || !storedSecret) {
      setError('Cihaz aktif bir terminal olarak kayıtlı değil.');
      return false;
    }

    setIsLoading(true);
    setError(null);
    try {
      const response = await fetch('/api/v1/auth/pin/login', {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
          'X-Terminal-Id': storedTerminalId,
          'X-Device-Secret': storedSecret,
        },
        body: JSON.stringify({
          terminalId: storedTerminalId,
          deviceSecret: storedSecret,
          userId,
          email,
          pin,
        }),
      });

      if (!response.ok) {
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
  };

  const logoutStaff = async (): Promise<void> => {
    const storedTerminalId = terminal?.terminalId || localStorage.getItem(STORAGE_KEYS.TERMINAL_ID);
    try {
      await fetch('/api/v1/auth/pin/logout', {
        method: 'POST',
        headers: storedTerminalId ? { 'X-Terminal-Id': storedTerminalId } : {},
      });
    } finally {
      setStaffUser(null);
    }
  };

  return (
    <TerminalContext.Provider
      value={{
        terminal,
        isEnrolled: !!terminal,
        staffUser,
        isLoading,
        error,
        activateTerminal,
        loginWithPin,
        logoutStaff,
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
