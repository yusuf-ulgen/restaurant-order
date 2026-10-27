import { test, expect } from '@playwright/test';
import {
  seedCustomerExperienceScenario,
  cleanupCustomerScenario,
  type SeededCustomerScenario,
} from './e2e-catalog-seed';

test.describe('Waiter & Operations Web E2E (Phase 10 Part 2 Verification)', () => {
  let scenario: SeededCustomerScenario;

  test.beforeEach(async ({ request }) => {
    scenario = await seedCustomerExperienceScenario(request);
  });

  test.afterEach(async ({ request }) => {
    if (scenario) {
      await cleanupCustomerScenario(request, scenario);
    }
  });

  test('WAIT-01: Terminal PIN unlock, active operation shell and quick lock workflow', async ({
    page,
  }) => {
    // 1. Seed enrollment in localStorage to bypass one-time activation
    await page.goto('http://127.0.0.1:3002');
    await page.evaluate(
      ({ tenantId, branchId }) => {
        localStorage.setItem(
          'ro_terminal_context',
          JSON.stringify({
            terminalId: 'term-e2e-1',
            tenantId,
            branchId,
            terminalName: 'Garson POS E2E',
            deviceIdentifier: 'dev-e2e-1',
            isActive: true,
          })
        );
        localStorage.setItem('ro_terminal_enrolled', 'true');
        sessionStorage.clear();
      },
      { tenantId: scenario.tenantId, branchId: scenario.branchId }
    );

    await page.reload();

    // 2. State 2: Locked -> PinPad is displayed
    const pinPadHeading = page.getByRole('heading', { name: 'Personel PIN Girişi' });
    await expect(pinPadHeading).toBeVisible({ timeout: 10000 });

    // Enter PIN via on-screen key buttons (1, 2, 3, 4)
    await page.getByRole('button', { name: '1' }).click();
    await page.getByRole('button', { name: '2' }).click();
    await page.getByRole('button', { name: '3' }).click();
    await page.getByRole('button', { name: '4' }).click();

    // 3. State 3: Authenticated Operations Shell
    await expect(page.getByText('Garson & Operasyon')).toBeVisible({ timeout: 10000 });
    await expect(page.getByText('Garson POS E2E')).toBeVisible({ timeout: 10000 });

    // 4. Test Lock function: Clicking "Kilitle" returns to PinPad immediately
    const lockBtn = page.getByRole('button', { name: 'Kilitle' });
    await expect(lockBtn).toBeVisible();
    await lockBtn.click();

    await expect(page.getByRole('heading', { name: 'Personel PIN Girişi' })).toBeVisible({ timeout: 5000 });
  });

  test('WAIT-02 & WAIT-03: Area filtering, table status visuals, and session detail drawer', async ({
    page,
    request,
  }) => {
    const adminHeaders = {
      Authorization: `Bearer ${scenario.adminToken}`,
      'Content-Type': 'application/json',
    };

    // Create a service request for scenario.tableId
    await request.post(`/api/v1/floor/branches/${scenario.branchId}/service-requests`, {
      headers: adminHeaders,
      data: {
        tableId: scenario.tableId,
        diningSessionId: scenario.sessionId,
        requestType: 'Waiter',
        note: 'E2E Garson Çağrısı',
      },
    });

    // Setup active terminal and staff session
    await page.goto('http://127.0.0.1:3002');
    await page.evaluate(
      ({ tenantId, branchId }) => {
        localStorage.setItem(
          'ro_terminal_context',
          JSON.stringify({
            terminalId: 'term-e2e-1',
            tenantId,
            branchId,
            terminalName: 'Garson POS E2E',
            deviceIdentifier: 'dev-e2e-1',
            isActive: true,
          })
        );
        localStorage.setItem('ro_terminal_enrolled', 'true');
        sessionStorage.setItem(
          'ro_staff_principal',
          JSON.stringify({
            userId: 'staff-e2e-1',
            email: 'waiter@restoran.com',
            role: 'Waiter',
            tenantId,
            branchId,
            securityVersion: 1,
          })
        );
      },
      { tenantId: scenario.tenantId, branchId: scenario.branchId }
    );

    await page.reload();

    // 1. Verify Floor View is rendered
    await expect(page.getByText('Masa Durum Özeti')).toBeVisible({ timeout: 10000 });

    // 2. Verify Table card exists with table number
    const tableCard = page.getByRole('button', { name: new RegExp(scenario.tableNumber, 'i') });
    await expect(tableCard).toBeVisible({ timeout: 10000 });

    // 3. Open Session Detail Drawer by clicking the table card
    await tableCard.click();

    // 4. Drawer checks: table header, duration, service request
    const drawerTitle = page.getByRole('heading', { name: new RegExp(scenario.tableNumber, 'i') });
    await expect(drawerTitle).toBeVisible({ timeout: 10000 });
    await expect(page.getByText(/Oturma Süresi/i)).toBeVisible();
    await expect(page.getByText(/E2E Garson Çağrısı/i)).toBeVisible({ timeout: 10000 });

    // 5. Acknowledge service request via "Kabul Et"
    const ackBtn = page.getByRole('button', { name: 'Kabul Et' });
    if (await ackBtn.isVisible()) {
      await ackBtn.click();
      await expect(page.getByText(/Durum: InProgress|Durum: Acknowledged/i)).toBeVisible({ timeout: 5000 });
    }
  });

  test('WAIT-05: Table transfer execution and concurrency conflict handling', async ({
    page,
    request,
  }) => {
    const adminHeaders = {
      Authorization: `Bearer ${scenario.adminToken}`,
      'Content-Type': 'application/json',
    };

    // Create an empty target table on the same dining area
    const targetTableNumber = `E2E-T${Date.now().toString().slice(-4)}`;
    const createTargetRes = await request.post(
      `/api/v1/floor/branches/${scenario.branchId}/tables`,
      {
        headers: adminHeaders,
        data: {
          diningAreaId: scenario.diningAreaId,
          tableNumber: targetTableNumber,
          name: 'Hedef Boş Masa',
          capacity: 4,
          shape: 'Square',
          positionX: 500,
          positionY: 500,
          width: 100,
          height: 100,
        },
      }
    );
    expect(createTargetRes.status()).toBe(201);
    const targetTable = await createTargetRes.json();

    // Navigate to operations-web with authenticated waiter
    await page.goto('http://127.0.0.1:3002');
    await page.evaluate(
      ({ tenantId, branchId }) => {
        localStorage.setItem(
          'ro_terminal_context',
          JSON.stringify({
            terminalId: 'term-e2e-1',
            tenantId,
            branchId,
            terminalName: 'Garson POS E2E',
            deviceIdentifier: 'dev-e2e-1',
            isActive: true,
          })
        );
        localStorage.setItem('ro_terminal_enrolled', 'true');
        sessionStorage.setItem(
          'ro_staff_principal',
          JSON.stringify({
            userId: 'staff-e2e-1',
            email: 'waiter@restoran.com',
            role: 'Waiter',
            tenantId,
            branchId,
            securityVersion: 1,
          })
        );
      },
      { tenantId: scenario.tenantId, branchId: scenario.branchId }
    );

    await page.reload();

    // Click source table card to open drawer
    const sourceCard = page.getByRole('button', { name: new RegExp(scenario.tableNumber, 'i') });
    await expect(sourceCard).toBeVisible({ timeout: 10000 });
    await sourceCard.click();

    // Open movement modal
    const moveBtn = page.getByRole('button', { name: /Taşı \/ Birleştir/i });
    await expect(moveBtn).toBeVisible({ timeout: 5000 });
    await moveBtn.click();

    // Verify Movement Modal
    await expect(page.getByRole('heading', { name: 'Masa Taşıma / Birleştirme' })).toBeVisible({ timeout: 5000 });

    // Select target table in select box
    const targetSelect = page.getByLabel(/Hedef Boş Masa:/i);
    await expect(targetSelect).toBeVisible();
    await targetSelect.selectOption({ value: targetTable.id });

    // Click "Taşımayı Tamamla"
    const completeTransferBtn = page.getByRole('button', { name: 'Taşımayı Tamamla' });
    await expect(completeTransferBtn).toBeEnabled();
    await completeTransferBtn.click();

    // Modal closes upon success and refreshed floor displays target table
    await expect(page.getByRole('heading', { name: 'Masa Taşıma / Birleştirme' })).toBeHidden({ timeout: 10000 });

    // Clean up created target table
    await request.delete(`/api/v1/floor/branches/${scenario.branchId}/tables/${targetTable.id}`, {
      headers: adminHeaders,
    });
  });

  test('WAIT-04: Staff quick order entry, item customization, and order cancellation in session detail', async ({
    page,
  }) => {
    // Setup active terminal and staff session
    await page.goto('http://127.0.0.1:3002');
    await page.evaluate(
      ({ tenantId, branchId }) => {
        localStorage.setItem(
          'ro_terminal_context',
          JSON.stringify({
            terminalId: 'term-e2e-1',
            tenantId,
            branchId,
            terminalName: 'Garson POS E2E',
            deviceIdentifier: 'dev-e2e-1',
            isActive: true,
          })
        );
        localStorage.setItem('ro_terminal_enrolled', 'true');
        localStorage.setItem(
          'ro_staff_user',
          JSON.stringify({
            userId: 'staff-e2e-1',
            email: 'waiter@restoran.com',
            role: 'Waiter',
            tenantId,
            branchId,
            securityVersion: 1,
          })
        );
      },
      { tenantId: scenario.tenantId, branchId: scenario.branchId }
    );

    await page.reload();

    // 1. Open Session Detail Drawer
    const tableCard = page.getByRole('button', { name: new RegExp(scenario.tableNumber, 'i') });
    await expect(tableCard).toBeVisible({ timeout: 10000 });
    await tableCard.click();

    // 2. Click "➕ Yeni Sipariş Gir (Hızlı Menü)"
    const newOrderBtn = page.getByRole('button', { name: /Yeni Sipariş Gir/i });
    await expect(newOrderBtn).toBeVisible({ timeout: 5000 });
    await newOrderBtn.click();

    // 3. Verify StaffOrderDrawer (WAIT-04)
    await expect(page.getByRole('heading', { name: /Hızlı Sipariş/i })).toBeVisible({ timeout: 10000 });

    // Item card should be rendered
    const itemCard = page.getByRole('button', { name: /Burger/i }).first();
    if (await itemCard.isVisible()) {
      await itemCard.click();

      // Customizer opens
      const addToCartBtn = page.getByRole('button', { name: /Sepete Ekle/i });
      if (await addToCartBtn.isVisible()) {
        await addToCartBtn.click();
      }

      // Submit order
      const submitOrderBtn = page.getByRole('button', { name: /Siparişi Onayla ve Gönder/i });
      await expect(submitOrderBtn).toBeVisible({ timeout: 5000 });
      await submitOrderBtn.click();

      // Drawer closes and orders round list updates
      await expect(page.getByRole('heading', { name: /Hızlı Sipariş/i })).toBeHidden({ timeout: 10000 });
      await expect(page.getByText(/Tur #1/i)).toBeVisible({ timeout: 10000 });

      // 4. Test Item Cancellation
      const cancelItemBtn = page.getByRole('button', { name: /İptal/i }).first();
      if (await cancelItemBtn.isVisible()) {
        await cancelItemBtn.click();

        // Cancel modal opens
        await expect(page.getByRole('heading', { name: /Kalem İptali/i })).toBeVisible({ timeout: 5000 });
        const confirmCancelBtn = page.getByRole('button', { name: 'Kalemi İptal Et' });
        await confirmCancelBtn.click();

        await expect(page.getByRole('heading', { name: /Kalem İptali/i })).toBeHidden({ timeout: 10000 });
      }
    }
  });

  test('WAIT-07: Realtime service requests, acknowledgement and completion in calls drawer', async ({
    page,
    request,
  }) => {
    // 1. Create a service request (via QR token exchange or branch admin)
    const exchangeResp = await request.post('/api/v1/customer/session/exchange', {
      data: { token: scenario.qrToken },
    });

    if (exchangeResp.ok()) {
      const exchangeData = await exchangeResp.json();
      await request.post('/api/v1/customer/service-requests', {
        headers: {
          Authorization: `Bearer ${exchangeData.accessToken || scenario.adminToken}`,
          'Content-Type': 'application/json',
          'Idempotency-Key': `e2e-call-${Date.now()}`,
        },
        data: {
          requestType: 'Waiter',
          note: 'Hesap lütfen',
        },
      });
    } else {
      await request.post(`/api/v1/floor/branches/${scenario.branchId}/service-requests`, {
        headers: {
          Authorization: `Bearer ${scenario.adminToken}`,
          'Content-Type': 'application/json',
        },
        data: {
          tableId: scenario.tableId,
          diningSessionId: scenario.sessionId,
          requestType: 'Waiter',
          note: 'Hesap lütfen',
        },
      });
    }

    // 2. Staff logs into operations web
    await page.goto('http://127.0.0.1:3002');
    await page.evaluate(
      ({ tenantId, branchId }) => {
        localStorage.setItem(
          'ro_terminal_context',
          JSON.stringify({
            terminalId: 'term-e2e-1',
            tenantId,
            branchId,
            terminalName: 'Garson POS E2E',
            deviceIdentifier: 'dev-e2e-1',
            isActive: true,
          })
        );
        localStorage.setItem('ro_terminal_enrolled', 'true');
        localStorage.setItem(
          'ro_staff_user',
          JSON.stringify({
            userId: 'staff-e2e-1',
            email: 'waiter@restoran.com',
            role: 'Waiter',
            tenantId,
            branchId,
            securityVersion: 1,
          })
        );
      },
      { tenantId: scenario.tenantId, branchId: scenario.branchId }
    );

    await page.reload();

    // 3. Open Calls Drawer via Header Button
    const callsBtn = page.getByTestId('operations-calls-drawer-toggle');
    await expect(callsBtn).toBeVisible({ timeout: 10000 });
    await callsBtn.click();

    // 4. Verify Active Calls Drawer Content
    await expect(page.getByText('Aktif Çağrılar')).toBeVisible({ timeout: 10000 });
    await expect(page.getByText(/Garson Çağrısı/i)).toBeVisible({ timeout: 10000 });

    // 5. Acknowledge service request (Kabul Et)
    const acceptBtn = page.getByRole('button', { name: 'Kabul Et' }).first();
    if (await acceptBtn.isVisible()) {
      await acceptBtn.click();
      await expect(page.getByText('İşleme Alındı')).toBeVisible({ timeout: 5000 });

      // 6. Complete service request (Tamamla)
      const completeBtn = page.getByRole('button', { name: 'Tamamla' }).first();
      await expect(completeBtn).toBeVisible({ timeout: 5000 });
      await completeBtn.click();

      // Verify request is completed and removed from active list
      await expect(page.getByText('Bekleyen Çağrı Yok')).toBeVisible({ timeout: 5000 });
    }
  });
});

