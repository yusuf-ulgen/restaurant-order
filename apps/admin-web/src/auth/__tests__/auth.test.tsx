import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { AuthProvider, useAuth } from '../AuthContext';
import { LoginForm } from '../LoginForm';
import { ProtectedRoute } from '../ProtectedRoute';
import { UserPrincipalDto } from '@restaurant-order/contracts';

const mockUser: UserPrincipalDto = {
  userId: 'user-1',
  email: 'admin@restoran.com',
  role: 'RestaurantAdmin',
  tenantId: 'tenant-1',
  securityVersion: 1,
};

describe('Admin Web Auth Components', () => {
  beforeEach(() => {
    vi.restoreAllMocks();
  });

  describe('ProtectedRoute', () => {
    it('renders spinner when loading', () => {
      // AuthProvider without initialUser starts with isLoading=true
      render(
        <AuthProvider>
          <ProtectedRoute>
            <div>Protected Content</div>
          </ProtectedRoute>
        </AuthProvider>
      );

      expect(screen.queryByText('Protected Content')).toBeNull();
    });

    it('renders login form when not authenticated', async () => {
      globalThis.fetch = vi.fn().mockResolvedValue({
        ok: false,
        status: 401,
        json: async () => ({}),
      }) as unknown as typeof fetch;

      render(
        <AuthProvider initialUser={null}>
          <ProtectedRoute>
            <div>Protected Content</div>
          </ProtectedRoute>
        </AuthProvider>
      );

      await waitFor(() => {
        expect(screen.getByText('Restoran Yönetim Girişi')).toBeDefined();
      });
      expect(screen.queryByText('Protected Content')).toBeNull();
    });

    it('renders unauthorized message when role does not match allowedRoles', () => {
      render(
        <AuthProvider initialUser={mockUser}>
          <ProtectedRoute allowedRoles={['SuperAdmin']}>
            <div>Protected Content</div>
          </ProtectedRoute>
        </AuthProvider>
      );

      expect(screen.getByRole('alert')).toBeDefined();
      expect(screen.getByText('Yetkisiz Erişim')).toBeDefined();
      expect(screen.queryByText('Protected Content')).toBeNull();
    });

    it('renders children when authenticated and role matches', () => {
      render(
        <AuthProvider initialUser={mockUser}>
          <ProtectedRoute allowedRoles={['RestaurantAdmin']}>
            <div>Protected Content</div>
          </ProtectedRoute>
        </AuthProvider>
      );

      expect(screen.getByText('Protected Content')).toBeDefined();
    });
  });

  describe('LoginForm', () => {
    it('renders login inputs and submits credentials', async () => {
      globalThis.fetch = vi.fn().mockImplementation((url: string) => {
        if (url === '/api/v1/auth/me') {
          return Promise.resolve({
            ok: false,
            status: 401,
            json: async () => ({}),
          });
        }
        if (url === '/api/v1/auth/refresh') {
          return Promise.resolve({ ok: false, status: 401 });
        }
        if (url === '/api/v1/auth/login') {
          return Promise.resolve({
            ok: true,
            json: async () => ({ user: mockUser }),
          });
        }
        return Promise.resolve({ ok: false });
      }) as unknown as typeof fetch;

      render(
        <AuthProvider>
          <LoginForm />
        </AuthProvider>
      );

      const submitBtn = await screen.findByRole('button', { name: 'Giriş Yap' });
      const emailInput = screen.getByLabelText(/E-Posta Adresi/i);
      const passwordInput = screen.getByLabelText(/Şifre/i);
      const tenantInput = screen.getByLabelText(/Restoran Kodu/i);

      fireEvent.change(emailInput, { target: { value: 'user@example.com' } });
      fireEvent.change(passwordInput, { target: { value: 'secret123' } });
      fireEvent.change(tenantInput, { target: { value: 'kadikoy' } });

      fireEvent.click(submitBtn);

      await waitFor(() => {
        expect(globalThis.fetch).toHaveBeenCalledWith(
          '/api/v1/auth/login',
          expect.objectContaining({
            method: 'POST',
            body: JSON.stringify({ email: 'user@example.com', password: 'secret123', tenantSlug: 'kadikoy' }),
          })
        );
      });
    });

    it('handles login failure and network error', async () => {
      globalThis.fetch = vi.fn().mockImplementation((url: string) => {
        if (url === '/api/v1/auth/me') {
          return Promise.resolve({
            ok: false,
            status: 401,
            json: async () => ({}),
          });
        }
        if (url === '/api/v1/auth/refresh') {
          return Promise.resolve({ ok: false, status: 401 });
        }
        if (url === '/api/v1/auth/login') {
          return Promise.resolve({
            ok: false,
            json: async () => ({ detail: 'Invalid credentials' }),
          });
        }
        return Promise.resolve({ ok: false });
      }) as unknown as typeof fetch;

      render(
        <AuthProvider>
          <LoginForm />
        </AuthProvider>
      );

      const submitBtn = await screen.findByRole('button', { name: 'Giriş Yap' });
      const emailInput = screen.getByLabelText(/E-Posta Adresi/i);
      const passwordInput = screen.getByLabelText(/Şifre/i);

      fireEvent.change(emailInput, { target: { value: 'wrong@example.com' } });
      fireEvent.change(passwordInput, { target: { value: 'wrong' } });
      fireEvent.click(submitBtn);

      await waitFor(() => {
        expect(screen.getByRole('alert')).toBeDefined();
        expect(screen.getByText('Invalid credentials')).toBeDefined();
      });
    });
  });

  describe('AuthContext and Session Restoration', () => {
    it('throws error when useAuth is outside AuthProvider', () => {
      const BadComponent = () => {
        useAuth();
        return null;
      };
      expect(() => render(<BadComponent />)).toThrow('useAuth must be used within an AuthProvider');
    });

    it('restores session when /api/v1/auth/me succeeds', async () => {
      globalThis.fetch = vi.fn().mockResolvedValue({
        ok: true,
        json: async () => mockUser,
      }) as unknown as typeof fetch;

      const StatusComponent = () => {
        const { user, isAuthenticated, logout } = useAuth();
        return (
          <div>
            <span>{isAuthenticated ? `Logged in as ${user?.email}` : 'Not logged in'}</span>
            <button onClick={logout}>Çıkış</button>
          </div>
        );
      };

      render(
        <AuthProvider>
          <StatusComponent />
        </AuthProvider>
      );

      await waitFor(() => {
        expect(screen.getByText('Logged in as admin@restoran.com')).toBeDefined();
      });

      // Test logout
      globalThis.fetch = vi.fn().mockResolvedValue({ ok: true }) as unknown as typeof fetch;
      fireEvent.click(screen.getByRole('button', { name: 'Çıkış' }));

      await waitFor(() => {
        expect(screen.getByText('Not logged in')).toBeDefined();
      });
    });
  });
});
