import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import { OperationsCallsDrawer } from '../OperationsCallsDrawer';
import { StaffServiceRequestDto, ReadyOrderItemDto } from '@restaurant-order/contracts';
import { OperationalNotification } from '../useOperationsRealtimeCoordinator';

const mockRequests: StaffServiceRequestDto[] = [
  {
    id: 'req-1',
    tenantId: 'tenant-1',
    branchId: 'branch-1',
    diningSessionId: 'session-1',
    tableId: 'table-1',
    tableNumber: '4',
    requestType: 'Waiter',
    status: 'Open',
    note: 'Su rica ediyoruz',
    createdAtUtc: new Date(Date.now() - 120000).toISOString(),
    concurrencyToken: 'token-req-1',
  },
  {
    id: 'req-2',
    tenantId: 'tenant-1',
    branchId: 'branch-1',
    diningSessionId: 'session-2',
    tableId: 'table-2',
    tableNumber: '7',
    requestType: 'Napkin',
    status: 'Acknowledged',
    note: null,
    createdAtUtc: new Date(Date.now() - 300000).toISOString(),
    acknowledgedAtUtc: new Date(Date.now() - 60000).toISOString(),
    concurrencyToken: 'token-req-2',
  },
];

const mockReadyItems: ReadyOrderItemDto[] = [
  {
    orderId: 'order-1',
    itemId: 'item-1',
    diningSessionId: 'session-1',
    tableId: 'table-1',
    tableNumber: '4',
    roundNumber: 1,
    itemName: 'Izgara Köfte',
    variantName: 'Porsiyon',
    quantity: 2,
    status: 'Ready',
    createdAtUtc: new Date().toISOString(),
  },
];

const mockNotifications: OperationalNotification[] = [
  {
    id: 'notif-1',
    title: 'Yeni Sipariş Alındı',
    subtitle: 'Masa 4',
    time: '14:30:00',
    eventType: 'order.created',
    isRead: false,
  },
];

