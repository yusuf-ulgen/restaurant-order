import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { App } from './App';
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

describe('Operations Web App - 5-Stage Authentication State Machine', () => {
  beforeEach(() => {
    vi.restoreAllMocks();
    localStorage.clear();
    sessionStorage.clear();
  });

  describe('Stage 1: UNENROLLED State', () => {
    it('renders only TerminalActivationView when device is not enrolled', () => {
      render(<App initialTerminal={null} initialStaff={null} />);

      // Activation screen elements MUST be present
      expect(screen.getByRole('heading', { level: 1, name: 'Cihaz Aktivasyonu' })).toBeDefined();
      expect(screen.getByLabelText(/Aktivasyon Kodu/i)).toBeDefined();
      expect(screen.getByLabelText(/Terminal Adı/i)).toBeDefined();
      expect(screen.getByRole('button', { name: 'Terminali Aktifleştir' })).toBeDefined();

      // Operational elements MUST NOT exist in DOM
      expect(screen.queryByText('Garson & Operasyon')).toBeNull();
      expect(screen.queryByText('Masa Yönetimi')).toBeNull();
      expect(screen.queryByText('Çağrılar (0)')).toBeNull();
      expect(screen.queryByRole('banner')).toBeNull();
    });
  });

  describe('Stage 2: LOCKED State', () => {
    it('renders only PinPadView when terminal is enrolled but staff is not logged in', () => {
      render(<App initialTerminal={mockTerminal} initialStaff={null} />);

      // PIN pad elements MUST be present
      expect(screen.getByText('Terminal Kilitli')).toBeDefined();
      expect(screen.getByText('Garson POS 1')).toBeDefined();
      expect(screen.getByText('Personel PIN Girişi')).toBeDefined();
      expect(screen.getByRole('button', { name: 'Farklı Terminal / Cihazı Sıfırla' })).toBeDefined();

      // Operational elements MUST NOT exist in DOM
      expect(screen.queryByText('Garson & Operasyon')).toBeNull();
      expect(screen.queryByText('Masa Yönetimi')).toBeNull();
      expect(screen.queryByText('Çağrılar (0)')).toBeNull();
      expect(screen.queryByRole('banner')).toBeNull();
    });
  });

  describe('Stage 3: AUTHENTICATED State', () => {
    it('renders full operations shell and table management when staff is authenticated', () => {
      render(<App initialTerminal={mockTerminal} initialStaff={mockStaff} />);

      // Operations shell MUST be present
      expect(screen.getByRole('heading', { level: 1, name: 'Garson & Operasyon' })).toBeDefined();
      expect(screen.getByText('Waiter')).toBeDefined();
      expect(screen.getByText('Garson POS 1')).toBeDefined();
      expect(screen.getByText('Masa Yönetimi')).toBeDefined();
      expect(screen.getByRole('button', { name: /Masa Planı/i })).toBeDefined();
      expect(screen.getByRole('button', { name: 'Çağrılar (0)' })).toBeDefined();
      expect(screen.getByRole('button', { name: 'Kilitle' })).toBeDefined();
    });

    it('renders empty state when there are no active tables', () => {
      render(
        <App
          initialTerminal={mockTerminal}
          initialStaff={mockStaff}
          hasActiveTables={false}
        />
      );

      expect(screen.getByRole('status')).toBeDefined();
      expect(screen.getByText('Aktif Masa Bulunmuyor')).toBeDefined();
    });

    it('opens and closes active calls Modal when Çağrılar button is clicked', () => {
      render(<App initialTerminal={mockTerminal} initialStaff={mockStaff} />);

      const callsBtn = screen.getByRole('button', { name: 'Çağrılar (0)' });
      fireEvent.click(callsBtn);

      const dialog = screen.getByRole('dialog');
      expect(dialog).toBeDefined();
      expect(screen.getByText('Aktif Çağrılar')).toBeDefined();
      expect(screen.getByText('Bekleyen Çağrı Yok')).toBeDefined();

      const closeBtn = screen.getByRole('button', { name: 'Kapat' });
      fireEvent.click(closeBtn);

      expect(screen.queryByRole('dialog')).toBeNull();
    });
  });

  describe('Stage 4: Lock / Shift Timeout Transition', () => {
    it('locks terminal and returns to PIN view when Kilitle is clicked', async () => {
      globalThis.fetch = vi.fn().mockResolvedValue({ ok: true }) as unknown as typeof fetch;

      render(<App initialTerminal={mockTerminal} initialStaff={mockStaff} />);

      expect(screen.getByText('Garson & Operasyon')).toBeDefined();

      const lockBtn = screen.getByRole('button', { name: 'Kilitle' });
      fireEvent.click(lockBtn);

      await waitFor(() => {
        // App transitions back to LOCKED state: PIN Pad visible, tables removed
        expect(screen.getByText('Terminal Kilitli')).toBeDefined();
        expect(screen.queryByText('Garson & Operasyon')).toBeNull();
        expect(screen.queryByText('Masa Yönetimi')).toBeNull();
      });
    });
  });

  describe('Stage 5: Terminal Revocation / Reset Transition', () => {
    it('resets to UNENROLLED state when terminal is deactivated', async () => {
      globalThis.fetch = vi.fn().mockResolvedValue({ ok: true }) as unknown as typeof fetch;

      render(<App initialTerminal={mockTerminal} initialStaff={null} />);

      expect(screen.getByText('Terminal Kilitli')).toBeDefined();

      const resetBtn = screen.getByRole('button', { name: 'Farklı Terminal / Cihazı Sıfırla' });
      fireEvent.click(resetBtn);

      await waitFor(() => {
        // Transitions to UNENROLLED state: Activation screen visible
        expect(screen.getByRole('heading', { level: 1, name: 'Cihaz Aktivasyonu' })).toBeDefined();
        expect(screen.queryByText('Terminal Kilitli')).toBeNull();
      });
    });
  });

  describe('Error Boundary', () => {
    it('catches render errors and displays error boundary fallback', () => {
      const spy = vi.spyOn(console, 'error').mockImplementation(() => {});

      render(
        <App
          initialTerminal={mockTerminal}
          initialStaff={mockStaff}
          initialError={true}
        />
      );

      const alert = screen.getByRole('alert');
      expect(alert).toBeDefined();
      expect(screen.getByText('Beklenmeyen Bir Hata Oluştu')).toBeDefined();

      spy.mockRestore();
    });
  });
});
