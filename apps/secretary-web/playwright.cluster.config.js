import { defineConfig, devices } from '@playwright/test';

export default defineConfig({
  testDir: './e2e',
  testMatch: '**/cluster.spec.js',
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
        PORT: '8080',
        GATEWAY_ROLE: 'master',
        NODE_ID: 'master-1',
        NODE_NAME: 'Master 1',
        CLUSTER_TOKEN: 'cluster-e2e-token',
        TERMINAL_SETTINGS_STORE_FILE: '/tmp/pty-agent-terminal-settings-master.json'
      }
    },
    {
      command: 'dotnet run --project ../terminal-gateway-dotnet/TerminalGateway.Api/TerminalGateway.Api.csproj',
      url: 'http://127.0.0.1:8081/api/health',
      timeout: 180000,
      reuseExistingServer: false,
      env: {
        HOST: '127.0.0.1',
        PORT: '8081',
        GATEWAY_ROLE: 'slave',
        MASTER_URL: 'http://127.0.0.1:8080',
        NODE_ID: 'slave-a',
        NODE_NAME: 'Slave A',
        CLUSTER_TOKEN: 'cluster-e2e-token',
        TERMINAL_SETTINGS_STORE_FILE: '/tmp/pty-agent-terminal-settings-slave-a.json'
      }
    },
    {
      command: 'dotnet run --project ../terminal-gateway-dotnet/TerminalGateway.Api/TerminalGateway.Api.csproj',
      url: 'http://127.0.0.1:8082/api/health',
      timeout: 180000,
      reuseExistingServer: false,
      env: {
        HOST: '127.0.0.1',
        PORT: '8082',
        GATEWAY_ROLE: 'slave',
        MASTER_URL: 'http://127.0.0.1:8080',
        NODE_ID: 'slave-b',
        NODE_NAME: 'Slave B',
        CLUSTER_TOKEN: 'cluster-e2e-token',
        TERMINAL_SETTINGS_STORE_FILE: '/tmp/pty-agent-terminal-settings-slave-b.json'
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
