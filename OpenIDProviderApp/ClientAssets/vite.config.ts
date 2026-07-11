import { defineConfig } from 'vite';

// Bundle vendor client assets (Bootstrap) into wwwroot/lib instead of pulling them
// from a CDN. Fixed output names keep the _Layout references stable; ASP.NET Core's
// asp-append-version tag helper appends a content hash for cache busting.
export default defineConfig({
  build: {
    outDir: '../wwwroot/lib',
    emptyOutDir: true,
    rollupOptions: {
      input: 'src/main.ts',
      output: {
        // ES output keeps Vite's default CSS extraction (an IIFE/UMD bundle would
        // inline the stylesheet into the JS and inject it late, causing a FOUC).
        // The _Layout loads this with <script type="module">.
        entryFileNames: 'bootstrap-bundle.js',
        assetFileNames: 'bootstrap-bundle[extname]',
      },
    },
  },
});
