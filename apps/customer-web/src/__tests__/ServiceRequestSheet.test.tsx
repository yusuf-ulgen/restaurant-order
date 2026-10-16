import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { describe, it, expect, vi, beforeEach } from 'vitest';
import type { CustomerSessionDto, ServiceRequestDto, DiningSessionDto } from '@restaurant-order/contracts';
import { ServiceRequestSheet } from '../components/service/ServiceRequestSheet';
import { customerServiceApi } from '../api/customerServiceApi';

describe('ServiceRequestSheet Component (CUST-06)', () => {
  const sampleSession: CustomerSessionDto = {
    sessionId: 'session-123',
    tenantId: 'tenant-abc',
    branchId: 'branch-xyz',
    tableId: 'table-456',
    tableNumber: '12',
    tableName: 'Masa 12',
    sessionStatus: 'Active',
    concurrencyToken: 'ct-token-1',
    guestCount: 2,
    openedAtUtc: '2026-10-16T19:00:00Z',
  };

  beforeEach(() => {
    vi.restoreAllMocks();
    vi.spyOn(customerServiceApi, 'getOpenRequests').mockResolvedValue([]);
  });

  it('renders all 5 service actions inside the bottom sheet', async () => {
    render(
      <ServiceRequestSheet
        isOpen={true}
        onClose={vi.fn()}
        session={sampleSession}
      />
    );

    await waitFor(() => {
      expect(screen.getByTestId('service-request-sheet')).toBeDefined();
    });
    expect(screen.getByTestId('service-card-waiter')).toBeDefined();
    expect(screen.getByTestId('service-card-water')).toBeDefined();
    expect(screen.getByTestId('service-card-napkin')).toBeDefined();
    expect(screen.getByTestId('service-card-assistance')).toBeDefined();
    expect(screen.getByTestId('service-card-bill')).toBeDefined();

    expect(screen.getAllByText('Garson Çağır').length).toBeGreaterThan(0);
    expect(screen.getAllByText('Su İste').length).toBeGreaterThan(0);
    expect(screen.getAllByText('Peçete İste').length).toBeGreaterThan(0);
    expect(screen.getAllByText('Yardım İste').length).toBeGreaterThan(0);
    expect(screen.getAllByText('Hesap İste').length).toBeGreaterThan(0);
  });

  it('submits a waiter request with confirmation and updates badge to open', async () => {
    const mockCreated: ServiceRequestDto = {
      id: 'sr-1',
      tenantId: sampleSession.tenantId,
      branchId: sampleSession.branchId,
      diningSessionId: sampleSession.sessionId,
      tableId: sampleSession.tableId,
      requestType: 'Waiter',
      status: 'Open',
      note: 'Acil bakar mısınız',
      idempotencyKey: 'key-1',
      createdAtUtc: '2026-10-16T20:00:00Z',
      concurrencyToken: 'ct-1',
    };

    const createSpy = vi.spyOn(customerServiceApi, 'createServiceRequest').mockResolvedValue(mockCreated);

    render(
      <ServiceRequestSheet
        isOpen={true}
        onClose={vi.fn()}
        session={sampleSession}
      />
    );

    // 1. Click "Garson Çağır" button
    const actionBtn = screen.getByTestId('action-btn-waiter');
    fireEvent.click(actionBtn);

    // 2. Confirmation box should appear with note input
    expect(screen.getByTestId('confirm-box-waiter')).toBeDefined();
    const noteInput = screen.getByTestId('note-input-waiter') as HTMLInputElement;
    fireEvent.change(noteInput, { target: { value: 'Acil bakar mısınız' } });

    // 3. Confirm submission
    const submitBtn = screen.getByTestId('submit-confirm-btn-waiter');
    fireEvent.click(submitBtn);

    await waitFor(() => {
      expect(createSpy).toHaveBeenCalledWith('Waiter', 'Acil bakar mısınız');
    });

    // 4. Badge should appear and announcement updated
    await waitFor(() => {
      expect(screen.getByTestId('badge-open-waiter')).toBeDefined();
    });
    expect(screen.getByText('Talebiniz Alındı')).toBeDefined();
  });

  it('prevents duplicate submittals on rapid double clicks', async () => {
    let resolvePromise: (value: ServiceRequestDto) => void;
    const delayedPromise = new Promise<ServiceRequestDto>((resolve) => {
      resolvePromise = resolve;
    });

    const createSpy = vi.spyOn(customerServiceApi, 'createServiceRequest').mockReturnValue(delayedPromise);

    render(
      <ServiceRequestSheet
        isOpen={true}
        onClose={vi.fn()}
        session={sampleSession}
      />
    );

    fireEvent.click(screen.getByTestId('action-btn-water'));
    const submitBtn = screen.getByTestId('submit-confirm-btn-water');

    // Rapid double click
    fireEvent.click(submitBtn);
    fireEvent.click(submitBtn);

    expect(createSpy).toHaveBeenCalledTimes(1);

    // Resolve in-flight call
    resolvePromise!({
      id: 'sr-water',
      tenantId: sampleSession.tenantId,
      branchId: sampleSession.branchId,
      diningSessionId: sampleSession.sessionId,
      tableId: sampleSession.tableId,
      requestType: 'Water',
      status: 'Open',
      idempotencyKey: 'key-w',
      createdAtUtc: '2026-10-16T20:00:00Z',
      concurrencyToken: 'ct-w',
    });

    await waitFor(() => {
      expect(screen.getByTestId('badge-open-water')).toBeDefined();
    });
  });

  it('renders pre-existing open request and allows cancellation', async () => {
    const existingReq: ServiceRequestDto = {
      id: 'sr-existing-napkin',
      tenantId: sampleSession.tenantId,
      branchId: sampleSession.branchId,
      diningSessionId: sampleSession.sessionId,
      tableId: sampleSession.tableId,
      requestType: 'Napkin',
      status: 'Open',
      note: 'Islak mendil',
      idempotencyKey: 'k-nap',
      createdAtUtc: '2026-10-16T20:00:00Z',
      concurrencyToken: 'ct-nap',
    };

    vi.spyOn(customerServiceApi, 'getOpenRequests').mockResolvedValue([existingReq]);
    const cancelSpy = vi.spyOn(customerServiceApi, 'cancelRequest').mockResolvedValue({
      ...existingReq,
      status: 'Cancelled',
    });

    render(
      <ServiceRequestSheet
        isOpen={true}
        onClose={vi.fn()}
        session={sampleSession}
      />
    );

    await waitFor(() => {
      expect(screen.getByTestId('badge-open-napkin')).toBeDefined();
    });
    expect(screen.getByText('Not: Islak mendil')).toBeDefined();

    // Cancel the open request
    const cancelBtn = screen.getByTestId('cancel-request-btn-napkin');
    fireEvent.click(cancelBtn);

    await waitFor(() => {
      expect(cancelSpy).toHaveBeenCalledWith('sr-existing-napkin');
    });

    // Badge should be removed after cancellation
    await waitFor(() => {
      expect(screen.queryByTestId('badge-open-napkin')).toBeNull();
    });
  });

  it('handles bill request lifecycle and triggers onSessionUpdated', async () => {
    const onSessionUpdated = vi.fn();
    const updatedSession: DiningSessionDto = {
      id: sampleSession.sessionId,
      tenantId: sampleSession.tenantId,
      branchId: sampleSession.branchId,
      tableId: sampleSession.tableId,
      status: 'BillRequested',
      guestCount: 2,
      openedAtUtc: sampleSession.openedAtUtc,
      concurrencyToken: 'ct-after-bill',
    };

    const billSpy = vi.spyOn(customerServiceApi, 'requestBill').mockResolvedValue(updatedSession);

    render(
      <ServiceRequestSheet
        isOpen={true}
        onClose={vi.fn()}
        session={sampleSession}
        onSessionUpdated={onSessionUpdated}
      />
    );

    fireEvent.click(screen.getByTestId('action-btn-bill'));
    expect(screen.getByTestId('confirm-box-bill')).toBeDefined();

    fireEvent.click(screen.getByTestId('submit-confirm-btn-bill'));

    await waitFor(() => {
      expect(billSpy).toHaveBeenCalledWith(
        sampleSession.branchId,
        sampleSession.sessionId,
        sampleSession.concurrencyToken
      );
    });

    expect(onSessionUpdated).toHaveBeenCalledWith(
      expect.objectContaining({
        sessionStatus: 'BillRequested',
        concurrencyToken: 'ct-after-bill',
      })
    );
  });
});
