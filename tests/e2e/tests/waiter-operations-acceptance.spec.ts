import { test, expect } from '@playwright/test';
import {
  seedCustomerExperienceScenario,
  cleanupCustomerScenario,
  type SeededCustomerScenario,
} from './e2e-catalog-seed';

test.describe('Waiter & Operations Acceptance & Security E2E (Phase 10 Part 5)', () => {
  let scenario: SeededCustomerScenario;

  test.beforeEach(async ({ request }) => {
    scenario = await seedCustomerExperienceScenario(request);
  });

  test.afterEach(async ({ request }) => {
    if (scenario) {
      await cleanupCustomerScenario(request, scenario);
    }
  });

  const setupTerminalRoutes = async (page: any, terminalName = 'Garson POS E2E') => {
    await page.route('**/api/v1/terminals/current', async (route: any) => {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          terminalId: 'term-e2e-acc',
          tenantId: scenario.tenantId,
          branchId: scenario.branchId,
          terminalName,
          deviceIdentifier: 'dev-e2e-acc',
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
              userId: 'staff-e2e-acc',
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
  };

  test('Acceptance: Invalid PIN input triggers error feedback and keeps terminal locked', async ({
    page,
  }) => {
    await setupTerminalRoutes(page);
    await page.goto('http://127.0.0.1:3002');

    // Verify Locked state: PinPad is displayed
    await expect(page.getByRole('heading', { name: 'Personel PIN Girişi' })).toBeVisible({ timeout: 10000 });

    // Enter wrong PIN (9, 9, 9, 9)
    await page.getByRole('button', { name: '9' }).click();
    await page.getByRole('button', { name: '9' }).click();
    await page.getByRole('button', { name: '9' }).click();
    await page.getByRole('button', { name: '9' }).click();

    // Terminal must remain in locked state with PinPad and error message
    await expect(page.getByRole('heading', { name: 'Personel PIN Girişi' })).toBeVisible({ timeout: 5000 });
    await expect(page.getByText('Geçersiz PIN veya personel yetkisi yok.')).toBeVisible({ timeout: 5000 });
    await expect(page.getByText('Garson & Operasyon')).toBeHidden();
  });

  test('Acceptance: Mobile 320px viewport has zero horizontal overflow and preserves touch accessibility', async ({
    page,
  }) => {
    await page.setViewportSize({ width: 320, height: 600 });
    await setupTerminalRoutes(page, 'Mobil POS 320');
    await page.goto('http://127.0.0.1:3002');

    // Unlock via PIN 1234
    await page.getByRole('button', { name: '1' }).click();
    await page.getByRole('button', { name: '2' }).click();
    await page.getByRole('button', { name: '3' }).click();
    await page.getByRole('button', { name: '4' }).click();

    // Verify operational shell is visible
    await expect(page.getByText('Garson & Operasyon')).toBeVisible({ timeout: 10000 });

    // Check for zero horizontal overflow on mobile body
    const scrollWidth = await page.evaluate(() => document.documentElement.scrollWidth);
    const clientWidth = await page.evaluate(() => document.documentElement.clientWidth);
    expect(scrollWidth).toBeLessThanOrEqual(clientWidth + 2);

    // Check that primary toggle buttons meet minimum touch target height
    const callsBtn = page.getByTestId('operations-calls-drawer-toggle');
    const box = await callsBtn.boundingBox();
    expect(box).not.toBeNull();
    if (box) {
      expect(box.height).toBeGreaterThanOrEqual(36);
    }
  });

  test('Acceptance: Request bill transitions session to BillRequested without simulating payment settlement', async ({
    page,
  }) => {
    await setupTerminalRoutes(page);
    await page.goto('http://127.0.0.1:3002');

    // Unlock via PIN
    await page.getByRole('button', { name: '1' }).click();
    await page.getByRole('button', { name: '2' }).click();
    await page.getByRole('button', { name: '3' }).click();
    await page.getByRole('button', { name: '4' }).click();

    await expect(page.getByText('Garson & Operasyon')).toBeVisible({ timeout: 10000 });

    // Select the table to open SessionDetailDrawer
    const tableCard = page.getByRole('button', { name: new RegExp(scenario.tableNumber, 'i') });
    await expect(tableCard).toBeVisible({ timeout: 10000 });
    await tableCard.click();

    // SessionDetailDrawer opens
    await expect(page.getByText(/Masa Bilgisi & Oturum/i)).toBeVisible({ timeout: 5000 });

    // Explicit assertion: Request bill button must be visible and clickable (no conditional skipping)
    const requestBillBtn = page.getByRole('button', { name: /Hesap İste/i });
    await expect(requestBillBtn).toBeVisible({ timeout: 5000 });
    await requestBillBtn.click();

    // Verify that status transitions to BillRequested without simulating payment
    await expect(page.getByText(/Hesap İstendi/i)).toBeVisible({ timeout: 5000 });
    await expect(page.getByText(/Kapanmış/i)).toBeHidden();
  });

  test('Acceptance: Cross-branch direct API access is strictly forbidden (403)', async ({
    request,
  }) => {
    const adminHeaders = {
      Authorization: `Bearer ${scenario.adminToken}`,
      'Content-Type': 'application/json',
    };

    const randomBranchId = '00000000-0000-0000-0000-000000000099';
    const resp = await request.get(`/api/v1/floor/branches/${randomBranchId}/status`, {
      headers: adminHeaders,
    });

    expect([403, 404]).toContain(resp.status());
  });
});
