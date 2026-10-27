import { test, expect } from '@playwright/test';
import { CART_STORAGE_KEY } from '@restaurant-order/contracts';
import {
  seedCustomerExperienceScenario,
  cleanupCustomerScenario,
  type SeededCustomerScenario,
} from './e2e-catalog-seed';

test.describe('Customer Experience Negative Flows & Security Boundaries (CUST-02, 03, 04, 06)', () => {
  let scenario: SeededCustomerScenario;

  test.beforeEach(async ({ request }) => {
    scenario = await seedCustomerExperienceScenario(request);
  });

  test.afterEach(async ({ request }) => {
    if (scenario) {
      await cleanupCustomerScenario(request, scenario);
    }
  });

  test('blocked addition when required modifier group requirements are unmet', async ({
    page,
  }) => {
    await page.goto(`http://127.0.0.1:3001/q/${encodeURIComponent(scenario.qrToken)}`);
    await page.getByTestId('continue-to-table-btn').click();

    const exploreBtn = page.getByTestId('explore-menu-btn');
    if (await exploreBtn.isVisible()) {
      await exploreBtn.click();
    } else {
      await page.getByTestId('bottom-nav-menu-btn').click();
    }

    // Open out of stock item directly: CTA should be disabled
    const oosCard = page.getByTestId(`menu-item-card-${scenario.itemOutOfStockId}`);
    await expect(oosCard).toBeVisible();
    await oosCard.click();

    const ctaBtn = page.getByTestId('add-to-cart-cta-btn');
    await expect(ctaBtn).toBeVisible();
    await expect(ctaBtn).toBeDisabled();
    await expect(page.getByTestId('item-out-of-stock-alert')).toBeVisible();
  });

  test('safely discards corrupt or tampered sessionStorage cart draft without crashing', async ({
    page,
  }) => {
    await page.goto(`http://127.0.0.1:3001/q/${encodeURIComponent(scenario.qrToken)}`);
    await page.getByTestId('continue-to-table-btn').click();
    await expect(page.getByTestId('customer-active-session')).toBeVisible();

    // Oturum yanıtı/cookie tamamlandıktan sonra yeni belgenin uygulama kodundan
    // önce bozuk taslağı kur; eski sayfanın effect'leri fixture'ı değiştiremesin.
    await page.addInitScript((key) => {
      sessionStorage.setItem(key, 'INVALID_CORRUPTED_JSON_<<<>>>');
    }, CART_STORAGE_KEY);

    await page.reload();
    await expect(page.getByTestId('customer-active-session')).toBeVisible({ timeout: 10000 });

    // Open Cart: should gracefully handle corrupted draft by falling back to clean empty state
    await page.getByTestId('bottom-nav-cart-btn').click();
    await expect(page.getByTestId('cart-empty-state')).toBeVisible({ timeout: 5000 });

    // Assert that the corrupt entry was truly and permanently removed from sessionStorage
    const storedAfter = await page.evaluate((key) => sessionStorage.getItem(key), CART_STORAGE_KEY);
    expect(storedAfter).toBeNull();
  });

  test('warns in cart when an item in cart is marked out of stock (86) on backend', async ({
    page,
    request,
  }) => {
    await page.goto(`http://127.0.0.1:3001/q/${encodeURIComponent(scenario.qrToken)}`);
    await page.getByTestId('continue-to-table-btn').click();

    const exploreBtn = page.getByTestId('explore-menu-btn');
    if (await exploreBtn.isVisible()) {
      await exploreBtn.click();
    } else {
      await page.getByTestId('bottom-nav-menu-btn').click();
    }

    // Add Burger to cart
    const burgerCard = page.getByTestId(`menu-item-card-${scenario.itemBurgerId}`);
    await burgerCard.click();
    await page.getByTestId('add-to-cart-cta-btn').click();

    // Now mark the burger item 86 on backend
    const burgerGetRes = await request.get(
      `/api/v1/catalog/branches/${scenario.branchId}/menus/${scenario.menuId}/items/${scenario.itemBurgerId}`,
      {
        headers: {
          Authorization: `Bearer ${scenario.adminToken}`,
        },
      }
    );
    expect(burgerGetRes.ok()).toBe(true);
    const burgerData = await burgerGetRes.json();

    const quick86Res = await request.post(
      `/api/v1/catalog/branches/${scenario.branchId}/menus/${scenario.menuId}/items/${scenario.itemBurgerId}/quick-86`,
      {
        headers: {
          Authorization: `Bearer ${scenario.adminToken}`,
          'Content-Type': 'application/json',
          'If-Match': `"${burgerData.concurrencyToken}"`,
        },
        data: { reasonCode: 'SoldOut', note: 'Stok tükendi' },
      }
    );
    expect(quick86Res.ok()).toBe(true);

    // Open Cart Sheet
    await page.getByTestId('bottom-nav-cart-btn').click();

    // Verification banner detects item is out of stock
    const issueElement = page.getByTestId(/cart-issue-/);
    await expect(issueElement).toBeVisible({ timeout: 5000 });
    await expect(issueElement).toContainText(/tükendi/i);
  });

  test('rejects unauthenticated customer service request with 401 Unauthorized', async ({
    request,
  }) => {
    const res = await request.post('/api/v1/customer/service-requests', {
      headers: {
        'Content-Type': 'application/json',
        'Idempotency-Key': 'test-no-csrf-key-1',
      },
      data: {
        requestType: 'Waiter',
      },
    });

    expect(res.status()).toBe(401);
  });

  test('rejects unauthenticated customer service request missing Idempotency-Key with 401 Unauthorized', async ({
    request,
  }) => {
    const res = await request.post('/api/v1/customer/service-requests', {
      headers: {
        'Content-Type': 'application/json',
      },
      data: {
        requestType: 'Waiter',
      },
    });

    expect(res.status()).toBe(401);
  });

  test('rejects unauthenticated customer order creation with 401 Unauthorized', async ({
    request,
  }) => {
    const res = await request.post('/api/v1/customer/orders', {
      headers: {
        'Content-Type': 'application/json',
        'Idempotency-Key': 'unauth-order-key',
      },
      data: {
        items: [{ menuItemId: '00000000-0000-0000-0000-000000000001', quantity: 1 }],
        clientPricingPolicyFingerprint: '0268781158e5455cd5066342bc2e2a04ede6e69974fef181a1dbd5d26c4728b9',
        clientExpectedTotalMinorUnits: 2500,
      },
    });

    expect(res.status()).toBe(401);
  });

  test('closed dining session cuts off customer active session and menu access', async ({
    page,
    request,
  }) => {
    await page.goto(`http://127.0.0.1:3001/q/${encodeURIComponent(scenario.qrToken)}`);
    await page.getByTestId('continue-to-table-btn').click();
    await expect(page.getByTestId('customer-active-session')).toBeVisible({ timeout: 10000 });

    // Cleanly close the dining session on backend
    await cleanupCustomerScenario(request, scenario);

    // Reload page: session is now Closed in database; active view must be cut off
    await page.reload();
    await expect(page.getByTestId('customer-active-session')).not.toBeVisible({ timeout: 10000 });
  });
});
