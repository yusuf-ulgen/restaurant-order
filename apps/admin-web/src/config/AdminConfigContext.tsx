import React, { createContext, useContext, useState, useEffect, useCallback, useRef } from 'react';
import {
  EffectiveTenantTheme,
  applyTenantTheme,
  clearTenantTheme,
  mapEffectiveThemeToOverrides,
} from '@restaurant-order/ui';
import { fetchWithCsrf } from '@restaurant-order/contracts';
import { useAuth } from '../auth/AuthContext';

export interface BranchOption {
  id: string;
  brandId: string;
  name: string;
  slug: string;
  isActive: boolean;
  status: string;
}

export interface AdminConfigContextValue {
  theme: EffectiveTenantTheme | null;
  branches: BranchOption[];
  selectedBranchId: string | null;
  selectedBranch: BranchOption | null;
  isLoading: boolean;
  error: string | null;
  isFallback: boolean;
  selectBranch: (branchId: string) => Promise<void>;
  reload: () => Promise<void>;
  retry: () => Promise<void>;
  updateThemeState: (newTheme: Partial<EffectiveTenantTheme>) => void;
}

const AdminConfigContext = createContext<AdminConfigContextValue | undefined>(undefined);

export function createDefaultFallbackTheme(tenantId?: string | null, branchId?: string | null): EffectiveTenantTheme {
  return {
    tenantId: tenantId || 'default-tenant',
    brandId: 'default-brand',
    branchId: branchId || 'default-branch',
    brandDisplayName: 'Restoran Yönetim',
    branchDisplayName: 'Merkez Şube',
    logoUrl: null,
    faviconUrl: null,
    primaryColor: '#111827',
    primaryHoverColor: '#1f2937',
    secondaryColor: '#4b5563',
    accentColor: '#2563eb',
    surfaceColor: '#ffffff',
    backgroundColor: '#f9fafb',
    shellTitle: 'Yönetim Paneli',
    shellSubtitle: 'Organizasyon Genel Bakışı',
    footerText: 'Admin Kontrol Paneli',
    footerBranchInfo: null,
    hasBranchOverride: false,
    navigationConfigJson: null,
    navigationOverrides: null,
  };
}

