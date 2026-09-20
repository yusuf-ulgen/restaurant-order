import { test, expect } from '@playwright/test';

test.describe('API Health Probes', () => {
  test('GET /health/live returns Healthy via HTTP', async ({ request }) => {
    const response = await request.get('/health/live');
    expect(response.status()).toBe(200);

    const body = await response.json();
    expect(body.status).toBe('Healthy');
    expect(body.service).toBe('restaurant-order-api');
    expect(body.version).toBe('0.1.0');
    expect(body.timestamp).toBeDefined();
    expect(body.color).toBeDefined();
  });

  test('GET /health/ready returns Healthy via HTTP', async ({ request }) => {
    const response = await request.get('/health/ready');
    expect(response.status()).toBe(200);

    const body = await response.json();
    expect(body.status).toBe('Healthy');
    expect(body.service).toBe('restaurant-order-api');
    expect(body.version).toBe('0.1.0');
    expect(body.checks).toBeDefined();
    expect(body.checks.database).toBe('Healthy');
    expect(body.checks.redis).toBe('Healthy');
  });
});
