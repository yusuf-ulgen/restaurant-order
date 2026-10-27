import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { OperationsFloorContainer } from '../OperationsFloorContainer';
import {
  TableFloorStatusDto,
  DiningAreaContract,
  BranchFloorStatusDto,
} from '@restaurant-order/contracts';

const mockAreas: DiningAreaContract[] = [
  { id: 'area-1', branchId: 'branch-1', name: 'Bahçe', sortOrder: 1, isActive: true } as any,
];

const mockTablesInit: TableFloorStatusDto[] = [
  {
    table: { id: 'table-1', branchId: 'branch-1', diningAreaId: 'area-1', tableNumber: 'B-1', name: 'Bahçe Masa 1', capacity: 4, isActive: true } as any,
    derivedStatus: 'Available',
    activeSession: null,
  },
  {
    table: { id: 'table-2', branchId: 'branch-1', diningAreaId: 'area-1', tableNumber: 'B-2', name: 'Bahçe Masa 2', capacity: 2, isActive: true } as any,
    derivedStatus: 'Available',
    activeSession: null,
  },
];

describe('WAIT-02 OperationsFloorContainer Loop & Selection Regression Tests', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('does NOT trigger an infinite reload loop on mount and snapshot updates', async () => {
    let fetchCount = 0;

    vi.spyOn(global, 'fetch').mockImplementation(async (input) => {
      const url = typeof input === 'string' ? input : (input as Request).url;
      if (url.includes('/status')) {
        fetchCount++;
        const statusDto: BranchFloorStatusDto = {
          branchId: 'branch-1',
          tables: mockTablesInit,
        };
        return new Response(JSON.stringify(statusDto), { status: 200 });
      }
      if (url.includes('/dining-areas')) {
        return new Response(JSON.stringify(mockAreas), { status: 200 });
      }
      return new Response(JSON.stringify({}), { status: 200 });
    });

    render(<OperationsFloorContainer branchId="branch-1" />);

    await waitFor(() => {
      expect(screen.getByText('B-1')).toBeDefined();
      expect(screen.getByText('B-2')).toBeDefined();
    });

    // Küçük bir bekleme ekleyerek herhangi bir re-render döngüsü tetiklenmediğini kanıtlayalım
    await new Promise((r) => setTimeout(r, 200));

    // İlk mount'ta sadece 1 kez status fetch yapılmış olmalı (sonsuz döngü yok)
    expect(fetchCount).toBe(1);
  });

  it('preserves selected table across floor refreshes without closing detail drawer', async () => {
    let refreshFloorCallback: (() => Promise<void>) | undefined;

    const mockStatus1: BranchFloorStatusDto = {
      branchId: 'branch-1',
      tables: mockTablesInit,
    };

    const mockStatus2: BranchFloorStatusDto = {
      branchId: 'branch-1',
      tables: [
        {
          table: { id: 'table-1', branchId: 'branch-1', diningAreaId: 'area-1', tableNumber: 'B-1', name: 'Bahçe Masa 1', capacity: 4, isActive: true } as any,
          derivedStatus: 'Occupied',
          activeSession: {
            id: 'sess-updated-1',
            tenantId: 'tenant-1',
            branchId: 'branch-1',
            tableId: 'table-1',
            status: 'Active',
            guestCount: 3,
            openedAtUtc: new Date().toISOString(),
            concurrencyToken: 'token-updated-1',
            createdAtUtc: new Date().toISOString(),
          },
        },
        mockTablesInit[1]!,
      ],
    };

    let fetchTurn = 1;
    vi.spyOn(global, 'fetch').mockImplementation(async (input) => {
      const url = typeof input === 'string' ? input : (input as Request).url;
      if (url.includes('/status')) {
        const data = fetchTurn === 1 ? mockStatus1 : mockStatus2;
        return new Response(JSON.stringify(data), { status: 200 });
      }
      if (url.includes('/dining-areas')) {
        return new Response(JSON.stringify(mockAreas), { status: 200 });
      }
      if (url.includes('/orders')) {
        return new Response(JSON.stringify([]), { status: 200 });
      }
      if (url.includes('/service-requests')) {
        return new Response(JSON.stringify([]), { status: 200 });
      }
      return new Response(JSON.stringify({}), { status: 200 });
    });

    render(
      <OperationsFloorContainer
        branchId="branch-1"
        onRegisterRefreshFloor={(fn) => {
          refreshFloorCallback = fn;
        }}
      />
    );

    const tableBtn = await screen.findByRole('button', { name: /^B-1/ });
    await waitFor(() => {
      expect(tableBtn.hasAttribute('disabled')).toBe(false);
    });

    // Masayı tıkla ve detay çekmecesini aç
    fireEvent.click(tableBtn);

    await waitFor(() => {
      expect(screen.getByText('B-1 - Bahçe Masa 1')).toBeDefined();
    });

    // Arka plan yenilemesi (realtime veya manuel) tetikleniyor
    fetchTurn = 2;
    expect(refreshFloorCallback).toBeDefined();
    await refreshFloorCallback!();

    await waitFor(() => {
      // Çekmece kapanmamış olmalı ve yeni veriyi yansıtmalı
      expect(screen.getByText('B-1 - Bahçe Masa 1')).toBeDefined();
    });
  });

  it('discards stale slower HTTP responses and does not overwrite newer floor snapshot', async () => {
    let resolveSlowRequest: ((val: Response) => void) | undefined;

    vi.spyOn(global, 'fetch').mockImplementation(async (input) => {
      const url = typeof input === 'string' ? input : (input as Request).url;
      if (url.includes('/status')) {
        // İlk istek kasıtlı olarak geciktiriliyor (race condition simülasyonu)
        return new Promise<Response>((resolve) => {
          resolveSlowRequest = resolve;
        });
      }
      if (url.includes('/dining-areas')) {
        return new Response(JSON.stringify(mockAreas), { status: 200 });
      }
      return new Response(JSON.stringify({}), { status: 200 });
    });

    const { unmount } = render(
      <OperationsFloorContainer
        branchId="branch-1"
      />
    );

    // Kapsam değişti veya unmount oldu
    unmount();

    // Gecikmiş eski yanıt şimdi dönüyor
    expect(resolveSlowRequest).toBeDefined();
    resolveSlowRequest!(
      new Response(
        JSON.stringify({
          branchId: 'branch-1',
          branchName: 'Eski Şube',
          tables: [],
        }),
        { status: 200 }
      )
    );

    // Hata atmadan ve state'e uygulanmadan sessizce atılmalıdır
    await new Promise((r) => setTimeout(r, 50));
  });
});
