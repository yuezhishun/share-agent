import { defineConfig } from 'vite';
import { resolve } from 'path';

export default defineConfig({
  base: './',
  root: resolve('src/renderer'),
  build: {
    outDir: resolve('out/renderer'),
    emptyOutDir: true,
    target: 'es2022',
    sourcemap: false,
    minify: true,
    reportCompressedSize: false,
    chunkSizeWarningLimit: 1200,
    rollupOptions: {
      input: { index: resolve('src/renderer/index.html') },
      output: {
        manualChunks(id: string) {
          if (!id.includes('node_modules')) return undefined;
          if (id.includes('/react-dom/') || id.includes('/react/')) return 'vendor-react';
          if (id.includes('/react-router-dom/') || id.includes('/react-router/')) return 'vendor-router';
          if (id.includes('/@arco-design/')) return 'vendor-arco';
          if (id.includes('/@microsoft/signalr/')) return 'vendor-signalr';
          return undefined;
        },
      },
    },
  },
  define: {
    global: 'globalThis',
  },
});
