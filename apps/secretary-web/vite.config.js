import { defineConfig } from 'vite';
import vue from '@vitejs/plugin-vue';

function normalizeBase(input) {
  const raw = String(input || '/').trim();
  if (!raw || raw === '/') {
    return '/';
  }

  const withLeadingSlash = raw.startsWith('/') ? raw : `/${raw}`;
  return withLeadingSlash.endsWith('/') ? withLeadingSlash : `${withLeadingSlash}/`;
}

export default defineConfig({
  base: normalizeBase(process.env.VITE_APP_BASE_PATH),
  plugins: [vue()],
  build: {
    rollupOptions: {
      output: {
        manualChunks(id) {
          if (!id.includes('node_modules')) {
            return null;
          }
          if (id.includes('vditor')) {
            return 'markdown-editor';
          }
          if (id.includes('xterm')) {
            return 'terminal-vendor';
          }
          if (id.includes('@microsoft/signalr')) {
            return 'signalr';
          }
          if (id.includes('vue') || id.includes('pinia') || id.includes('vue-router')) {
            return 'vue-core';
          }
          return null;
        }
      }
    }
  },
  server: {
    host: '0.0.0.0',
    port: 3000,
    proxy: {
      '/api': {
        target: 'http://localhost:8080',
        changeOrigin: true
      },
      '/ws': {
        target: 'ws://localhost:8080',
        ws: true
      },
      '/hubs': {
        target: 'http://localhost:8080',
        changeOrigin: true,
        ws: true
      }
    }
  }
});
