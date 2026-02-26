import { defineConfig, devices } from '@playwright/test';

export default defineConfig({
  testDir: './e2e',
  testMatch: '**/integration.spec.js',
  fullyParallel: false,
  reporter: 'html',
  use: {
    baseURL: 'http://127.0.0.1:4173',
    trace: 'on-first-retry'
  },
  webServer: [
    {
      command: 'dotnet run --project ../terminal-gateway-dotnet/TerminalGateway.Api/TerminalGateway.Api.csproj',
      url: 'http://127.0.0.1:8080/api/health',
      timeout: 180000,
      reuseExistingServer: false,
      env: {
        HOST: '127.0.0.1',
        PORT: '8080'
      }
    },
    {
      command: 'npm run dev -- --host 127.0.0.1 --port 4173',
      url: 'http://127.0.0.1:4173',
      timeout: 120000,
      reuseExistingServer: false
    }
  ],
  projects: [
    {
      name: 'chromium',
      use: { ...devices['Desktop Chrome'] }
    }
  ]
});
