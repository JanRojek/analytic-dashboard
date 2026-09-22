# Cairn frontend

React + TypeScript + Vite frontend for the Analytic Dashboard thesis project.
"Cairn" is a temporary product name; branding lives in `src/brand/` and is easy to replace.

## Run

```bash
npm install
npm run dev      # http://localhost:5173, proxies /api -> http://localhost:5151
npm run build    # type-check + production build
npm run lint
```

The dev server expects the ASP.NET Core API on `http://localhost:5151` (the `http` launch profile).

## Where things live

- `src/api/` – typed HTTP client and endpoint modules mirroring the backend contracts.
- `src/features/` – product areas (auth, projects, workspace, data, prepare, explore, dashboards).
- `src/charts/` – the in-house SVG chart set used by Explore and dashboards.
- `src/mocks/` – local adapters for capabilities the backend does not provide yet
  (dashboard layout, preparation recipes, recent items). See `../FRONTEND_BACKEND_GAPS.md`.
- `docs/frontend-concept.md` (repo root) – information architecture and design rationale.
