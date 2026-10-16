import crypto from 'node:crypto';
import type { APIRequestContext } from '@playwright/test';

export interface SeededCustomerScenario {
  tenantId: string;
  branchId: string;
  diningAreaId: string;
  tableId: string;
  tableNumber: string;
  qrToken: string;
  menuId: string;
  categoryIdBurger: string;
  categoryIdDrink: string;
  itemBurgerId: string;
  itemDrinkId: string;
  itemOutOfStockId: string;
  variantSingleId: string;
  variantDoubleId: string;
  reqModifierGroupId: string;
  optModifierGroupId: string;
  adminToken: string;
}

export function mintDevAdminToken(tenantId: string): string {
  const secret = process.env.E2E_JWT_SECRET || process.env.JWT_SECRET;
  if (!secret) {
    throw new Error('E2E_JWT_SECRET or JWT_SECRET environment variable is required');
  }
  const header = Buffer.from(JSON.stringify({ alg: 'HS256', typ: 'JWT', kid: 'k1' })).toString('base64url');
  const now = Math.floor(Date.now() / 1000);
  const exp = now + 3600;
  const payload = Buffer.from(
    JSON.stringify({
      sub: '00000000-0000-0000-0000-000000000001',
      sid: '00000000-0000-0000-0000-000000000002',
      jti: crypto.randomUUID().replace(/-/g, ''),
      principal_type: 'staff',
      role: 'RestaurantAdmin',
      auth_method: 'password',
      security_version: '1',
      iss: 'restaurant-order',
      aud: 'restaurant-order-clients',
      iat: now,
      nbf: now,
      exp: exp,
      tenant_id: tenantId,
    })
  ).toString('base64url');

  const signature = crypto.createHmac('sha256', secret).update(`${header}.${payload}`).digest('base64url');
  return `${header}.${payload}.${signature}`;
}

