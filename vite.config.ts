import react from '@vitejs/plugin-react';
import { defineConfig, type Plugin } from 'vite';

// Production-only Content-Security-Policy (dev server needs inline HMR scripts).
const csp = (): Plugin => ({
  name: 'winsolar-csp',
  apply: 'build',
  transformIndexHtml(html) {
    const policy =
      "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; " +
      "img-src 'self' data:; font-src 'self'; connect-src 'self' https://geocoding-api.open-meteo.com; " +
      "object-src 'none'; base-uri 'self'; form-action 'none'";
    return html.replace('<head>', `<head>\n    <meta http-equiv="Content-Security-Policy" content="${policy}" />`);
  },
});

export default defineConfig({
  base: './',
  plugins: [react(), csp()],
  server: { port: 3000, host: 'localhost' },
});
