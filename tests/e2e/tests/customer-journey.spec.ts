import { test, expect } from '@playwright/test';
import {
  seedCustomerExperienceScenario,
  cleanupCustomerScenario,
  type SeededCustomerScenario,
} from './e2e-catalog-seed';

test.describe('Customer Experience End-to-End Journey (CUST-02, CUST-03, CUST-04, CUST-06)', () => {
  let scenario: SeededCustomerScenario;

  test.beforeEach(async ({ request }) => {
    scenario = await seedCustomerExperienceScenario(request);
  });

  test.afterEach(async ({ request }) => {
    if (scenario) {
      await cleanupCustomerScenario(request, scenario);
    }
  });

  test('executes complete 15-step customer journey with persistent security and draft recovery', async ({
    page,
  }) => {
    // 1. QR scan: visit landing URL with signed token
    await page.goto(`http://127.0.0.1:3001/q/${encodeURIComponent(scenario.qrToken)}`);

    // 2. Open Session: resolve table info and continue to dining session
    const resolveView = page.getByTestId('table-resolve-view');
    await expect(resolveView).toBeVisible({ timeout: 10000 });
    await expect(resolveView.getByText(scenario.tableNumber).first()).toBeVisible();

    const continueBtn = page.getByTestId('continue-to-table-btn');
    await expect(continueBtn).toBeVisible();
    await continueBtn.click();

    const sessionOverview = page.getByTestId('customer-active-session');
    await expect(sessionOverview).toBeVisible({ timeout: 10000 });

    // 3. Navigate to Menu
    const exploreBtn = page.getByTestId('explore-menu-btn');
    if (await exploreBtn.isVisible()) {
      await exploreBtn.click();
    } else {
      await page.getByTestId('bottom-nav-menu-btn').click();
    }
    const digitalMenuView = page.getByTestId('digital-menu-view');
    await expect(digitalMenuView).toBeVisible({ timeout: 10000 });

    // 4. Select Category
    const drinkPill = page.getByTestId(`category-pill-${scenario.categoryIdDrink}`);
    await expect(drinkPill).toBeVisible();
    await drinkPill.click();

    const drinkCard = page.getByTestId(`menu-item-card-${scenario.itemDrinkId}`);
    await expect(drinkCard).toBeVisible();

    const burgerPill = page.getByTestId(`category-pill-${scenario.categoryIdBurger}`);
    await expect(burgerPill).toBeVisible();
    await burgerPill.click();

    const burgerCard = page.getByTestId(`menu-item-card-${scenario.itemBurgerId}`);
    await expect(burgerCard).toBeVisible();

    // 5. Use Search and Filter
    const searchInput = page.getByLabel('Menüde veya lezzetlerde ara');
    await expect(searchInput).toBeVisible();
    await searchInput.fill('Gourmet');
    await expect(burgerCard).toBeVisible();
    await expect(drinkCard).not.toBeVisible();

    // Test filter bottom sheet opening and closing
    const openFiltersBtn = page.getByTestId('open-filters-btn');
    await expect(openFiltersBtn).toBeVisible();
    await openFiltersBtn.click();
    const filterSheet = page.getByTestId('menu-filter-bottom-sheet');
    await expect(filterSheet).toBeVisible();
    const applyFiltersBtn = page.getByTestId('apply-filter-sheet-btn');
    await applyFiltersBtn.click();

    // Clear search
    await searchInput.fill('');
    await expect(burgerCard).toBeVisible();

    // 6. Open Item Detail Sheet
    await burgerCard.click();
    const customizationCta = page.getByTestId('add-to-cart-cta-btn');
    await expect(customizationCta).toBeVisible({ timeout: 10000 });

    // Verify allergen information rendered
    await expect(page.getByText('Susam içerir')).toBeVisible();

    // 7. Select Variant & Required Modifier
    const doubleVariant = page.getByTestId(`variant-option-${scenario.variantDoubleId}`);
    await expect(doubleVariant).toBeVisible();
    await doubleVariant.click();

    // Required modifier: Cheddar is preselected or selectable
    const cheddarOption = page.getByTestId(/modifier-option-/).first();
    await expect(cheddarOption).toBeVisible();
    await cheddarOption.click();

    // Optional customer note
    const noteField = page.getByPlaceholder('Alerjen veya özel hazırlık notunuzu yazabilirsiniz...');
    if (await noteField.isVisible()) {
      await noteField.fill('Az pişmiş lütfen');
    }

    // 8. Add Item to Cart
    await expect(customizationCta).toBeEnabled();
    await customizationCta.click();

    // Verify badge updates on bottom nav
    const cartBadge = page.getByTestId('cart-badge-count');
    await expect(cartBadge).toHaveText('1');

    // 9. Change Quantity in Cart
    const cartNavBtn = page.getByTestId('bottom-nav-cart-btn');
    await cartNavBtn.click();

    const subtotalDisplay = page.getByTestId('cart-subtotal-display');
    await expect(subtotalDisplay).toBeVisible({ timeout: 5000 });

    const incrementBtn = page.getByTestId(/cart-increment-/).first();
    await expect(incrementBtn).toBeVisible();
    await incrementBtn.click();

    const qtyDisplay = page.getByTestId(/cart-qty-/).first();
    await expect(qtyDisplay).toHaveText('2');

    // Close Cart Sheet
    await page.getByLabel('Kapat').first().click();

    // 10. Page reload & Verify Session + Cart Recovery
    await page.reload();
    await expect(page.getByTestId('customer-active-session')).toBeVisible({ timeout: 10000 });
    await expect(page.getByTestId('cart-badge-count')).toHaveText('2');

    // 11. Create Waiter Call Service Request
    const waiterNavBtn = page.getByTestId('bottom-nav-waiter-btn');
    await waiterNavBtn.click();

    const waiterActionBtn = page.getByTestId('action-btn-waiter');
    await expect(waiterActionBtn).toBeVisible();
    await waiterActionBtn.click();

    const confirmBox = page.getByTestId('confirm-box-waiter');
    await expect(confirmBox).toBeVisible();

    const waiterNoteInput = page.getByTestId('note-input-waiter');
    if (await waiterNoteInput.isVisible()) {
      await waiterNoteInput.fill('Masa için tuz rica ediyoruz');
    }

    const submitConfirmBtn = page.getByTestId('submit-confirm-btn-waiter');
    await submitConfirmBtn.click();

    // Verify Open Badge
    const openBadge = page.getByTestId('badge-open-waiter');
    await expect(openBadge).toBeVisible({ timeout: 10000 });

    // 12. Verify Duplicate Click Protection (Action button hidden or disabled while open)
    await expect(page.getByTestId('action-btn-waiter')).not.toBeVisible();

    // 13. Cancel Service Request
    const cancelRequestBtn = page.getByTestId('cancel-request-btn-waiter');
    await expect(cancelRequestBtn).toBeVisible();
    await cancelRequestBtn.click();

    await expect(openBadge).not.toBeVisible({ timeout: 10000 });

    // 14. Request Bill
    const billActionBtn = page.getByTestId('action-btn-bill');
    await expect(billActionBtn).toBeVisible();
    await billActionBtn.click();

    const billConfirmBox = page.getByTestId('confirm-box-bill');
    await expect(billConfirmBox).toBeVisible();

    const submitBillBtn = page.getByTestId('submit-confirm-btn-bill');
    await submitBillBtn.click();

    // Verify Bill Requested state
    const billBadge = page.getByTestId('badge-bill-requested');
    await expect(billBadge).toBeVisible({ timeout: 10000 });

    // Close Service Sheet
    await page.getByLabel('Kapat').first().click();

    // 15. Verify Security: Raw tokens absent from URL and JS Storage
    const currentUrl = page.url();
    expect(currentUrl).not.toContain(scenario.qrToken);
    expect(currentUrl).not.toContain('/q/');

    const localStorageKeys = await page.evaluate(() => Object.keys(localStorage));
    const sessionStorageKeys = await page.evaluate(() => Object.keys(sessionStorage));

    expect(localStorageKeys).not.toContain('access_token');
    expect(localStorageKeys).not.toContain('jwt');
    expect(sessionStorageKeys).not.toContain('access_token');
    expect(sessionStorageKeys).not.toContain('jwt');
    expect(sessionStorageKeys).toContain('restaurant_order_cart_draft_v1');
  });
});
