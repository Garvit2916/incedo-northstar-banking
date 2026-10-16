import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

// The development server proxies API calls to the running backend so the SPA
// can be developed without CORS friction. In production the API base URL is
// supplied through VITE_API_BASE_URL at build time.
export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    proxy: {
      '/api': {
        target: 'http://localhost:5199',
        changeOrigin: true,
      },
      '/health': {
        target: 'http://localhost:5199',
        changeOrigin: true,
      },
    },
  },
});
