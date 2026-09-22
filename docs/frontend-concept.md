# Aperture — frontend concept

**Aperture** is a temporary product identity for Analytic Dashboard: a calm workspace that turns a dataset into an understandable question and then a presentation canvas. Backend names and domain concepts remain unchanged.

## Information architecture

- **Projects Center** is the stable home: searchable projects, grid/list views, creation, renaming, and deletion. Project previews use available chart data.
- **Project workspace** has four tabs: **Overview**, **Data**, **Explore**, and **Dashboards**. A project contains multiple datasets and dashboards; a dashboard can combine charts from different datasets in that project.
- **Dataset workspace** combines row preview, data quality, and preparation. Profiling is a step in understanding data, not a disconnected administrative entity.
- **Explore** combines query construction and visualization. Choose a dataset, grouping, measure, and calculation; inspect a chart or result table; add it to a dashboard.
- **Dashboard editor** leaves the main sidebar behind. An outline, central canvas, and contextual inspector support chart selection, ordering, width, and configuration. Presentation removes editing controls.

The sidebar stays focused on projects and orientation. Backend entities such as import jobs and dataset versions appear through contextual status, not independent navigation sections.

## Representative journey

Sign in or register with email confirmation, or explicitly enter the demo. Open Retail performance or create an empty project. Add a CSV, review its first rows, and import it. Inspect columns and missing values. In the demo, trim text or fill missing values and create a prepared copy. Explore a grouped measure, choose a visualization, add it to a dashboard, arrange the canvas, save, and open presentation.

An empty project demonstrates the same journey without seeded content. Existing work reopens through project navigation and persistent browser drafts.

## Product decisions

- Preparation runs against the entire local source and creates a new dataset. The original stays available; the ten-row preview is never mistaken for the transformation input.
- Completeness means missing-cell coverage. It is not presented as proof of accuracy or duplicate detection.
- Query labels explain the calculation; Count explicitly counts nonempty measure values. Changing a question marks existing results as stale until the query runs again.
- Imports expose an honest waiting state when the backend has no ready version. Progress percentages and failure details are not invented.
- Destructive actions require confirmation. Dataset deletion checks dashboard references and directs users to the affected dashboards.
- Loading, retry, empty, and storage-failure states belong to each workflow. Keyboard focus, semantic tables, Mantine dialogs, and reduced-motion support provide the interaction foundation.

## Visual direction

Warm white surfaces, pale sage sections, forest-green actions, fine borders, modest radii, and a consistent system sans-serif create a restrained identity. Composed sections establish hierarchy in the Projects Center; analytical views become denser and more focused. Motion is limited to feedback and transitions. General pages adapt to narrow screens; canvas authoring prioritizes desktop space.

The product mark lives in `frontend/src/components/Brand.tsx`; theme and surface tokens live in `main.tsx` and the frontend styles.

## Integration boundary

React, TypeScript, Vite, Mantine, React Router, and TanStack Query are retained. Presentation uses the shared `DataAdapter`. `data/api.ts` calls existing endpoints; `mocks/` owns sample content, browser persistence, local calculations, preparation, and editor drafts. Demo entry is explicit and never substitutes for a failed API request. Connected mode uses real projects, imports, profiles, grouped queries, dashboards, and new chart persistence. Layout and existing-chart edits remain visibly browser-local in both modes.

See [frontend/backend gaps](../FRONTEND_BACKEND_GAPS.md) and [run instructions](../frontend/README.md).
