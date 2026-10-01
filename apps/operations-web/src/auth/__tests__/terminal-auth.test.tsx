import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { TerminalProvider, useTerminal } from '../TerminalContext';
import { PinPad } from '../PinPad';
import { PinAuthModal } from '../PinAuthModal';
import { TerminalActivationModal } from '../TerminalActivationModal';
import { TerminalContextDto, UserPrincipalDto } from '@restaurant-order/contracts';

const mockTerminal: TerminalContextDto = {
  terminalId: 'term-1',
  tenantId: 'tenant-1',
  branchId: 'branch-1',
  terminalName: 'Garson POS 1',
  deviceIdentifier: 'dev-1',
  isActive: true,
};

const mockStaff: UserPrincipalDto = {
  userId: 'staff-1',
  email: 'waiter@restoran.com',
  role: 'Waiter',
  tenantId: 'tenant-1',
  branchId: 'branch-1',
  securityVersion: 1,
};

describe('Operations Web Terminal & Auth Components', () => {
  beforeEach(() => {
    vi.restoreAllMocks();
    localStorage.clear();
  });

  describe('PinPad Component', () => {
    it('handles digit inputs, clear, delete, and submission upon 4 digits', async () => {
      const onSubmit = vi.fn().mockResolvedValue(undefined);
      const onCancel = vi.fn();

      render(<PinPad onSubmit={onSubmit} onCancel={onCancel} />);

      expect(screen.getByText('Personel PIN Girişi')).toBeDefined();

      // Enter 1, 2, 3
      fireEvent.click(screen.getByRole('button', { name: '1' }));
      fireEvent.click(screen.getByRole('button', { name: '2' }));
      fireEvent.click(screen.getByRole('button', { name: '3' }));

      // Delete last digit (3)
      fireEvent.click(screen.getByRole('button', { name: 'Sil' }));

      // Clear all
      fireEvent.click(screen.getByRole('button', { name: 'Temizle' }));

      // Enter full PIN: 1, 2, 3, 4
      fireEvent.click(screen.getByRole('button', { name: '1' }));
      fireEvent.click(screen.getByRole('button', { name: '2' }));
      fireEvent.click(screen.getByRole('button', { name: '3' }));
      fireEvent.click(screen.getByRole('button', { name: '4' }));

      await waitFor(() => {
        expect(onSubmit).toHaveBeenCalledWith('1234');
      });

      // Cancel button
      fireEvent.click(screen.getByRole('button', { name: 'İptal' }));
      expect(onCancel).toHaveBeenCalled();
    });

    it('displays error message when provided', () => {
      render(<PinPad onSubmit={vi.fn()} errorMessage="Geçersiz PIN" />);
      expect(screen.getByRole('alert')).toBeDefined();
      expect(screen.getByText('Geçersiz PIN')).toBeDefined();
    });
  });

  describe('PinAuthModal Component', () => {
    it('submits PIN and calls onSuccess + onClose', async () => {
      const onClose = vi.fn();
      const onSuccess = vi.fn();

      localStorage.setItem('ro_terminal_id', 'term-1');
      localStorage.setItem('ro_device_secret', 'secret-1');

      const MockProvider = ({ children }: { children: React.ReactNode }) => {
        return (
          <TerminalProvider initialTerminal={mockTerminal}>
            {children}
          </TerminalProvider>
        );
      };

      globalThis.fetch = vi.fn().mockResolvedValue({
        ok: true,
        json: async () => ({ user: mockStaff }),
      }) as unknown as typeof fetch;

      render(
        <MockProvider>
          <PinAuthModal isOpen={true} onClose={onClose} onSuccess={onSuccess} />
        </MockProvider>
      );

      expect(screen.getByText('Personel Hızlı Geçiş')).toBeDefined();

      // Enter 4 digits
      fireEvent.click(screen.getByRole('button', { name: '1' }));
      fireEvent.click(screen.getByRole('button', { name: '2' }));
      fireEvent.click(screen.getByRole('button', { name: '3' }));
      fireEvent.click(screen.getByRole('button', { name: '4' }));

      await waitFor(() => {
        expect(onSuccess).toHaveBeenCalled();
        expect(onClose).toHaveBeenCalled();
      });
    });
  });

  describe('TerminalActivationModal Component', () => {
    it('activates terminal and closes modal', async () => {
      const onClose = vi.fn();

      globalThis.fetch = vi.fn().mockResolvedValue({
        ok: true,
        json: async () => ({
          terminalId: 'term-new',
          tenantId: 'tenant-1',
          branchId: 'branch-1',
          terminalName: 'Kasa 1',
          deviceSecret: 'sec-123',
        }),
      }) as unknown as typeof fetch;

      render(
        <TerminalProvider>
          <TerminalActivationModal isOpen={true} onClose={onClose} />
        </TerminalProvider>
      );

      const codeInput = screen.getByLabelText(/Aktivasyon Kodu/i);
      const nameInput = screen.getByLabelText(/Terminal Adı/i);
      const submitBtn = screen.getByRole('button', { name: 'Terminali Aktifleştir' });

      fireEvent.change(codeInput, { target: { value: 'code-123' } });
      fireEvent.change(nameInput, { target: { value: 'Kasa 1' } });
      fireEvent.click(submitBtn);

      await waitFor(() => {
        expect(globalThis.fetch).toHaveBeenCalledWith(
          '/api/v1/terminals/activate',
          expect.objectContaining({ method: 'POST' })
        );
        expect(onClose).toHaveBeenCalled();
      });
    });
  });

  describe('TerminalContext & Hooks', () => {
    it('throws error when useTerminal is outside TerminalProvider', () => {
      const BadComponent = () => {
        useTerminal();
        return null;
      };
      expect(() => render(<BadComponent />)).toThrow('useTerminal must be used within a TerminalProvider');
    });

    it('rejects loginWithPin when device is not enrolled', async () => {
      const TestComponent = () => {
        const { loginWithPin, error } = useTerminal();
        return (
          <div>
            <button onClick={() => loginWithPin('1234')}>Giriş</button>
            {error && <span role="alert">{error}</span>}
          </div>
        );
      };

      render(
        <TerminalProvider initialTerminal={null}>
          <TestComponent />
        </TerminalProvider>
      );

      fireEvent.click(screen.getByRole('button', { name: 'Giriş' }));

      await waitFor(() => {
        expect(screen.getByRole('alert').textContent).toContain('Cihaz aktif bir terminal olarak kayıtlı değil.');
      });
    });

    it('supports logoutStaff', async () => {
      globalThis.fetch = vi.fn().mockResolvedValue({ ok: true }) as unknown as typeof fetch;

      const TestComponent = () => {
        const { staffUser, logoutStaff } = useTerminal();
        return (
          <div>
            <span>{staffUser ? staffUser.email : 'No staff'}</span>
            <button onClick={logoutStaff}>Vardiya Bitir</button>
          </div>
        );
      };

      render(
        <TerminalProvider initialTerminal={mockTerminal} initialStaff={mockStaff}>
          <TestComponent />
        </TerminalProvider>
      );

      expect(screen.getByText('waiter@restoran.com')).toBeDefined();
      fireEvent.click(screen.getByRole('button', { name: 'Vardiya Bitir' }));

      await waitFor(() => {
        expect(screen.getByText('No staff')).toBeDefined();
      });
    });
  });
});
