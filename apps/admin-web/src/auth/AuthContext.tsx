import React, { createContext, useContext, useState, useEffect, useCallback } from 'react';
import {
  UserPrincipalDto,
  SingleFlightRefreshQueue,
  fetchWithCsrf,
  clearClientCookies,
} from '@restaurant-order/contracts';

interface AuthContextValue {
  user: UserPrincipalDto | null;
  isAuthenticated: boolean;
  isLoading: boolean;
  error: string | null;
  login: (email: string, password: string, tenantSlug?: string) => Promise<boolean>;
  logout: () => Promise<void>;
}

const AuthContext = createContext<AuthContextValue | undefined>(undefined);
const refreshQueue = new SingleFlightRefreshQueue();

export const AuthProvider: React.FC<{ children: React.ReactNode; initialUser?: UserPrincipalDto | null }> = ({
  children,
  initialUser = null,
}) => {
  const [user, setUser] = useState<UserPrincipalDto | null>(initialUser);
  const [isLoading, setIsLoading] = useState<boolean>(!initialUser);
  const [error, setError] = useState<string | null>(null);

  const restoreSession = useCallback(async () => {
    try {
      const response = await fetchWithCsrf('/api/v1/auth/me');
      if (response.ok) {
        const data = await response.json();
        setUser(data);
      } else if (response.status === 401) {
        // Try single-flight refresh
        const refreshed = await refreshQueue.executeRefresh(async () => {
          const refRes = await fetchWithCsrf('/api/v1/auth/refresh', { method: 'POST' });
          return refRes.ok;
        });

        if (refreshed) {
          const retryRes = await fetchWithCsrf('/api/v1/auth/me');
          if (retryRes.ok) {
            setUser(await retryRes.json());
          }
        } else {
          setUser(null);
        }
      }
    } catch {
      setUser(null);
    } finally {
      setIsLoading(false);
    }
  }, []);

  useEffect(() => {
    if (!initialUser) {
      restoreSession();
    }
  }, [initialUser, restoreSession]);

  const login = async (email: string, password: string, tenantSlug?: string): Promise<boolean> => {
    setIsLoading(true);
    setError(null);
    try {
      const response = await fetchWithCsrf('/api/v1/auth/login', {
        method: 'POST',
        body: JSON.stringify({ email, password, tenantSlug }),
      });

      if (!response.ok) {
        const data = await response.json().catch(() => ({}));
        setError(data.detail || 'Geçersiz e-posta veya şifre.');
        return false;
      }

      const result = await response.json();
      setUser(result.user);
      return true;
    } catch {
      setError('Bağlantı hatası oluştu. Lütfen tekrar deneyin.');
      return false;
    } finally {
      setIsLoading(false);
    }
  };

  const logout = async (): Promise<void> => {
    try {
      await fetchWithCsrf('/api/v1/auth/logout', { method: 'POST' });
    } finally {
      setUser(null);
      clearClientCookies();
    }
  };

  return (
    <AuthContext.Provider
      value={{
        user,
        isAuthenticated: !!user,
        isLoading,
        error,
        login,
        logout,
      }}
    >
      {children}
    </AuthContext.Provider>
  );
};

export const useAuth = (): AuthContextValue => {
  const context = useContext(AuthContext);
  if (!context) {
    throw new Error('useAuth must be used within an AuthProvider');
  }
  return context;
};
