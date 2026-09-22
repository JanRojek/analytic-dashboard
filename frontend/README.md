# Aperture frontend

A project-centered analytics prototype for **Analytic Dashboard**, built with React, TypeScript, Vite, Mantine, React Router, and TanStack Query.

## Run locally

Use Node.js 24 and npm. From `frontend/`:

```sh
npm install
npm run dev
```

Open [the frontend](http://127.0.0.1:5173). On the sign-in screen, choose **Explore the demo workspace**. No backend or account is required for this explicit demo.

Open **Retail performance** to inspect its seeded dataset and dashboard, or create a project. **Add data → Try it with sample data** supplies a real CSV. Continue through data quality, local preparation, Explore, and the dashboard editor/presentation.

```sh
npm run build
npm run lint
npm test
```

Tests exercise CSV parsing, profile/query semantics, preparation, browser persistence and failure handling, the local analytical journey at the adapter level, editor draft recovery, and API request/error contracts. These automated tests do not run a browser or a live backend. The build emits `dist/`; `npm run preview` serves that output locally. A deployed build needs an equivalent same-origin `/api` reverse proxy and SPA route fallback.

## Connect to the existing backend

Sign-in and registration forms use real authentication. Failures never enter the demo automatically. The development proxy sends `/api/*` to `https://localhost:7208`, removing `/api`; cookies are included and cookie paths are rewritten to `/` so registration cookies reach the proxied auth routes. Certificate verification is disabled only for this local development proxy.

To select another backend, create an uncommitted `frontend/.env.local`:

```dotenv
API_PROXY_TARGET=https://localhost:7208
```

Restart Vite after changing it. Keep the browser origin consistent; `localhost` and `127.0.0.1` have separate cookies and local storage.

The backend requires its pinned .NET SDK **10.0.303**, PostgreSQL connection configuration (`ConnectionStrings:Default`), applied migrations, and email delivery. Its existing SMTP defaults target `localhost:1025`; provide an SMTP service or your own SMTP configuration. Registration requires a confirmed email.

When launching the backend, override its frontend email-link origin to match this app. For PowerShell:

```powershell
$env:Frontend__BaseUrl = 'http://127.0.0.1:5173'
dotnet run --project backend/src/AnalyticDashboard.Api --launch-profile https
```

Run that command from the repository root with the existing backend prerequisites configured. The committed backend origin is `https://localhost:1234`; without the override, confirmation and password-reset links target that older address. Backend configuration and services were not modified by the frontend implementation.

During review the API and SMTP ports were not running, so a complete live login/import journey remains unverified.

## Where behavior lives

- `src/data/types.ts`, `adapter.ts`, and `api.ts`: shared contracts and real API access.
- `src/mocks/`: sample data, local adapter, CSV/profile/query/preparation logic, and editor persistence.
- `src/components/SessionProvider.tsx`: explicit demo/connected sessions and expiry recovery.
- `src/pages/`: project, authentication, dataset, analytics, and dashboard experiences.

Demo data and changes persist in this browser. Preparation creates a copy of the full source dataset. Import preview accepts CSV with headers, up to **10 MB / 20,000 rows** in either mode. Dataset browsing shows ten preview rows; profile and demo calculations use all rows.

In connected mode, creating or deleting dashboards/charts immediately uses the backend. **Layout, existing-chart edits, and exploration drafts remain browser-local** and are labeled accordingly. Save the editor to update its presentation layout and configuration. Presentation uses current widget records and dataset versions; it is not a frozen published snapshot. Clearing site storage deletes local drafts/data; it does not delete server projects.

Read the [frontend concept](../docs/frontend-concept.md) and [frontend/backend gaps](../FRONTEND_BACKEND_GAPS.md) for product decisions and remaining integration boundaries.
