import { defineConfig } from 'vitest/config';

export default defineConfig({
  test: {
    globals: true,
    testTimeout: 10000,
    environment: 'jsdom',
    include: ['src/renderer/**/*.test.ts', 'src/renderer/**/*.test.tsx'],
    setupFiles: ['./tests/vitest.dom.setup.ts'],
    coverage: {
      provider: 'v8',
      reporter: ['text', 'text-summary', 'html', 'lcov'],
      reportsDirectory: './coverage',
      include: ['src/renderer/**/*.{ts,tsx}'],
      exclude: ['src/renderer/main.tsx'],
      thresholds: {
        statements: 0,
        branches: 0,
        functions: 0,
        lines: 0
      }
    }
  }
});
