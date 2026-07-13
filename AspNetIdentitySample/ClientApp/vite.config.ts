import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';
import tailwindcss from '@tailwindcss/vite';

// The auth SPA is served by the provider: the ASP.NET host returns index.html for /Auth/Login and
// /Auth/Register, and serves the hashed assets under /auth/. Building into the provider's
// wwwroot/auth means no dev-server proxy is involved in the OIDC redirect flow.
export default defineConfig({
  base: '/auth/',
  plugins: [react(), tailwindcss()],
  build: {
    outDir: '../wwwroot/auth',
    emptyOutDir: true,
  },
});
