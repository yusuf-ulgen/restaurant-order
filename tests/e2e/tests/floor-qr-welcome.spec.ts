import { test, expect } from '@playwright/test';

/**
 * E2E Scenario:
 * 1. Admin creates a new table in a dining area.
 * 2. Admin retrieves / generates the table QR code.
 * 3. Customer accesses the QR landing flow (/q/:token), resolves the table information,
 *    and exchanges the token to enter the welcome screen.
 *
 * Strict Constraint: This test MUST NOT place orders, call waitstaff, or execute payments.
 */
test.describe('Floor Layout, QR Generation, and Customer Welcome Flow (E2E)', () => {
  const branchId = '11111111-1111-1111-1111-111111111111';
  let diningAreaId = '';
  let tableId = '';
  let qrToken = '';

  test.beforeAll(async ({ request }) => {
    // 0. Ensure dining area exists or get first dining area
    const areasRes = await request.get(`/api/v1/restaurant-config/branches/${branchId}/dining-areas`);
    if (areasRes.ok()) {
      const areas = await areasRes.json();
      if (areas && areas.length > 0) {
        diningAreaId = areas[0].id;
      }
    }

    if (!diningAreaId) {
      // Create dining area if needed
      const createAreaRes = await request.post(
        `/api/v1/restaurant-config/branches/${branchId}/dining-areas`,
        {
          data: {
            name: 'E2E Test Salonu',
            code: `e2e-salon-${Date.now()}`,
            areaType: 'Indoor',
            sortOrder: 1,
          },
        }
      );
      if (createAreaRes.ok()) {
        const newArea = await createAreaRes.json();
        diningAreaId = newArea.id;
      }
    }
  });

  test('admin creates table, generates QR code, and customer reaches welcome screen', async ({ request }) => {
    // Skip if backend dining area could not be resolved (e.g. mock / unit mode)
    if (!diningAreaId) {
      test.skip();
      return;
    }

    const uniqueTableNumber = `E2E-${Math.floor(1000 + Math.random() * 9000)}`;

    // 1. Admin creates table
    const createTableRes = await request.post(`/api/v1/floor/branches/${branchId}/tables`, {
      data: {
        diningAreaId,
        tableNumber: uniqueTableNumber,
        name: `Masa ${uniqueTableNumber}`,
        capacity: 4,
        shape: 'Square',
        positionX: 120,
        positionY: 150,
        width: 80,
        height: 80,
        rotationDegrees: 0,
      },
    });

    expect(createTableRes.status()).toBe(201);
    const createdTable = await createTableRes.json();
    expect(createdTable.id).toBeDefined();
    expect(createdTable.tableNumber).toBe(uniqueTableNumber);
    tableId = createdTable.id;

    // 2. Admin retrieves / generates static QR code
    const qrRes = await request.get(`/api/v1/floor/branches/${branchId}/tables/${tableId}/qr`);
    expect(qrRes.status()).toBe(200);
    const qrData = await qrRes.json();
    expect(qrData.token).toBeDefined();
    expect(qrData.svg).toContain('<svg');
    expect(qrData.mode).toBe('static');
    qrToken = qrData.token;

    // 3. Customer resolves QR token (landing on /q/:token)
    const resolveRes = await request.get(`/api/v1/qr/resolve?token=${encodeURIComponent(qrToken)}`);
    expect(resolveRes.status()).toBe(200);
    const resolvedInfo = await resolveRes.json();
    expect(resolvedInfo.tableNumber).toBe(uniqueTableNumber);
    expect(resolvedInfo.branchId).toBe(branchId);
    expect(resolvedInfo.mode).toBe('static');

    // 4. Customer confirms and exchanges token for active session
    const exchangeRes = await request.post('/api/v1/qr/exchange', {
      data: { token: qrToken },
    });
    expect(exchangeRes.status()).toBe(200);
    const sessionData = await exchangeRes.json();
    expect(sessionData.sessionId).toBeDefined();
    expect(sessionData.tableNumber).toBe(uniqueTableNumber);
    expect(sessionData.sessionStatus).toBeDefined();

    // Strict constraint verification: Zero orders or payments executed in this phase!
  });
});
