import crypto from 'node:crypto';
import { defineConfig, devices } from '@playwright/test';

const API_BASE_URL = process.env.VITE_API_URL || 'http://127.0.0.1:5000';
const SHARED_JWT_SECRET = process.env.E2E_JWT_SECRET || process.env.JWT_SECRET || crypto.randomBytes(32).toString('hex');
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
      url: `${API_BASE_URL}/health/live`,
      reuseExistingServer: !process.env.CI,
      timeout: 60 * 1000,
      env: {
        ASPNETCORE_URLS: API_BASE_URL,
        ASPNETCORE_ENVIRONMENT: 'Development',
        DEPLOYMENT_COLOR: 'blue',
        JWT_SECRET: SHARED_JWT_SECRET,
      },
    },
    {
      command: 'pnpm --filter customer-web dev',
      url: 'http://127.0.0.1:3001',
      reuseExistingServer: !process.env.CI,
      timeout: 60 * 1000,
    },
  ],
  projects: [
    {
      name: 'chromium',
      use: { ...devices['Desktop Chrome'] },
    },
  ],
});
