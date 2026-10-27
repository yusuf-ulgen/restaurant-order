import { describe, it, expect, vi, beforeEach } from 'vitest';
import { renderHook, act } from '@testing-library/react';
import { useOperationsRealtimeCoordinator } from '../useOperationsRealtimeCoordinator';
import * as api from '../api';
import * as ui from '@restaurant-order/ui';
import type { TerminalContextDto, UserPrincipalDto } from '@restaurant-order/contracts';

vi.mock('../api');
vi.mock('@restaurant-order/ui', async () => {
  const actual = await vi.importActual<typeof import('@restaurant-order/ui')>('@restaurant-order/ui');
  return {
    ...actual,
    useAudioChime: () => ({ playChime: vi.fn(), isMuted: false, toggleMute: vi.fn() }),
    useRealtimeEvents: vi.fn(),
    markEventReceivedInForeground: vi.fn(),
  };
});

describe('useOperationsRealtimeCoordinator Scoping & Lifecycle', () => {
  const mockTerminalA: TerminalContextDto = {
    terminalId: 'term-a',
    tenantId: '11111111-1111-1111-1111-111111111111',
    branchId: 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa',
    terminalName: 'Kasa A',
    deviceIdentifier: 'dev-a',
    isActive: true,
  };

  const mockTerminalB: TerminalContextDto = {
    terminalId: 'term-b',
    tenantId: '11111111-1111-1111-1111-111111111111',
    branchId: 'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb',
    terminalName: 'Kasa B',
    deviceIdentifier: 'dev-b',
    isActive: true,
  };

  const mockStaff: UserPrincipalDto = {
    userId: 'staff-1',
    email: 'waiter@test.com',
    tenantId: '11111111-1111-1111-1111-111111111111',
    branchId: 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa',
    role: 'Waiter',
    securityVersion: 1,
  };

  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('Branch A delayed response does not overwrite Branch B current snapshot', async () => {
    let resolveBranchA: (value: any) => void;
    const branchAPromise = new Promise((res) => {
      resolveBranchA = res;
    });

    vi.mocked(api.fetchActiveServiceRequests).mockImplementation(async (branchId) => {
      if (branchId === mockTerminalA.branchId) {
        return branchAPromise as any;
      }
      return [
        {
          id: 'req-b1',
          branchId: mockTerminalB.branchId,
          tableId: 't-b1',
          status: 'Open',
          requestType: 'WaiterCall',
          createdAtUtc: new Date().toISOString(),
          concurrencyToken: 'token-b',
        },
      ] as any;
    });

    vi.mocked(api.fetchBranchReadyItems).mockResolvedValue([]);
    vi.mocked(ui.useRealtimeEvents).mockReturnValue({
      status: 'connected',
      lastSequenceNumber: 1,
      resyncRequired: false,
    } as any);

    const { result, rerender } = renderHook(
      ({ terminal }) =>
        useOperationsRealtimeCoordinator({
          terminal,
          staffUser: mockStaff,
        }),
      { initialProps: { terminal: mockTerminalA } }
    );

    // Switch scope to Branch B before Branch A resolves
    rerender({ terminal: mockTerminalB });

    await act(async () => {
      await Promise.resolve();
    });

    // Verify Branch B data was applied
    expect(result.current.activeRequests).toHaveLength(1);
    expect(result.current.activeRequests[0]!.id).toBe('req-b1');

    // Now Branch A resolves late
    await act(async () => {
      resolveBranchA!([
        {
          id: 'req-a1-stale',
          branchId: mockTerminalA.branchId,
          tableId: 't-a1',
          status: 'Open',
          requestType: 'Water',
          createdAtUtc: new Date().toISOString(),
          concurrencyToken: 'token-a',
        },
      ]);
      await Promise.resolve();
    });

    // Crucial regression assertion: Branch A stale response must NOT overwrite Branch B
    expect(result.current.activeRequests).toHaveLength(1);
    expect(result.current.activeRequests[0]!.id).toBe('req-b1');
  });

  it('fails fullResync and throws error when snapshot request fails', async () => {
    vi.mocked(api.fetchActiveServiceRequests).mockRejectedValue(new Error('Network error on snapshot'));
    vi.mocked(api.fetchBranchReadyItems).mockResolvedValue([]);
    vi.mocked(ui.useRealtimeEvents).mockReturnValue({
      status: 'connected',
      lastSequenceNumber: 1,
      resyncRequired: false,
    } as any);

    const { result } = renderHook(() =>
      useOperationsRealtimeCoordinator({
        terminal: mockTerminalA,
        staffUser: mockStaff,
      })
    );

    await act(async () => {
      await expect(result.current.fullResync()).rejects.toThrow('Network error on snapshot');
    });

    // Stale/error state indicator is properly recorded
    expect(result.current.isStale).toBe(true);
    expect(result.current.syncError).toContain('Network error on snapshot');
  });

  it('triggers onSessionRevoked and cleans up state immediately when onSessionClosed fires', async () => {
    let capturedOnSessionClosed: (() => void) | undefined;
    vi.mocked(ui.useRealtimeEvents).mockImplementation((opts: any) => {
      capturedOnSessionClosed = opts.onSessionClosed;
      return { status: 'connected', lastSequenceNumber: 1, resyncRequired: false } as any;
    });

    const onSessionRevoked = vi.fn();
    const { result } = renderHook(() =>
      useOperationsRealtimeCoordinator({
        terminal: mockTerminalA,
        staffUser: mockStaff,
        onSessionRevoked,
      })
    );

    expect(capturedOnSessionClosed).toBeDefined();

    await act(async () => {
      capturedOnSessionClosed!();
    });

    expect(onSessionRevoked).toHaveBeenCalledTimes(1);
    expect(result.current.activeRequests).toEqual([]);
    expect(result.current.readyItems).toEqual([]);
    expect(result.current.notifications).toEqual([]);
  });
});