describe('WAIT-07 OperationsCallsDrawer Component', () => {
  const onAcknowledgeRequest = vi.fn();
  const onCompleteRequest = vi.fn();
  const onDismissNotification = vi.fn();
  const onClearAllNotifications = vi.fn();
  const onGoToTable = vi.fn();
  const onClose = vi.fn();

  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('renders active requests and ready items with tab navigation and correct counters', () => {
    render(
      <OperationsCallsDrawer
        isOpen={true}
        onClose={onClose}
        activeRequests={mockRequests}
        readyItems={mockReadyItems}
        notifications={mockNotifications}
        onAcknowledgeRequest={onAcknowledgeRequest}
        onCompleteRequest={onCompleteRequest}
        onDismissNotification={onDismissNotification}
        onClearAllNotifications={onClearAllNotifications}
        onGoToTable={onGoToTable}
        isOperating={false}
      />
    );

    // Sekmeler ve sayaçlar
    expect(screen.getByText('🔔 Aktif Çağrılar')).toBeDefined();
    expect(screen.getByText('🍽️ Hazır Ürünler')).toBeDefined();
    expect(screen.getByText('📜 Olay Akışı')).toBeDefined();

    // Aktif çağrılar tabında Masa 4 ve Masa 7 listelenmeli
    expect(screen.getByText('Masa 4')).toBeDefined();
    expect(screen.getByText('Garson Çağrısı')).toBeDefined();
    expect(screen.getByText('Bekliyor')).toBeDefined();
    expect(screen.getByText('Not: "Su rica ediyoruz"')).toBeDefined();

    expect(screen.getByText('Masa 7')).toBeDefined();
    expect(screen.getByText('Peçete İsteği')).toBeDefined();
    expect(screen.getByText('İşleme Alındı')).toBeDefined();
  });

  it('calls onAcknowledgeRequest with requestId and concurrencyToken when Kabul Et is clicked', () => {
    render(
      <OperationsCallsDrawer
        isOpen={true}
        onClose={onClose}
        activeRequests={mockRequests}
        readyItems={mockReadyItems}
        notifications={mockNotifications}
        onAcknowledgeRequest={onAcknowledgeRequest}
        onCompleteRequest={onCompleteRequest}
        onDismissNotification={onDismissNotification}
        onClearAllNotifications={onClearAllNotifications}
        onGoToTable={onGoToTable}
        isOperating={false}
      />
    );

    const acceptBtn = screen.getByRole('button', { name: 'Kabul Et' });
    fireEvent.click(acceptBtn);

    expect(onAcknowledgeRequest).toHaveBeenCalledWith('req-1', 'token-req-1');
  });

  it('calls onCompleteRequest with requestId and concurrencyToken when Tamamla is clicked', () => {
    render(
      <OperationsCallsDrawer
        isOpen={true}
        onClose={onClose}
        activeRequests={mockRequests}
        readyItems={mockReadyItems}
        notifications={mockNotifications}
        onAcknowledgeRequest={onAcknowledgeRequest}
        onCompleteRequest={onCompleteRequest}
        onDismissNotification={onDismissNotification}
        onClearAllNotifications={onClearAllNotifications}
        onGoToTable={onGoToTable}
        isOperating={false}
      />
    );

    const completeBtn = screen.getByRole('button', { name: 'Tamamla' });
    fireEvent.click(completeBtn);

    expect(onCompleteRequest).toHaveBeenCalledWith('req-2', 'token-req-2');
  });

  it('switches to ready items tab and allows navigation to table', () => {
    render(
      <OperationsCallsDrawer
        isOpen={true}
        onClose={onClose}
        activeRequests={mockRequests}
        readyItems={mockReadyItems}
        notifications={mockNotifications}
        onAcknowledgeRequest={onAcknowledgeRequest}
        onCompleteRequest={onCompleteRequest}
        onDismissNotification={onDismissNotification}
        onClearAllNotifications={onClearAllNotifications}
        onGoToTable={onGoToTable}
        isOperating={false}
      />
    );

    const readyTabBtn = screen.getByRole('button', { name: /Hazır Ürünler/i });
    fireEvent.click(readyTabBtn);

    expect(screen.getByText('2x Izgara Köfte (Porsiyon)')).toBeDefined();
    expect(screen.getByText('Tur #1')).toBeDefined();

    const goToTableBtns = screen.getAllByRole('button', { name: 'Masaya Git' });
    expect(goToTableBtns[0]).toBeDefined();
    fireEvent.click(goToTableBtns[0]!);

    expect(onGoToTable).toHaveBeenCalledWith('table-1');
    expect(onClose).toHaveBeenCalled();
  });

  it('switches to stream tab and dismissing notification does not affect service requests', () => {
    render(
      <OperationsCallsDrawer
        isOpen={true}
        onClose={onClose}
        activeRequests={mockRequests}
        readyItems={mockReadyItems}
        notifications={mockNotifications}
        onAcknowledgeRequest={onAcknowledgeRequest}
        onCompleteRequest={onCompleteRequest}
        onDismissNotification={onDismissNotification}
        onClearAllNotifications={onClearAllNotifications}
        onGoToTable={onGoToTable}
        isOperating={false}
      />
    );

    const streamTabBtn = screen.getByRole('button', { name: /Olay Akışı/i });
    fireEvent.click(streamTabBtn);

    expect(screen.getByText('Yeni Sipariş Alındı')).toBeDefined();

    const dismissBtn = screen.getByTitle('Bildirimi Kapat');
    fireEvent.click(dismissBtn);

    expect(onDismissNotification).toHaveBeenCalledWith('notif-1');
    expect(onAcknowledgeRequest).not.toHaveBeenCalled();
    expect(onCompleteRequest).not.toHaveBeenCalled();
  });

  it('displays actionError banner when provided', () => {
    render(
      <OperationsCallsDrawer
        isOpen={true}
        onClose={onClose}
        activeRequests={mockRequests}
        readyItems={mockReadyItems}
        notifications={mockNotifications}
        onAcknowledgeRequest={onAcknowledgeRequest}
        onCompleteRequest={onCompleteRequest}
        onDismissNotification={onDismissNotification}
        onClearAllNotifications={onClearAllNotifications}
        onGoToTable={onGoToTable}
        isOperating={false}
        actionError="Bu çağrı başka bir personel tarafından kabul edilmiş."
      />
    );

    expect(screen.getByText('Bu çağrı başka bir personel tarafından kabul edilmiş.')).toBeDefined();
  });
});
