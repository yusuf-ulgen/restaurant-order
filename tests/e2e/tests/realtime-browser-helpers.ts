import { expect } from '@playwright/test';
import type { SeededCustomerScenario } from './e2e-catalog-seed';

export async function enterCustomerSession(page: any, scenario: SeededCustomerScenario) {
  await page.goto(`http://127.0.0.1:3001/q/${encodeURIComponent(scenario.qrToken)}`);
  const resolveView = page.getByTestId('table-resolve-view');
  await expect(resolveView).toBeVisible({ timeout: 10000 });
  await expect(resolveView.getByText(scenario.tableNumber).first()).toBeVisible({ timeout: 10000 });

  const continueBtn = page.getByTestId('continue-to-table-btn');
  await expect(continueBtn).toBeVisible({ timeout: 10000 });
  await continueBtn.click();

  const sessionOverview = page.getByTestId('customer-active-session');
  await expect(sessionOverview).toBeVisible({ timeout: 10000 });
  return sessionOverview;
}

export async function transitionSessionToClosed(request: any, scenario: SeededCustomerScenario, reason: string): Promise<void> {
  const sessionRes = await request.get(
    `/api/v1/floor/branches/${scenario.branchId}/sessions/${scenario.sessionId}`,
    { headers: { Authorization: `Bearer ${scenario.adminToken}` } }
  );
  expect(sessionRes.status()).toBe(200);
  const sessionData = await sessionRes.json();
  let currentToken = sessionData.concurrencyToken;
  let currentStatus = sessionData.status;

  if (currentStatus === 'Open') {
    const actRes = await request.post(
      `/api/v1/floor/branches/${scenario.branchId}/sessions/${scenario.sessionId}/activate`,
      {
        headers: {
          Authorization: `Bearer ${scenario.adminToken}`,
          'If-Match': `"${currentToken}"`,
          'Content-Type': 'application/json',
        },
        data: { concurrencyToken: currentToken },
      }
    );
    expect([200, 204]).toContain(actRes.status());
    const actData = await actRes.json();
    currentToken = actData.concurrencyToken;
    currentStatus = 'Active';
  }

  if (currentStatus === 'Active') {
    const billRes = await request.post(
      `/api/v1/floor/branches/${scenario.branchId}/sessions/${scenario.sessionId}/request-bill`,
      {
        headers: {
          Authorization: `Bearer ${scenario.adminToken}`,
          'If-Match': `"${currentToken}"`,
          'Content-Type': 'application/json',
        },
        data: { concurrencyToken: currentToken },
      }
    );
    expect([200, 204]).toContain(billRes.status());
    const billData = await billRes.json();
    currentToken = billData.concurrencyToken;
  }

  const closeRes = await request.post(
    `/api/v1/floor/branches/${scenario.branchId}/sessions/${scenario.sessionId}/close`,
    {
      headers: {
        Authorization: `Bearer ${scenario.adminToken}`,
        'If-Match': `"${currentToken}"`,
        'Content-Type': 'application/json',
      },
      data: {
        reason,
        concurrencyToken: currentToken,
      },
    }
  );
  expect([200, 204]).toContain(closeRes.status());
}
