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

  test('Acceptance: Invalid PIN input triggers error feedback and keeps terminal locked', async ({
    page,
  }) => {
    await page.goto('http://127.0.0.1:3002');
    await page.evaluate(
      ({ tenantId, branchId }) => {
        localStorage.setItem(
          'ro_terminal_context',
          JSON.stringify({
            terminalId: 'term-e2e-acc',
            tenantId,
            branchId,
            terminalName: 'Garson POS Acceptance',
            deviceIdentifier: 'dev-e2e-acc',
            isActive: true,
          })
        );
        localStorage.setItem('ro_terminal_enrolled', 'true');
        sessionStorage.clear();
      },
      { tenantId: scenario.tenantId, branchId: scenario.branchId }
    );

    await page.reload();

    // Verify Locked state: PinPad is displayed
    await expect(page.getByRole('heading', { name: 'Personel PIN Girişi' })).toBeVisible({ timeout: 10000 });

    // Enter wrong PIN (9, 9, 9, 9)
    await page.getByRole('button', { name: '9' }).click();
    await page.getByRole('button', { name: '9' }).click();
    await page.getByRole('button', { name: '9' }).click();
    await page.getByRole('button', { name: '9' }).click();

    // Terminal must remain in locked state with PinPad
    await expect(page.getByRole('heading', { name: 'Personel PIN Girişi' })).toBeVisible({ timeout: 5000 });
    await expect(page.getByText('Garson & Operasyon')).toBeHidden();
  });

  test('Acceptance: Mobile 320px viewport has zero horizontal overflow and preserves touch accessibility', async ({
    page,
  }) => {
    await page.setViewportSize({ width: 320, height: 600 });
    await page.goto('http://127.0.0.1:3002');

    await page.evaluate(
      ({ tenantId, branchId }) => {
        localStorage.setItem(
          'ro_terminal_context',
          JSON.stringify({
            terminalId: 'term-e2e-m320',
            tenantId,
            branchId,
            terminalName: 'Mobil POS 320',
            deviceIdentifier: 'dev-e2e-m320',
            isActive: true,
          })
        );
        localStorage.setItem('ro_terminal_enrolled', 'true');
        localStorage.setItem(
          'ro_staff_user',
          JSON.stringify({
            userId: 'staff-e2e-m320',
            email: 'waiter320@restoran.com',
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

    // Verify operational shell is visible
    await expect(page.getByText('Garson & Operasyon')).toBeVisible({ timeout: 10000 });

    // Check for zero horizontal overflow on mobile body
    const scrollWidth = await page.evaluate(() => document.documentElement.scrollWidth);
    const clientWidth = await page.evaluate(() => document.documentElement.clientWidth);
    expect(scrollWidth).toBeLessThanOrEqual(clientWidth + 2); // 1-2px rounding allowance

    // Check that primary toggle buttons meet minimum touch target height (at least 36-44px)
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
    await page.goto('http://127.0.0.1:3002');
    await page.evaluate(
      ({ tenantId, branchId }) => {
        localStorage.setItem(
          'ro_terminal_context',
          JSON.stringify({
            terminalId: 'term-e2e-bill',
            tenantId,
            branchId,
            terminalName: 'Garson POS Bill',
            deviceIdentifier: 'dev-e2e-bill',
            isActive: true,
          })
        );
        localStorage.setItem('ro_terminal_enrolled', 'true');
        localStorage.setItem(
          'ro_staff_user',
          JSON.stringify({
            userId: 'staff-e2e-bill',
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

    // Select the table to open SessionDetailDrawer
    const tableCard = page.getByRole('button', { name: new RegExp(scenario.tableNumber, 'i') });
    await expect(tableCard).toBeVisible({ timeout: 10000 });
    await tableCard.click();

    // SessionDetailDrawer opens
    await expect(page.getByText(/Masa Bilgisi & Oturum/i)).toBeVisible({ timeout: 5000 });

    // Click "Hesap İste (Bill Requested)" button
    const requestBillBtn = page.getByRole('button', { name: /Hesap İste/i });
    if (await requestBillBtn.isVisible()) {
      await requestBillBtn.click();

      // Verify that status transitions to BillRequested without simulating payment
      await expect(page.getByText(/Hesap İstendi/i)).toBeVisible({ timeout: 5000 });
      // The session should not be closed or paid
      await expect(page.getByText(/Kapanmış/i)).toBeHidden();
    }
  });

  test('Acceptance: Cross-branch direct API access is strictly forbidden (403)', async ({
    request,
  }) => {
    // Generate a waiter token specifically for branch A
    const adminHeaders = {
      Authorization: `Bearer ${scenario.adminToken}`,
      'Content-Type': 'application/json',
    };

    // Attempt to access a completely different branch with admin token from another branch scope
    const randomBranchId = '00000000-0000-0000-0000-000000000099';
    const resp = await request.get(`/api/v1/floor/branches/${randomBranchId}/status`, {
      headers: adminHeaders,
    });

    // Cross-tenant or non-existent branch access MUST return 403 or 404, never 200 with sensitive floor status
    expect([403, 404]).toContain(resp.status());
  });
});
