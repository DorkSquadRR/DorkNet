import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';
import path from 'node:path';

// Public-facing site. Source lives at DorkNet.Server/site; build emits
// straight into DorkNet.Server/wwwroot/site so the static file
// middleware picks it up at the apex localhost / rec.net hosts without
// any extra publish wiring.
export default defineConfig({
  plugins: [react()],
  // Relative-base — see admin-ui/vite.config.ts. Same single-origin
  // path-prefix scenario applies if the site ever gets opened through
  // `https://<apex>/__dn/www/`.
  base: './',
  resolve: {
    alias: { '@': path.resolve(__dirname, 'src') },
  },
  build: {
    outDir: path.resolve(__dirname, '../wwwroot/site'),
    emptyOutDir: true,
    target: 'es2022',
    sourcemap: true,
    rollupOptions: {
      output: {
        entryFileNames: 'assets/[name]-join-password-[hash].js',
        chunkFileNames: 'assets/[name]-join-password-[hash].js',
      },
    },
  },
  server: {
    port: 5174,
    proxy: {
      // Dev convenience: `npm run dev` proxies API calls to the
      // running ASP.NET server so we can iterate on the SPA with HMR
      // without dealing with CORS.
      '/api': {
        target: 'http://localhost:80',
        changeOrigin: true,
        secure: false,
      },
    },
  },
});
