import crypto from 'node:crypto';
import { defineConfig, devices } from '@playwright/test';

const API_BASE_URL = process.env.VITE_API_URL || 'http://127.0.0.1:5000';
const SHARED_JWT_SECRET = process.env.E2E_JWT_SECRET || process.env.JWT_SECRET || crypto.randomBytes(32).toString('hex');
const SHARED_QR_KEY = process.env.QR_KEY_K1 || crypto.randomBytes(32).toString('hex');
const DATABASE_URL = process.env.DATABASE_URL || 'Host=localhost;Port=5432;Database=restaurant_order_dev;Username=postgres;Password=postgres';
const REDIS_URL = process.env.REDIS_URL || 'localhost:6379';

process.env.E2E_JWT_SECRET = SHARED_JWT_SECRET;
process.env.JWT_SECRET = SHARED_JWT_SECRET;

export default defineConfig({
  testDir: './tests',
  fullyParallel: true,
  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 2 : 0,
  workers: process.env.CI ? 1 : undefined,
  reporter: 'list',
  use: {
    baseURL: API_BASE_URL,
    trace: 'on-first-retry',
  },
  webServer: [
    {
      command: 'dotnet run --project ../../apps/api --no-launch-profile',
      stdout: 'pipe',
      url: `${API_BASE_URL}/health/live`,
      reuseExistingServer: !process.env.CI,
      timeout: 60 * 1000,
      env: {
        ASPNETCORE_URLS: API_BASE_URL,
        ASPNETCORE_ENVIRONMENT: 'Development',
        DEPLOYMENT_COLOR: 'blue',
        JWT_SECRET: SHARED_JWT_SECRET,
        DATABASE_URL: DATABASE_URL,
        REDIS_URL: REDIS_URL,
        QrSecurity__CurrentKeyId: 'k1',
        QrSecurity__Keys__k1: SHARED_QR_KEY,
      },
    },
    {
      command: 'dotnet run --project ../../apps/worker --no-launch-profile',
      stdout: 'pipe',
      url: 'http://127.0.0.1:5005/health/live',
      reuseExistingServer: !process.env.CI,
      timeout: 60 * 1000,
      env: {
        ASPNETCORE_URLS: 'http://127.0.0.1:5005',
        ASPNETCORE_ENVIRONMENT: 'Development',
        DEPLOYMENT_COLOR: 'blue',
        ACTIVE_DEPLOYMENT_SLOT: 'blue',
        WORKER_DATABASE_URL: process.env.WORKER_DATABASE_URL || DATABASE_URL,
        REDIS_URL: REDIS_URL,
      },
    },
    {
      command: 'pnpm --filter customer-web dev',
      url: 'http://127.0.0.1:3001',
      reuseExistingServer: !process.env.CI,
      timeout: 60 * 1000,
    },
    {
      command: 'pnpm --filter operations-web dev',
      url: 'http://127.0.0.1:3002',
      reuseExistingServer: !process.env.CI,
      timeout: 60 * 1000,
    },
  ],
  projects: [
    {
      name: 'chromium',
      use: { ...devices['Desktop Chrome'], channel: process.env.PLAYWRIGHT_CHANNEL || undefined },
    },
  ],
});
