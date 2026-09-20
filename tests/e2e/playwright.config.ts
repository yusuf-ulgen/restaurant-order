import { defineConfig, devices } from '@playwright/test';

const API_BASE_URL = process.env.VITE_API_URL || 'http://127.0.0.1:5000';

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
  webServer: {
    command: 'dotnet run --project ../../apps/api --no-launch-profile',
    url: `${API_BASE_URL}/health/live`,
    reuseExistingServer: !process.env.CI,
    timeout: 60 * 1000,
    env: {
      ASPNETCORE_URLS: API_BASE_URL,
      ASPNETCORE_ENVIRONMENT: 'Development',
      DEPLOYMENT_COLOR: 'blue',
    },
  },
  projects: [
    {
      name: 'chromium',
      use: { ...devices['Desktop Chrome'] },
    },
  ],
});
