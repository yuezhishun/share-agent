import { defineConfig } from 'vite';
import vue from '@vitejs/plugin-vue';

export default defineConfig({
  plugins: [vue()],
  server: {
    host: '0.0.0.0',
    port: 3000,
    proxy: {
      '/web-pty/api': {
        target: 'http://localhost:8080',
        changeOrigin: true,
        rewrite: (path) => path.replace(/^\/web-pty\/api/, '/api')
      },
      '/web-pty/ws': {
        target: 'ws://localhost:8080',
        ws: true,
        rewrite: (path) => path.replace(/^\/web-pty\/ws/, '/ws')
      },
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
