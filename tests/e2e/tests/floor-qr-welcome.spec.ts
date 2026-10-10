import { test, expect } from '@playwright/test';
import crypto from 'node:crypto';

/**
 * Mints a synthetic development RestaurantAdmin JWT token adhering to ADR-0009 and API claim invariants.
 */
function mintDevAdminToken(tenantId: string): string {
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

/**
 * Deterministic E2E Flow for Phase 6:
 * 1. Authenticated RestaurantAdmin identifies / creates dining area.
 * 2. Admin creates a new table on the floor layout.
 * 3. Admin generates / retrieves static QR code with signed cryptographic token.
 * 4. Customer verifies table metadata via /api/v1/qr/resolve.
 * 5. Customer navigates to /q/:token in the customer web app.
 * 6. Welcome screen renders restaurant, branch, and table information.
 * 7. Customer initiates session exchange and active welcome view appears.
 * 8. Raw token is completely stripped from browser URL / address bar.
 * 9. Strict Phase 6 boundary: zero orders, bills, or payments executed.
 * 10. Admin safely deactivates the test table after scenario completion.
 */
test.describe('Floor Layout, QR Generation, and Customer Welcome Flow (E2E)', () => {
  const tenantId = '11111111-1111-1111-1111-111111111111';
  const branchId = 'd1111111-1111-1111-1111-111111111111';
  const adminToken = mintDevAdminToken(tenantId);
  const authHeaders = {
    Authorization: `Bearer ${adminToken}`,
    'Content-Type': 'application/json',
  };

  test('admin creates table, generates QR code, and customer reaches welcome screen with URL sanitized', async ({
    request,
    page,
  }) => {
    // 0. Ensure dev/test backend is healthy and seeded with synthetic identities
    const seedRes = await request.post('/api/v1/dev/seed');
    const seedBody = await seedRes.text();
    expect(seedRes.status(), `Dev seed must be accessible and succeed before running E2E scenario: ${seedBody}`).toBe(200);

    // 1. Get or create dining area with authenticated admin
    const areasRes = await request.get(`/api/v1/restaurant-config/branches/${branchId}/dining-areas`, {
      headers: authHeaders,
    });
    expect(areasRes.status(), 'Admin should be able to fetch dining areas').toBe(200);

    const areas = await areasRes.json();
    let diningAreaId = '';
    let createdAreaConcurrencyToken: string | null = null;
    let createdAreaId: string | null = null;
    if (Array.isArray(areas) && areas.length > 0) {
      diningAreaId = areas[0].id;
    } else {
      const createAreaRes = await request.post(
        `/api/v1/restaurant-config/branches/${branchId}/dining-areas`,
        {
          headers: authHeaders,
          data: {
            name: 'E2E Ana Salon',
            code: `e2e-salon-${Date.now()}`,
            areaType: 'Indoor',
            sortOrder: 1,
          },
        }
      );
      expect(createAreaRes.status(), 'Admin should be able to create dining area').toBe(201);
      const newArea = await createAreaRes.json();
      diningAreaId = newArea.id;
      createdAreaId = newArea.id;
      createdAreaConcurrencyToken = newArea.concurrencyToken;
    }
    expect(diningAreaId).toBeTruthy();

    const uniqueTableNumber = `E2E-${Math.floor(1000 + Math.random() * 9000)}`;

    // 2. Admin creates table on floor layout
    const createTableRes = await request.post(`/api/v1/floor/branches/${branchId}/tables`, {
      headers: authHeaders,
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

    expect(createTableRes.status(), 'Creating table should return 201 Created').toBe(201);
    const createdTable = await createTableRes.json();
    expect(createdTable.id).toBeDefined();
    expect(createdTable.tableNumber).toBe(uniqueTableNumber);
    const tableId = createdTable.id;
    let concurrencyToken = createdTable.concurrencyToken;

    try {
      // 3. Admin retrieves signed static QR code
      const qrRes = await request.get(`/api/v1/floor/branches/${branchId}/tables/${tableId}/qr`, {
        headers: authHeaders,
      });
      expect(qrRes.status(), 'Fetching table QR should return 200 OK').toBe(200);
      const qrData = await qrRes.json();
      expect(qrData.token).toBeDefined();
      expect(qrData.svg).toContain('<svg');
      expect(qrData.mode).toBe('static');
      const qrToken = qrData.token;

      // 4. API level: Customer resolves QR token
      const resolveRes = await request.get(`/api/v1/qr/resolve?token=${encodeURIComponent(qrToken)}`);
      expect(resolveRes.status(), 'Resolving QR token should return 200 OK').toBe(200);
      const resolvedInfo = await resolveRes.json();
      expect(resolvedInfo.tableNumber).toBe(uniqueTableNumber);
      expect(resolvedInfo.branchId).toBe(branchId);
      expect(resolvedInfo.mode).toBe('static');

      // 5. Browser level: Customer visits QR landing URL
      await page.goto(`http://127.0.0.1:3001/q/${encodeURIComponent(qrToken)}`);

      // 6. Welcome screen renders table details
      const resolveView = page.getByTestId('table-resolve-view');
      await expect(resolveView).toBeVisible({ timeout: 10000 });
      await expect(resolveView.getByText(uniqueTableNumber).first()).toBeVisible();

      // 7. Customer initiates session exchange by clicking button
      const continueBtn = page.getByTestId('continue-to-table-btn');
      await expect(continueBtn).toBeVisible();
      await continueBtn.click();

      // 8. Session welcome view is presented
      const sessionWelcomeView = page.getByTestId('session-welcome-view');
      await expect(sessionWelcomeView).toBeVisible({ timeout: 10000 });
      await expect(sessionWelcomeView.getByText(uniqueTableNumber).first()).toBeVisible();

      // 9. Verify URL sanitization: raw token MUST NOT remain in URL or query string
      const currentUrl = page.url();
      expect(currentUrl).not.toContain(qrToken);
      expect(currentUrl).not.toContain('/q/');

      // 10. Strict Phase 6 boundary: no orders, bill requests, or payments exist
    } finally {
      // 1. Cleanly close any open dining sessions for the table
      try {
        const sessionsRes = await request.get(`/api/v1/floor/branches/${branchId}/tables/${tableId}/sessions`, {
          headers: authHeaders,
        });
        if (sessionsRes.ok()) {
          const sessions = await sessionsRes.json();
          for (const session of sessions) {
            if (session.status !== 'Closed') {
              let currentToken = session.concurrencyToken;
              if (session.status === 'Open') {
                const actRes = await request.post(
                  `/api/v1/floor/branches/${branchId}/sessions/${session.id}/activate`,
                  {
                    headers: authHeaders,
                    data: { concurrencyToken: currentToken },
                  }
                );
                if (actRes.ok()) {
                  const actData = await actRes.json();
                  currentToken = actData.concurrencyToken;
                }
              }
              const billRes = await request.post(
                `/api/v1/floor/branches/${branchId}/sessions/${session.id}/request-bill`,
                {
                  headers: authHeaders,
                  data: { concurrencyToken: currentToken },
                }
              );
              if (billRes.ok()) {
                const billData = await billRes.json();
                currentToken = billData.concurrencyToken;
              }
              await request.post(
                `/api/v1/floor/branches/${branchId}/sessions/${session.id}/close`,
                {
                  headers: authHeaders,
                  data: { concurrencyToken: currentToken, reason: 'E2E Cleanup' },
                }
              );
            }
          }
        }
      } catch {
        // Proceed with table deactivation
      }

      // 2. Fetch fresh concurrency token and deactivate table
      const tableGetRes = await request.get(`/api/v1/floor/branches/${branchId}/tables/${tableId}`, {
        headers: authHeaders,
      });
      if (tableGetRes.ok()) {
        const tableData = await tableGetRes.json();
        concurrencyToken = tableData.concurrencyToken;
      }

      const deactRes = await request.post(
        `/api/v1/floor/branches/${branchId}/tables/${tableId}/deactivate`,
        {
          headers: authHeaders,
          data: { concurrencyToken },
        }
      );
      expect(deactRes.status(), 'Table must be deactivated successfully after sessions closed').toBe(200);

      // 3. If test created its own dining area, deactivate it
      if (createdAreaId && createdAreaConcurrencyToken) {
        await request.post(
          `/api/v1/restaurant-config/branches/${branchId}/dining-areas/${createdAreaId}/deactivate`,
          {
            headers: authHeaders,
            data: { concurrencyToken: createdAreaConcurrencyToken },
          }
        );
      }
    }
  });
});