export const AdminConfigProvider: React.FC<{
  children: React.ReactNode;
  initialTheme?: EffectiveTenantTheme | null;
  initialBranches?: BranchOption[];
  initialBranchId?: string | null;
}> = ({
  children,
  initialTheme = null,
  initialBranches = [],
  initialBranchId = null,
}) => {
  const { user, isAuthenticated } = useAuth();

  const [theme, setTheme] = useState<EffectiveTenantTheme | null>(initialTheme);
  const [branches, setBranches] = useState<BranchOption[]>(initialBranches);
  const [selectedBranchId, setSelectedBranchId] = useState<string | null>(
    initialBranchId || user?.branchId || null
  );
  const [isLoading, setIsLoading] = useState<boolean>(!initialTheme && isAuthenticated);
  const [error, setError] = useState<string | null>(null);
  const [isFallback, setIsFallback] = useState<boolean>(false);

  // Track previous tenant/branch to detect changes and clean up tokens
  const prevTenantIdRef = useRef<string | null | undefined>(user?.tenantId);
  const prevBranchIdRef = useRef<string | null>(selectedBranchId);

  const applyThemeTokens = useCallback((themeToApply: EffectiveTenantTheme | null) => {
    if (!themeToApply) {
      clearTenantTheme();
      return;
    }
    try {
      const overrides = mapEffectiveThemeToOverrides(themeToApply);
      applyTenantTheme(overrides);
    } catch {
      // Ignore CSS variable assignment failure in test/unsupported environments
    }
  }, []);

  const loadConfig = useCallback(async (branchIdToLoad?: string | null) => {
    if (!isAuthenticated || !user?.tenantId) {
      setTheme(null);
      setBranches([]);
      setIsLoading(false);
      clearTenantTheme();
      return;
    }

    setIsLoading(true);
    setError(null);
    setIsFallback(false);

    try {
      // 1. Fetch branch list
      let branchList: BranchOption[] = [];
      const branchRes = await fetchWithCsrf('/api/v1/restaurant-config/branches');
      if (branchRes.ok) {
        const data = await branchRes.json();
        branchList = Array.isArray(data) ? data : [];
        setBranches(branchList);
      }

      // Determine active branch ID
      const targetBranchId =
        branchIdToLoad ||
        selectedBranchId ||
        user.branchId ||
        (branchList.length > 0 ? branchList[0]?.id || null : null);

      if (targetBranchId && targetBranchId !== selectedBranchId) {
        setSelectedBranchId(targetBranchId);
      }

      // 2. Fetch effective theme for branch if branch exists
      if (targetBranchId) {
        const themeRes = await fetchWithCsrf(
          `/api/v1/restaurant-config/branches/${targetBranchId}/theme`
        );
        if (themeRes.ok) {
          const themeData: EffectiveTenantTheme = await themeRes.json();
          // Parse navigationOverrides from navigationConfigJson if present
          if (themeData.navigationConfigJson && !themeData.navigationOverrides) {
            try {
              themeData.navigationOverrides = JSON.parse(themeData.navigationConfigJson);
            } catch {
              themeData.navigationOverrides = null;
            }
          }
          setTheme(themeData);
          applyThemeTokens(themeData);
          setIsLoading(false);
          return;
        }
      }

      // 3. Fallback to default theme if no branch or branch theme endpoint not 200
      const defaultTheme = createDefaultFallbackTheme(user.tenantId, targetBranchId);
      setTheme(defaultTheme);
      applyThemeTokens(defaultTheme);
      setIsFallback(true);
    } catch {
      // Fallback to safe default theme on network/API failure
      const fallbackTheme = createDefaultFallbackTheme(user?.tenantId ?? null, selectedBranchId);
      setTheme(fallbackTheme);
      applyThemeTokens(fallbackTheme);
      setIsFallback(true);
      setError('Yapılandırma ayarları sunucudan alınamadı. Güvenli varsayılan tema etkinleştirildi.');
    } finally {
      setIsLoading(false);
    }
  }, [isAuthenticated, user?.tenantId, user?.branchId, selectedBranchId, applyThemeTokens]);

  // Initial load when user authenticates
  useEffect(() => {
    if (isAuthenticated) {
      if (!initialTheme) {
        loadConfig(selectedBranchId);
      } else {
        applyThemeTokens(initialTheme);
      }
    } else {
      // Clean up on logout
      setTheme(null);
      setBranches([]);
      setSelectedBranchId(null);
      setIsFallback(false);
      clearTenantTheme();
    }
  }, [isAuthenticated, user?.tenantId, initialTheme, loadConfig, applyThemeTokens, selectedBranchId]);

  // Clean up and reload when tenant changes
  useEffect(() => {
    if (prevTenantIdRef.current && prevTenantIdRef.current !== user?.tenantId) {
      clearTenantTheme();
      setSelectedBranchId(null);
      loadConfig(null);
    }
    prevTenantIdRef.current = user?.tenantId;
  }, [user?.tenantId, loadConfig]);

  // Clean up and re-apply when branch changes
  useEffect(() => {
    if (prevBranchIdRef.current && prevBranchIdRef.current !== selectedBranchId) {
      clearTenantTheme();
      loadConfig(selectedBranchId);
    }
    prevBranchIdRef.current = selectedBranchId;
  }, [selectedBranchId, loadConfig]);

  const selectBranch = async (branchId: string) => {
    if (branchId === selectedBranchId) return;
    clearTenantTheme();
    setSelectedBranchId(branchId);
    await loadConfig(branchId);
  };

  const reload = async () => {
    await loadConfig(selectedBranchId);
  };

  const retry = async () => {
    setError(null);
    await loadConfig(selectedBranchId);
  };

  const updateThemeState = (newTheme: Partial<EffectiveTenantTheme>) => {
    setTheme((prev) => {
      const merged = prev ? { ...prev, ...newTheme } : (newTheme as EffectiveTenantTheme);
      applyThemeTokens(merged);
      return merged;
    });
  };

  const selectedBranch = Array.isArray(branches)
    ? branches.find((b) => b.id === selectedBranchId) || null
    : null;

  return (
    <AdminConfigContext.Provider
      value={{
        theme,
        branches,
        selectedBranchId,
        selectedBranch,
        isLoading,
        error,
        isFallback,
        selectBranch,
        reload,
        retry,
        updateThemeState,
      }}
    >
      {children}
    </AdminConfigContext.Provider>
  );
};

export const useAdminConfig = (): AdminConfigContextValue => {
  const context = useContext(AdminConfigContext);
  if (!context) {
    const defaultTheme = createDefaultFallbackTheme();
    return {
      theme: defaultTheme,
      branches: [],
      selectedBranchId: null,
      selectedBranch: null,
      isLoading: false,
      error: null,
      isFallback: true,
      selectBranch: async () => {},
      reload: async () => {},
      retry: async () => {},
      updateThemeState: () => {},
    };
  }
  return context;
};
