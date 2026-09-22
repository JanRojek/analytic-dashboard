import react from '@vitejs/plugin-react'
import { defineConfig } from 'vite'

// The API is reached through the `/api` prefix so that browser navigation to
// application routes such as `/projects` never collides with backend routes.
// The backend scopes its registration cookie to `/auth`, so that cookie path is
// rewritten to the proxied prefix; the identity cookie lives at `/` and is kept.
export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    proxy: {
      '/api': {
        target: 'http://localhost:5151',
        changeOrigin: true,
        rewrite: (path) => path.replace(/^\/api/, ''),
        cookiePathRewrite: { '/auth': '/api/auth', '/': '/' },
      },
    },
  },
})