export async function seedCustomerExperienceScenario(
  request: APIRequestContext,
  tenantId = '11111111-1111-1111-1111-111111111111',
  branchId = 'd1111111-1111-1111-1111-111111111111'
): Promise<SeededCustomerScenario> {
  const adminToken = mintDevAdminToken(tenantId);
  const authHeaders = {
    Authorization: `Bearer ${adminToken}`,
    'Content-Type': 'application/json',
  };

  // 0. Ensure dev seed is run
  const seedRes = await request.post('/api/v1/dev/seed');
  if (!seedRes.ok()) {
    throw new Error(`CRITICAL: /api/v1/dev/seed failed: ${await seedRes.text()}`);
  }

  // 1. Dining Area
  const areasRes = await request.get(`/api/v1/restaurant-config/branches/${branchId}/dining-areas`, {
    headers: authHeaders,
  });
  if (!areasRes.ok()) {
    throw new Error(`Failed to fetch dining areas: ${await areasRes.text()}`);
  }
  const areas = await areasRes.json();
  let diningAreaId = '';
  if (Array.isArray(areas) && areas.length > 0) {
    diningAreaId = areas[0].id;
  } else {
    const createAreaRes = await request.post(`/api/v1/restaurant-config/branches/${branchId}/dining-areas`, {
      headers: authHeaders,
      data: { name: 'E2E Salon', code: `e2e-salon-${Date.now()}`, areaType: 'Indoor', sortOrder: 1 },
    });
    if (!createAreaRes.ok()) throw new Error(`Create dining area failed: ${await createAreaRes.text()}`);
    const newArea = await createAreaRes.json();
    diningAreaId = newArea.id;
  }

  // 2. Table
  const uniqueTableNumber = `E2E-${Math.floor(1000 + Math.random() * 9000)}`;
  const tableRes = await request.post(`/api/v1/floor/branches/${branchId}/tables`, {
    headers: authHeaders,
    data: {
      diningAreaId,
      tableNumber: uniqueTableNumber,
      name: `Masa ${uniqueTableNumber}`,
      capacity: 4,
      shape: 'Square',
      positionX: 100,
      positionY: 100,
      width: 80,
      height: 80,
      rotationDegrees: 0,
    },
  });
  if (!tableRes.ok()) throw new Error(`Create table failed: ${await tableRes.text()}`);
  const table = await tableRes.json();

  // 3. QR Token
  const qrRes = await request.get(`/api/v1/floor/branches/${branchId}/tables/${table.id}/qr`, {
    headers: authHeaders,
  });
  if (!qrRes.ok()) throw new Error(`Get QR token failed: ${await qrRes.text()}`);
  const qrData = await qrRes.json();
  const qrToken = qrData.token;

  // 4. Create Menu & Activate
  const menuRes = await request.post(`/api/v1/catalog/branches/${branchId}/menus`, {
    headers: authHeaders,
    data: {
      name: `Müşteri Menüsü ${Date.now()}`,
      slug: `musteri-menusu-${Date.now()}`,
      description: 'Faz 7 E2E Test Menüsü',
    },
  });
  if (!menuRes.ok()) throw new Error(`Create menu failed: ${await menuRes.text()}`);
  const menu = await menuRes.json();

  const actMenuRes = await request.post(`/api/v1/catalog/branches/${branchId}/menus/${menu.id}/activate`, {
    headers: { ...authHeaders, 'If-Match': `"${menu.concurrencyToken}"` },
  });
  if (!actMenuRes.ok()) throw new Error(`Activate menu failed: ${await actMenuRes.text()}`);

  // 5. Categories
  const catBurgerRes = await request.post(`/api/v1/catalog/branches/${branchId}/menus/${menu.id}/categories`, {
    headers: authHeaders,
    data: {
      name: 'Burgerler',
      slug: `burgerler-${Date.now()}`,
      description: 'Gurme Burgerler',
      sortOrder: 1,
    },
  });
  if (!catBurgerRes.ok()) throw new Error(`Create category burger failed: ${await catBurgerRes.text()}`);
  const catBurger = await catBurgerRes.json();

  const catDrinkRes = await request.post(`/api/v1/catalog/branches/${branchId}/menus/${menu.id}/categories`, {
    headers: authHeaders,
    data: {
      name: 'İçecekler',
      slug: `icecekler-${Date.now()}`,
      description: 'Soğuk İçecekler',
      sortOrder: 2,
    },
  });
  if (!catDrinkRes.ok()) throw new Error(`Create category drink failed: ${await catDrinkRes.text()}`);
  const catDrink = await catDrinkRes.json();

  // 6. Item 1: Gourmet Burger (with image, tags, variants, modifiers)
  const burgerRes = await request.post(`/api/v1/catalog/branches/${branchId}/menus/${menu.id}/items`, {
    headers: authHeaders,
    data: {
      categoryId: catBurger.id,
      name: 'Gourmet Burger',
      slug: `gourmet-burger-${Date.now()}`,
      basePriceMinorUnits: 18000,
      shortDescription: 'Özel gurme burger',
      fullDescription: 'Taze brioche ekmeği arasında 150g dana köftesi ve karamelize soğan.',
      imageUrl: 'https://images.unsplash.com/photo-1568901346375-23c9450c58cd',
      sortOrder: 1,
    },
  });
  if (!burgerRes.ok()) throw new Error(`Create burger item failed: ${await burgerRes.text()}`);
  const burger = await burgerRes.json();

  // Item 1 Metadata (Spicy, Dietary, Allergen)
  const metaRes = await request.put(`/api/v1/catalog/branches/${branchId}/menus/${menu.id}/items/${burger.id}/metadata`, {
    headers: authHeaders,
    data: { spicyLevel: 1, dietaryTags: ['Glutensiz'], allergenTags: ['Susam'] },
  });
  if (!metaRes.ok()) throw new Error(`Update burger metadata failed: ${await metaRes.text()}`);

  // Item 1 Variants
  const vSingleRes = await request.post(`/api/v1/catalog/branches/${branchId}/menus/${menu.id}/items/${burger.id}/variants`, {
    headers: authHeaders,
    data: { name: 'Tek Köfte', shortCode: 'SNG', absolutePriceMinorUnits: 18000, sortOrder: 1, isDefault: true },
  });
  if (!vSingleRes.ok()) throw new Error(`Create variant single failed: ${await vSingleRes.text()}`);
  const vSingle = await vSingleRes.json();

  const vDoubleRes = await request.post(`/api/v1/catalog/branches/${branchId}/menus/${menu.id}/items/${burger.id}/variants`, {
    headers: authHeaders,
    data: { name: 'Çift Köfte', shortCode: 'DBL', absolutePriceMinorUnits: 25000, sortOrder: 2, isDefault: false },
  });
  if (!vDoubleRes.ok()) throw new Error(`Create variant double failed: ${await vDoubleRes.text()}`);
  const vDouble = await vDoubleRes.json();

  // Item 1 Required Modifier Group
  const reqModRes = await request.post(`/api/v1/catalog/branches/${branchId}/modifier-groups`, {
    headers: authHeaders,
    data: { name: 'Peynir Seçimi', minSelections: 1, maxSelections: 1, sortOrder: 1 },
  });
  if (!reqModRes.ok()) throw new Error(`Create req modifier group failed: ${await reqModRes.text()}`);
  const reqMod = await reqModRes.json();

  await request.post(`/api/v1/catalog/branches/${branchId}/modifier-groups/${reqMod.id}/options`, {
    headers: authHeaders,
    data: { name: 'Cheddar', priceDeltaMinorUnits: 2500, sortOrder: 1, isDefault: true },
  });
  await request.post(`/api/v1/catalog/branches/${branchId}/modifier-groups/${reqMod.id}/options`, {
    headers: authHeaders,
    data: { name: 'Kaşar', priceDeltaMinorUnits: 2000, sortOrder: 2, isDefault: false },
  });
  await request.post(`/api/v1/catalog/branches/${branchId}/menus/${menu.id}/items/${burger.id}/modifier-groups`, {
    headers: authHeaders,
    data: { modifierGroupId: reqMod.id, sortOrder: 1 },
  });

  // Item 1 Optional Modifier Group
  const optModRes = await request.post(`/api/v1/catalog/branches/${branchId}/modifier-groups`, {
    headers: authHeaders,
    data: { name: 'Ekstralar', minSelections: 0, maxSelections: 2, sortOrder: 2 },
  });
  if (!optModRes.ok()) throw new Error(`Create opt modifier group failed: ${await optModRes.text()}`);
  const optMod = await optModRes.json();

  await request.post(`/api/v1/catalog/branches/${branchId}/modifier-groups/${optMod.id}/options`, {
    headers: authHeaders,
    data: { name: 'Karamelize Soğan', priceDeltaMinorUnits: 1500, sortOrder: 1, isDefault: false },
  });
  await request.post(`/api/v1/catalog/branches/${branchId}/menus/${menu.id}/items/${burger.id}/modifier-groups`, {
    headers: authHeaders,
    data: { modifierGroupId: optMod.id, sortOrder: 2 },
  });

  // 7. Item 2: Ayran (Drink, no image)
  const drinkRes = await request.post(`/api/v1/catalog/branches/${branchId}/menus/${menu.id}/items`, {
    headers: authHeaders,
    data: {
      categoryId: catDrink.id,
      name: 'Ayran',
      slug: `ayran-${Date.now()}`,
      basePriceMinorUnits: 3000,
      shortDescription: 'Geleneksel yayık ayranı.',
      sortOrder: 1,
    },
  });
  if (!drinkRes.ok()) throw new Error(`Create drink item failed: ${await drinkRes.text()}`);
  const drink = await drinkRes.json();

  // 8. Item 3: Out of stock item (quick-86)
  const oosRes = await request.post(`/api/v1/catalog/branches/${branchId}/menus/${menu.id}/items`, {
    headers: authHeaders,
    data: {
      categoryId: catBurger.id,
      name: 'Özel Şef Burgeri',
      slug: `special-burger-${Date.now()}`,
      basePriceMinorUnits: 32000,
      shortDescription: 'Tükenen lezzet.',
      sortOrder: 2,
    },
  });
  if (!oosRes.ok()) throw new Error(`Create OOS item failed: ${await oosRes.text()}`);
  const oosItem = await oosRes.json();

  const quick86Res = await request.post(
    `/api/v1/catalog/branches/${branchId}/menus/${menu.id}/items/${oosItem.id}/quick-86`,
    { headers: authHeaders }
  );
  if (!quick86Res.ok()) throw new Error(`Quick 86 failed: ${await quick86Res.text()}`);

  return {
    tenantId,
    branchId,
    diningAreaId,
    tableId: table.id,
    tableNumber: uniqueTableNumber,
    qrToken,
    menuId: menu.id,
    categoryIdBurger: catBurger.id,
    categoryIdDrink: catDrink.id,
    itemBurgerId: burger.id,
    itemDrinkId: drink.id,
    itemOutOfStockId: oosItem.id,
    variantSingleId: vSingle.id,
    variantDoubleId: vDouble.id,
    reqModifierGroupId: reqMod.id,
    optModifierGroupId: optMod.id,
    adminToken,
  };
}

