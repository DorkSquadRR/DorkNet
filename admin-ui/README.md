# admin-ui/

React + Vite + Tailwind SPA. Talks to `/api/admin/v1/*` on the server.

Embedded into:
1. The standalone server's `wwwroot/admin/` (advanced mode), served at
   `https://admin.<your-domain>/`.
2. The Easy desktop app via a Photino WebView (host mode admin tab).

## Dev

```bash
npm install
npm run dev    # http://localhost:5173, proxies /api → :5000
```

Start the server first; Vite's proxy doesn't synthesize endpoints.

## Build

```bash
npm run build  # dist/ — copied into server/wwwroot/admin by the
               # server's CI publish step, and into easy-app/Resources/
               # by tools/build-easy-app.ps1
```

## Stack notes

- React 18 + Vite 5 + TypeScript
- Tailwind 3 with a custom dark `ink/brand` palette in `tailwind.config.js`
- React Router 6
- No external component library; primitives in `src/components/`
- No state library beyond per-page `useState` + `useApi`

See [docs/architecture.md](../docs/architecture.md) for how the SPA is
delivered in both modes.
