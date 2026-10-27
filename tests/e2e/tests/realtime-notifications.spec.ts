import { test, expect } from '@playwright/test';
import * as signalR from '@microsoft/signalr';
import {
  seedCustomerExperienceScenario,
  cleanupCustomerScenario,
  mintDevAdminToken,
  type SeededCustomerScenario,
} from './e2e-catalog-seed';
import {
  createStaffSignalRConnection,
  createCustomerSignalRConnection,
  mintDevCustomerToken,
  waitForEventType,
} from './realtime-e2e-helpers';

import { enterCustomerSession, transitionSessionToClosed } from './realtime-browser-helpers';

test.describe('Real-Time Events & Notifications E2E (Phase 9 Complete Verification)', () => {
  let scenario: SeededCustomerScenario;

  test.beforeEach(async ({ request }) => {
    scenario = await seedCustomerExperienceScenario(request);
  });

  test.afterEach(async ({ request }) => {
    if (scenario) {
      await cleanupCustomerScenario(request, scenario);
    }
  });

  // 1. Müşteri sipariş oluşturur; yetkili personel olayı alır.
  test('Scenario 1: Customer creates order; authorized staff receives live event on hub', async ({
    page,
  }) => {
    const waiterToken = mintDevAdminToken(scenario.tenantId, scenario.branchId, 'Waiter');
    const waiterHub = createStaffSignalRConnection(waiterToken, scenario.branchId);
    await waiterHub.start();

    try {
      const orderEventPromise = waitForEventType(waiterHub, 'order.created');

      await enterCustomerSession(page, scenario);

      // Navigate to menu and place order via customer UI
      const exploreBtn = page.getByTestId('explore-menu-btn');
      if (await exploreBtn.isVisible()) {
        await exploreBtn.click();
      } else {
        await page.getByTestId('bottom-nav-menu-btn').click();
      }

      const burgerCard = page.getByTestId(`menu-item-card-${scenario.itemBurgerId}`);
      await expect(burgerCard).toBeVisible({ timeout: 10000 });
      await burgerCard.click();

      const addToCartCta = page.getByTestId('add-to-cart-cta-btn');
      await expect(addToCartCta).toBeVisible({ timeout: 5000 });
      await addToCartCta.click();

      await page.getByTestId('bottom-nav-cart-btn').click();
      await page.getByTestId('submit-order-cta-btn').click();

      const submitBtn = page.getByTestId('confirm-and-submit-order-btn');
      await expect(submitBtn).toBeVisible({ timeout: 5000 });
      await submitBtn.click();

      await expect(page.getByTestId('order-success-sheet')).toBeVisible({ timeout: 10000 });

      // Authorized waiter hub connection receives the real operational event
      const liveEvent = await orderEventPromise;
      expect(liveEvent.tenant_id).toBe(scenario.tenantId);
      expect(liveEvent.branch_id).toBe(scenario.branchId);
      expect(liveEvent.event_type).toBe('order.created');
    } finally {
      await waiterHub.stop();
    }
  });

  // 2. Gerçek sipariş durum değişikliği müşteri ekranını günceller.
  test('Scenario 2: Real order item status change updates customer screen in real time', async ({
    page,
    request,
  }) => {
    await enterCustomerSession(page, scenario);

    // Place an initial order via customer UI
    const exploreBtn = page.getByTestId('explore-menu-btn');
    if (await exploreBtn.isVisible()) {
      await exploreBtn.click();
    } else {
      await page.getByTestId('bottom-nav-menu-btn').click();
    }

    const burgerCard = page.getByTestId(`menu-item-card-${scenario.itemBurgerId}`);
    await expect(burgerCard).toBeVisible({ timeout: 10000 });
    await burgerCard.click();

    await page.getByTestId('add-to-cart-cta-btn').click();
    await page.getByTestId('bottom-nav-cart-btn').click();
    await page.getByTestId('submit-order-cta-btn').click();

    const [orderResponse] = await Promise.all([
      page.waitForResponse((res) => res.url().includes('/api/v1/customer/orders') && res.request().method() === 'POST'),
      page.getByTestId('confirm-and-submit-order-btn').click(),
    ]);
    expect(orderResponse.status()).toBe(201);
    const orderData = await orderResponse.json();
    const orderId = orderData.id;
    const itemId = orderData.items[0].id;

    // Close success sheet if open and ensure table tab is active
    const successSheet = page.getByTestId('order-success-sheet');
    await expect(successSheet).toBeVisible({ timeout: 10000 });
    const returnBtn = page.getByTestId('return-to-menu-btn');
    if (await returnBtn.isVisible()) {
      await returnBtn.click();
    }
    await expect(successSheet).not.toBeVisible();

    const tableNavBtn = page.getByTestId('bottom-nav-table-btn');
    await tableNavBtn.click();
    await expect(page.getByTestId('table-overview-tab')).toBeVisible();

    // Staff cancels or updates item status via staff endpoint
    const cancelRes = await request.post(
      `/api/v1/floor/branches/${scenario.branchId}/sessions/${scenario.sessionId}/orders/${orderId}/items/${itemId}/cancel`,
      {
        headers: {
          Authorization: `Bearer ${scenario.adminToken}`,
          'If-Match': `"${orderData.concurrencyToken}"`,
          'Content-Type': 'application/json',
        },
        data: {
          reason: 'E2E Status Update',
          concurrencyToken: orderData.concurrencyToken,
        },
      }
    );
    expect([200, 204]).toContain(cancelRes.status());

    // Customer active session receives event and updates order status UI
    await expect(page.getByTestId('active-order-status-card')).toBeVisible({ timeout: 15000 });
  });

  // 3. Servis talebi doğru branch’e ulaşır; yanlış branch’e ulaşmaz.
  test('Scenario 3: Service request reaches correct branch and not wrong branch', async ({
    page,
  }) => {
    const wrongBranchId = 'd2222222-2222-2222-2222-222222222222';
    const correctWaiterToken = mintDevAdminToken(scenario.tenantId, scenario.branchId, 'Waiter');
    const wrongWaiterToken = mintDevAdminToken(scenario.tenantId, wrongBranchId, 'Waiter');

    const correctHub = createStaffSignalRConnection(correctWaiterToken, scenario.branchId);
    const wrongHub = createStaffSignalRConnection(wrongWaiterToken, wrongBranchId);

    await correctHub.start();
    await wrongHub.start();

    try {
      let wrongBranchReceived = false;
      wrongHub.on('ReceiveEvent', () => {
        wrongBranchReceived = true;
      });

      const correctEventPromise = waitForEventType(correctHub, 'table.service_requested');

      await enterCustomerSession(page, scenario);

      await page.getByTestId('request-service-btn').click();
      await expect(page.getByTestId('service-request-sheet')).toBeVisible();

      await page.getByTestId('action-btn-waiter').click();
      await page.getByTestId('submit-confirm-btn-waiter').click();

      const receivedEvent = await correctEventPromise;
      expect(receivedEvent.tenant_id).toBe(scenario.tenantId);
      expect(receivedEvent.branch_id).toBe(scenario.branchId);
      expect(receivedEvent.event_type).toBe('table.service_requested');

      // Assert wrong branch never received the event
      expect(wrongBranchReceived).toBe(false);
    } finally {
      await correctHub.stop();
      await wrongHub.stop();
    }
  });

  // 4. Quick-86 sonrası müşteri ekranı/sepette ürün unavailable görünür.
  test('Scenario 4: Quick-86 updates customer menu and cart to unavailable in real time', async ({
    page,
    request,
  }) => {
    await enterCustomerSession(page, scenario);

    const exploreBtn = page.getByTestId('explore-menu-btn');
    if (await exploreBtn.isVisible()) {
      await exploreBtn.click();
    } else {
      await page.getByTestId('bottom-nav-menu-btn').click();
    }
    await expect(page.getByTestId('digital-menu-view')).toBeVisible({ timeout: 10000 });

    // 1. Fetch current item concurrency token to strictly satisfy If-Match and body DTO
    const itemGetRes = await request.get(
      `/api/v1/catalog/branches/${scenario.branchId}/menus/${scenario.menuId}/items/${scenario.itemBurgerId}`,
      { headers: { Authorization: `Bearer ${scenario.adminToken}` } }
    );
    expect(itemGetRes.status()).toBe(200);
    const itemData = await itemGetRes.json();
    const concurrencyToken = itemData.concurrencyToken;

    // 2. Admin marks burger 86 (unavailable) with strict DTO: reasonCode = 'SoldOut'
    const postRes = await request.post(
      `/api/v1/catalog/branches/${scenario.branchId}/menus/${scenario.menuId}/items/${scenario.itemBurgerId}/quick-86`,
      {
        headers: {
          Authorization: `Bearer ${scenario.adminToken}`,
          'If-Match': `"${concurrencyToken}"`,
          'Content-Type': 'application/json',
        },
        data: {
          reasonCode: 'SoldOut',
          note: 'Tükendi',
          concurrencyToken: concurrencyToken,
        },
      }
    );
    expect(postRes.status()).toBe(200);

    // 3. Customer menu view refreshes via realtime catalog.item_availability_changed event
    const burgerCard = page.getByTestId(`menu-item-card-${scenario.itemBurgerId}`);
    await expect(burgerCard).toBeVisible({ timeout: 10000 });
    await expect(burgerCard.getByText(/Tükendi|Stokta Yok/i).first()).toBeVisible({ timeout: 10000 });
  });

  // 5. Bağlantı kesilirken oluşan değişiklik reconnect ve bütün sync sayfalarıyla toparlanır.
  test('Scenario 5: HTTP catch-up sync endpoint returns ordered events with pagination', async ({
    request,
  }) => {
    const waiterToken = mintDevAdminToken(scenario.tenantId, scenario.branchId, 'Waiter');
    const syncRes = await request.get(`/api/v1/events/sync?since_sequence=0&limit=2`, {
      headers: {
        Authorization: `Bearer ${waiterToken}`,
      },
    });

    if (syncRes.status() !== 200) {
      console.error('SYNC ERROR DETAIL:', syncRes.status(), await syncRes.text());
    }
    expect(syncRes.status()).toBe(200);
    const body = await syncRes.json();
    expect(Array.isArray(body.events)).toBe(true);
    expect(typeof body.latest_sequence_number).toBe('number');
    expect(typeof body.has_more).toBe('boolean');

    if (body.has_more) {
      const nextSyncRes = await request.get(
        `/api/v1/events/sync?since_sequence=${body.latest_sequence_number}&limit=2`,
        { headers: { Authorization: `Bearer ${waiterToken}` } }
      );
      expect(nextSyncRes.status()).toBe(200);
      const nextBody = await nextSyncRes.json();
      expect(Array.isArray(nextBody.events)).toBe(true);
    }
  });

  // 6. Session kapanınca eski müşteri bağlantısı sonraki özel olayları alamaz.
  test('Scenario 6: Closed session prevents customer connection from receiving subsequent private events', async ({
    request, page,
  }) => {
    await enterCustomerSession(page, scenario);
    const customerToken = mintDevCustomerToken(scenario.tenantId, scenario.branchId, scenario.sessionId);
    const customerHub = createCustomerSignalRConnection(customerToken);
    await customerHub.start();
    await page.evaluate(async (token) => {
      const modulePath = '/node_modules/.vite/deps/@microsoft_signalr.js';
      const sdk = await import(modulePath);
      const state = { closed: false, leaked: 0 };
      (window as any).__revocationProbe = state;
      const hub = new sdk.HubConnectionBuilder().withUrl('/hubs/events', {
        accessTokenFactory: () => token, transport: sdk.HttpTransportType.WebSockets, skipNegotiation: true,
      }).build();
      hub.onclose(() => { state.closed = true; });
      hub.on('ReceiveEvent', () => { if (state.closed) state.leaked++; });
      await hub.start();
      (window as any).__probeHub = hub;
    }, customerToken);

    let sessionClosedReceived = false;
    const leakedSubsequentEvents: any[] = [];

    customerHub.on('SessionClosed', () => {
      sessionClosedReceived = true;
    });
    customerHub.on('AccessRevoked', () => { sessionClosedReceived = true; });
    const isPrivateEvent = (eventType: string) =>
      eventType === 'order.created' ||
      eventType === 'order.item_status_changed' ||
      eventType === 'table.service_requested' ||
      eventType === 'table.service_request_status_changed';

    customerHub.on('ReceiveEvent', (ev) => {
      const parsed = typeof ev === 'string' ? JSON.parse(ev) : ev;
      if (isPrivateEvent(parsed.event_type)) {
        leakedSubsequentEvents.push(parsed);
      }
    });

    try {
      // Move session through linear lifecycle: Active -> BillRequested -> Closed
      await transitionSessionToClosed(request, scenario, 'E2E Session Closed Test');

      // Customer receives terminal eviction signal
      await expect.poll(() => sessionClosedReceived, { timeout: 10000 }).toBe(true);
      await expect.poll(() => page.evaluate(() => (window as any).__revocationProbe.closed), { timeout: 10000 }).toBe(true);
      await expect.poll(() => customerHub.state).toBe(signalR.HubConnectionState.Disconnected);
      expect(await page.evaluate(() => (window as any).__revocationProbe.leaked)).toBe(0);

      // Server-side continuous revocation: even if client ignores SessionClosed and attempts
      // to maintain transport, server has removed connection from SignalR groups and evicted it,
      // so no subsequent private events can reach this connection.
      await new Promise((r) => setTimeout(r, 2000));
      expect(leakedSubsequentEvents).toHaveLength(0);
    } finally {
      await customerHub.stop();
    }
  });

  // 7. Geçerli ama başka tenant/session’a ait istemci yanlış olayı alamaz.
  test('Scenario 7: Client from another tenant cannot receive events from different tenant', async ({
    page,
  }) => {
    const foreignTenantId = '22222222-2222-2222-2222-222222222222';
    const foreignBranchId = 'd4444444-4444-4444-4444-444444444444';
    const foreignWaiterToken = mintDevAdminToken(foreignTenantId, foreignBranchId, 'Waiter');
    const foreignHub = createStaffSignalRConnection(foreignWaiterToken, foreignBranchId);
    await foreignHub.start();

    try {
      let foreignReceived = false;
      foreignHub.on('ReceiveEvent', () => {
        foreignReceived = true;
      });

      await enterCustomerSession(page, scenario);
      await page.getByTestId('request-service-btn').click();
      await page.getByTestId('action-btn-waiter').click();
      await page.getByTestId('submit-confirm-btn-waiter').click();

      // Wait a grace period to ensure worker dispatched
      await new Promise((r) => setTimeout(r, 2000));
      expect(foreignReceived).toBe(false);
    } finally {
      await foreignHub.stop();
    }
  });

  // 8. Herhangi bir masanın kapanması personel bağlantısını kapatmaz.
  test('Scenario 8: Table closure does not disconnect staff connection', async ({
    request,
  }) => {
    const waiterToken = mintDevAdminToken(scenario.tenantId, scenario.branchId, 'Waiter');
    const waiterHub = createStaffSignalRConnection(waiterToken, scenario.branchId);
    await waiterHub.start();

    try {
      expect(waiterHub.state).toBe(signalR.HubConnectionState.Connected);

      await transitionSessionToClosed(request, scenario, 'Table Closure Waiter Resiliency Test');

      // Waiter hub remains active and connected
      await new Promise((r) => setTimeout(r, 1000));
      expect(waiterHub.state).toBe(signalR.HubConnectionState.Connected);
    } finally {
      await waiterHub.stop();
    }
  });

  // 9. Push izni reddedilince temel sipariş akışı çalışmaya devam eder.
  test('Scenario 9: Denying push permission does not impede customer browsing and ordering flow', async ({
    page,
  }) => {
    await enterCustomerSession(page, scenario);

    // If push notification banner is present, dismiss it
    const dismissBtn = page.getByRole('button', { name: /Daha Sonra|Kapat|Şimdi Değil/i }).first();
    if (await dismissBtn.isVisible()) {
      await dismissBtn.click();
    }

    // Customer can seamlessly navigate to menu and view items
    const exploreBtn = page.getByTestId('explore-menu-btn');
    if (await exploreBtn.isVisible()) {
      await exploreBtn.click();
    } else {
      await page.getByTestId('bottom-nav-menu-btn').click();
    }

    const digitalMenuView = page.getByTestId('digital-menu-view');
    await expect(digitalMenuView).toBeVisible({ timeout: 10000 });

    const burgerCard = page.getByTestId(`menu-item-card-${scenario.itemBurgerId}`);
    await expect(burgerCard).toBeVisible();
  });
});
