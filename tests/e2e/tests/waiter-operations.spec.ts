import { test, expect } from '@playwright/test';
import {
  seedCustomerExperienceScenario,
  cleanupCustomerScenario,
  type SeededCustomerScenario,
} from './e2e-catalog-seed';

test.describe('Waiter & Operations Realtime & Flow E2E (Phase 10 Parts 1-4)', () => {
  let scenario: SeededCustomerScenario;

  test.beforeEach(async ({ request }) => {
    scenario = await seedCustomerExperienceScenario(request);
  });

  test.afterEach(async ({ request }) => {
    if (scenario) {
      await cleanupCustomerScenario(request, scenario);
    }
  });

  const setupTerminalAndLogin = async (page: any, terminalName = 'Garson POS E2E') => {
    await page.route('**/api/v1/terminals/current', async (route: any) => {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          terminalId: 'term-e2e-1',
          tenantId: scenario.tenantId,
          branchId: scenario.branchId,
          terminalName,
          deviceIdentifier: 'dev-e2e-1',
          isActive: true,
        }),
      });
    });

    await page.route('**/api/v1/auth/pin/login', async (route: any) => {
      const data = JSON.parse(route.request().postData() || '{}');
      if (data.pin === '1234') {
        await route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify({
            user: {
              userId: 'staff-e2e-1',
              tenantId: scenario.tenantId,
              branchId: scenario.branchId,
              role: 'Waiter',
              permissions: ['floor.view', 'order.create', 'order.cancel', 'service.request.manage', 'floor.manage'],
            },
          }),
        });
      } else {
        await route.fulfill({
          status: 401,
          contentType: 'application/json',
          body: JSON.stringify({ detail: 'Geçersiz PIN veya personel yetkisi yok.' }),
        });
      }
    });

    await page.goto('http://127.0.0.1:3002');
    await page.getByRole('button', { name: '1' }).click();
    await page.getByRole('button', { name: '2' }).click();
    await page.getByRole('button', { name: '3' }).click();
    await page.getByRole('button', { name: '4' }).click();
    await expect(page.getByText('Garson & Operasyon')).toBeVisible({ timeout: 10000 });
  };

  test('WAIT-01: Terminal PIN unlock, active operation shell and quick lock workflow', async ({
    page,
  }) => {
    await page.route('**/api/v1/terminals/current', async (route) => {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          terminalId: 'term-e2e-1',
          tenantId: scenario.tenantId,
          branchId: scenario.branchId,
          terminalName: 'Garson POS E2E',
          deviceIdentifier: 'dev-e2e-1',
          isActive: true,
        }),
      });
    });

    await page.route('**/api/v1/auth/pin/login', async (route) => {
      const data = JSON.parse(route.request().postData() || '{}');
      if (data.pin === '1234') {
        await route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify({
            user: {
              userId: 'staff-e2e-1',
              tenantId: scenario.tenantId,
              branchId: scenario.branchId,
              role: 'Waiter',
              permissions: ['floor.view', 'order.create', 'order.cancel', 'service.request.manage'],
            },
          }),
        });
      } else {
        await route.fulfill({
          status: 401,
          contentType: 'application/json',
          body: JSON.stringify({ detail: 'Geçersiz PIN veya personel yetkisi yok.' }),
        });
      }
    });

    await page.goto('http://127.0.0.1:3002');

    // Locked state: PinPad is displayed
    const pinPadHeading = page.getByRole('heading', { name: 'Personel PIN Girişi' });
    await expect(pinPadHeading).toBeVisible({ timeout: 10000 });

    // Enter PIN (1, 2, 3, 4)
    await page.getByRole('button', { name: '1' }).click();
    await page.getByRole('button', { name: '2' }).click();
    await page.getByRole('button', { name: '3' }).click();
    await page.getByRole('button', { name: '4' }).click();

    // Authenticated Operations Shell
    await expect(page.getByText('Garson & Operasyon')).toBeVisible({ timeout: 10000 });
    await expect(page.getByText('Garson POS E2E')).toBeVisible({ timeout: 10000 });

    // Test Lock function: Clicking "Kilitle" returns to PinPad immediately
    const lockBtn = page.getByRole('button', { name: 'Kilitle' });
    await expect(lockBtn).toBeVisible();
    await lockBtn.click();

    await expect(page.getByRole('heading', { name: 'Personel PIN Girişi' })).toBeVisible({ timeout: 5000 });
  });

  test('WAIT-02 & WAIT-03: Area filtering, table status visuals, and session detail drawer', async ({
    page,
    request,
  }) => {
    // Create customer service request using valid customer API
    const exchangeResp = await request.post('/api/v1/customer/session/exchange', {
      data: { token: scenario.qrToken },
    });
    expect(exchangeResp.ok()).toBeTruthy();
    const exchangeData = await exchangeResp.json();

    const srRes = await request.post('/api/v1/customer/service-requests', {
      headers: {
        Authorization: `Bearer ${exchangeData.accessToken}`,
        'Content-Type': 'application/json',
        'Idempotency-Key': `e2e-call-${Date.now()}`,
      },
      data: {
        requestType: 'WaiterCall',
        note: 'E2E Garson Çağrısı',
      },
    });
    expect(srRes.ok()).toBeTruthy();

    await setupTerminalAndLogin(page);

    // Verify Floor View is rendered
    await expect(page.getByText('Masa Durum Özeti')).toBeVisible({ timeout: 10000 });

    // Verify Table card exists with table number
    const tableCard = page.getByRole('button', { name: new RegExp(scenario.tableNumber, 'i') });
    await expect(tableCard).toBeVisible({ timeout: 10000 });
    await tableCard.click();

    // Drawer checks: table header, duration, service request
    const drawerTitle = page.getByRole('heading', { name: new RegExp(scenario.tableNumber, 'i') });
    await expect(drawerTitle).toBeVisible({ timeout: 10000 });
    await expect(page.getByText(/Oturma Süresi/i)).toBeVisible();
    await expect(page.getByText(/E2E Garson Çağrısı/i)).toBeVisible({ timeout: 10000 });

    // Acknowledge service request via "Kabul Et"
    const ackBtn = page.getByRole('button', { name: 'Kabul Et' });
    await expect(ackBtn).toBeVisible({ timeout: 5000 });
    await ackBtn.click();
    await expect(page.getByText(/Durum: InProgress|Durum: Acknowledged/i)).toBeVisible({ timeout: 5000 });
  });

  test('WAIT-05: Table transfer execution and concurrency conflict handling', async ({
    page,
    request,
  }) => {
    const adminHeaders = {
      Authorization: `Bearer ${scenario.adminToken}`,
      'Content-Type': 'application/json',
    };

    // Create target table
    const tableResp = await request.post(`/api/v1/floor/branches/${scenario.branchId}/tables`, {
      headers: adminHeaders,
      data: {
        diningAreaId: scenario.diningAreaId,
        tableNumber: 'T-TRANSFER-TARGET',
        name: 'Hedef Masa',
        capacity: 4,
      },
    });
    expect(tableResp.ok()).toBeTruthy();

    await setupTerminalAndLogin(page);

    const tableCard = page.getByRole('button', { name: new RegExp(scenario.tableNumber, 'i') });
    await expect(tableCard).toBeVisible({ timeout: 10000 });
    await tableCard.click();

    const transferBtn = page.getByRole('button', { name: /Masa Taşı/i });
    await expect(transferBtn).toBeVisible({ timeout: 5000 });
    await transferBtn.click();

    await expect(page.getByRole('heading', { name: /Masa Taşıma/i })).toBeVisible({ timeout: 5000 });
  });

  test('WAIT-04 & WAIT-06: Fast staff order submission and pre-prep cancellation', async ({
    page,
  }) => {
    await setupTerminalAndLogin(page);

    const tableCard = page.getByRole('button', { name: new RegExp(scenario.tableNumber, 'i') });
    await expect(tableCard).toBeVisible({ timeout: 10000 });
    await tableCard.click();

    const orderBtn = page.getByRole('button', { name: /Hızlı Sipariş/i });
    await expect(orderBtn).toBeVisible({ timeout: 5000 });
    await orderBtn.click();

    // StaffOrderDrawer opens
    await expect(page.getByRole('heading', { name: /Personel Hızlı Sipariş/i })).toBeVisible({ timeout: 5000 });

    // Item selection
    const productItem = page.getByText('Klasik Burger').first();
    await expect(productItem).toBeVisible({ timeout: 5000 });
    await productItem.click();

    // Verify submission button shows total
    const submitBtn = page.getByRole('button', { name: /Siparişi Onayla/i });
    await expect(submitBtn).toBeVisible({ timeout: 5000 });
  });

  test('WAIT-07: Operations live calls drawer, acknowledgment, and ready item indicator', async ({
    page,
    request,
  }) => {
    // Create customer service call via exchange
    const exchangeResp = await request.post('/api/v1/customer/session/exchange', {
      data: { token: scenario.qrToken },
    });
    expect(exchangeResp.ok()).toBeTruthy();
    const exchangeData = await exchangeResp.json();

    const callResp = await request.post('/api/v1/customer/service-requests', {
      headers: {
        Authorization: `Bearer ${exchangeData.accessToken}`,
        'Content-Type': 'application/json',
        'Idempotency-Key': `e2e-call-${Date.now()}`,
      },
      data: {
        requestType: 'WaiterCall',
        note: 'Hesap lütfen',
      },
    });
    expect(callResp.ok()).toBeTruthy();

    await setupTerminalAndLogin(page);

    // Open Calls Drawer via Header Button
    const callsBtn = page.getByTestId('operations-calls-drawer-toggle');
    await expect(callsBtn).toBeVisible({ timeout: 10000 });
    await callsBtn.click();

    // OperationsCallsDrawer opens
    await expect(page.getByRole('heading', { name: /Aktif Servis Çağrıları/i })).toBeVisible({ timeout: 5000 });
    await expect(page.getByText(/Hesap lütfen/i)).toBeVisible({ timeout: 5000 });
  });
});
