import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';
import path from 'node:path';

// Source lives at DorkNet.Server/admin-ui; build emits straight into
// DorkNet.Server/wwwroot/admin so the static file middleware picks it
// up at admin.localhost without any extra publish wiring.
export default defineConfig({
  plugins: [react()],
  // Relative-base so the built index.html references its assets as
  // `./assets/...` instead of `/assets/...`. Required when the SPA is
  // served under a path prefix like the Easy Launcher's single-origin
  // mode (`https://<apex>/__dn/admin/`) — a root-absolute asset path
  // would skip the /__dn/admin/ prefix and 404 on the wrong server
  // host. Still works fine for the subdomain case
  // (`admin.<apex>/...`) since `./assets/...` resolves against the
  // document URL in both shapes.
  base: './',
  resolve: {
    alias: {
      '@': path.resolve(__dirname, 'src'),
    },
  },
  build: {
    outDir: path.resolve(__dirname, '../wwwroot/admin'),
    emptyOutDir: true,
    target: 'es2022',
    sourcemap: true,
  },
  server: {
    port: 5173,
    proxy: {
      // Dev convenience: `npm run dev` proxies API calls to the
      // running ASP.NET server so we can iterate on the SPA with HMR
      // without dealing with CORS. Adjust target if the server runs
      // on a non-default port locally.
      '/api': {
        target: 'http://localhost:80',
        changeOrigin: true,
        secure: false,
      },
    },
  },
});
