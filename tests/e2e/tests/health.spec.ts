import { test, expect } from '@playwright/test';

test.describe('API Health Probes', () => {
  test('GET /health/live returns Healthy', async ({ request }) => {
    const response = await request.get('/health/live');
    expect(response.status()).toBe(200);

    const body = await response.json();
    expect(body.status).toBe('Healthy');
    expect(body.service).toBe('restaurant-order-api');
  });

  test('GET /health/ready returns Healthy', async ({ request }) => {
    const response = await request.get('/health/ready');
    expect(response.status()).toBe(200);

    const body = await response.json();
    expect(body.status).toBe('Healthy');
    expect(body.service).toBe('restaurant-order-api');
  });
});