export async function cleanupCustomerScenario(
  request: APIRequestContext,
  scenario: SeededCustomerScenario
): Promise<void> {
  const authHeaders = {
    Authorization: `Bearer ${scenario.adminToken}`,
    'Content-Type': 'application/json',
  };

  try {
    const sessionsRes = await request.get(
      `/api/v1/floor/branches/${scenario.branchId}/tables/${scenario.tableId}/sessions`,
      { headers: authHeaders }
    );
    if (sessionsRes.ok()) {
      const sessions = await sessionsRes.json();
      for (const s of sessions) {
        if (s.status !== 'Closed') {
          let token = s.concurrencyToken;
          if (s.status === 'Open') {
            const actRes = await request.post(
              `/api/v1/floor/branches/${scenario.branchId}/sessions/${s.id}/activate`,
              { headers: authHeaders, data: { concurrencyToken: token } }
            );
            if (actRes.ok()) token = (await actRes.json()).concurrencyToken;
          }
          if (s.status !== 'BillRequested') {
            const billRes = await request.post(
              `/api/v1/floor/branches/${scenario.branchId}/sessions/${s.id}/request-bill`,
              { headers: authHeaders, data: { concurrencyToken: token } }
            );
            if (billRes.ok()) token = (await billRes.json()).concurrencyToken;
          }
          await request.post(
            `/api/v1/floor/branches/${scenario.branchId}/sessions/${s.id}/close`,
            { headers: authHeaders, data: { concurrencyToken: token, reason: 'E2E Cleanup' } }
          );
        }
      }
    }

    const tableGet = await request.get(
      `/api/v1/floor/branches/${scenario.branchId}/tables/${scenario.tableId}`,
      { headers: authHeaders }
    );
    if (tableGet.ok()) {
      const tableData = await tableGet.json();
      await request.post(
        `/api/v1/floor/branches/${scenario.branchId}/tables/${scenario.tableId}/deactivate`,
        { headers: authHeaders, data: { concurrencyToken: tableData.concurrencyToken } }
      );
    }
  } catch {
    // Non-fatal cleanup
  }
}
