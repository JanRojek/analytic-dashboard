import type { DataAdapter, Dataset, Project, Widget } from "../data/types.ts";
import { parseCsv } from "./csv.ts";
import { profileRows, queryRows } from "./analytics.ts";
import {
  mutateDemoState,
  normalizedName,
  readDemoState,
  requireDashboard,
  requireDataset,
  requireProject,
} from "./store.ts";

const now = () => new Date().toISOString();
const id = () => crypto.randomUUID();

export const demoAdapter: DataAdapter = {
  async listProjects() {
    return readDemoState().projects;
  },
  async getProject(p) {
    return requireProject(readDemoState(), p);
  },
  async createProject(name, description) {
    return mutateDemoState((state) => {
      const clean = normalizedName(name);
      if (
        state.projects.some((p) => p.name.toLowerCase() === clean.toLowerCase())
      )
        throw new Error("A project with this name already exists.");
      const project: Project = {
        id: id(),
        name: clean,
        description: description?.trim(),
        color: "teal",
        createdAtUtc: now(),
      };
      state.projects.unshift(project);
      return project;
    });
  },
  async renameProject(p, name) {
    return mutateDemoState((state) => {
      const project = requireProject(state, p);
      const clean = normalizedName(name);
      if (
        state.projects.some(
          (other) =>
            other.id !== p && other.name.toLowerCase() === clean.toLowerCase(),
        )
      )
        throw new Error("A project with this name already exists.");
      project.name = clean;
      return project;
    });
  },
  async deleteProject(p) {
    mutateDemoState((state) => {
      requireProject(state, p);
      const datasetIds = new Set(
        state.datasets
          .filter((d) => d.projectId === p)
          .map((d) => d.dataset.id),
      );
      const dashboardIds = new Set(
        state.dashboards.filter((d) => d.projectId === p).map((d) => d.id),
      );
      state.projects = state.projects.filter((item) => item.id !== p);
      state.datasets = state.datasets.filter((item) => item.projectId !== p);
      state.dashboards = state.dashboards.filter(
        (item) => item.projectId !== p,
      );
      state.widgets = state.widgets.filter(
        (item) => !dashboardIds.has(item.dashboardId),
      );
      for (const datasetId of datasetIds) delete state.recipes[datasetId];
    });
  },
  async listDatasets(p) {
    const state = readDemoState();
    requireProject(state, p);
    return state.datasets
      .filter((item) => item.projectId === p)
      .map((item) => item.dataset);
  },
  async getDataset(p, d) {
    return requireDataset(readDemoState(), p, d).dataset;
  },
  async getImportStatus(p, d) {
    requireDataset(readDemoState(), p, d);
    return null;
  },
  async getProfile(p, d) {
    const item = requireDataset(readDemoState(), p, d);
    return profileRows(item.dataset, item.columns, item.rows);
  },
  async getDatasetRows(p, d, page, pageSize) {
    const item = requireDataset(readDemoState(), p, d);

    if (pageSize < 1 || pageSize > 100) {
      throw new Error("Page size must be between 1 and 100.");
    }

    const normalizedPage = page <= 0 ? 1 : page;

    const totalRows = item.rows.length;
    const totalPages = Math.max(1, Math.ceil(totalRows / pageSize));

    const actualPage = Math.min(normalizedPage, totalPages);

    const offset = (actualPage - 1) * pageSize;

    return {
      items: item.rows.slice(offset, offset + pageSize),
      page: actualPage,
      pageSize,
      totalRows,
      totalPages,
    };
  },
  async importCsv(p, file) {
    if (!/\.csv$/i.test(file.name))
      throw new Error("Choose a .csv file to import.");
    if (file.size > 10 * 1024 * 1024)
      throw new Error("The local demo supports files up to 10 MB.");
    const { columns, rows } = parseCsv(await file.text());
    return mutateDemoState((state) => {
      requireProject(state, p);
      const dataset: Dataset = {
        id: id(),
        name: normalizedName(file.name.replace(/\.csv$/i, ""), 200),
        createdAtUtc: now(),
        currentVersion: {
          id: id(),
          versionNumber: 1,
          originalFileName: file.name,
          rowCount: rows.length,
          columnCount: columns.length,
        },
      };
      state.datasets.unshift({ projectId: p, dataset, columns, rows });
      return { datasetId: dataset.id };
    });
  },
  async deleteDataset(p, d) {
    mutateDemoState((state) => {
      requireDataset(state, p, d);
      if (state.widgets.some((widget) => widget.datasetId === d))
        throw new Error(
          "This dataset is used by a dashboard. Remove its charts before deleting it.",
        );
      state.datasets = state.datasets.filter((item) => item.dataset.id !== d);
      delete state.recipes[d];
    });
  },
  async query(p, d, config) {
    const item = requireDataset(readDemoState(), p, d);
    return queryRows(item.columns, item.rows, config);
  },
  async listDashboards(p) {
    const state = readDemoState();
    requireProject(state, p);
    return state.dashboards.filter((item) => item.projectId === p);
  },
  async getDashboard(p, d) {
    return requireDashboard(readDemoState(), p, d);
  },
  async createDashboard(p, name) {
    return mutateDemoState((state) => {
      requireProject(state, p);
      const dashboard = {
        id: id(),
        projectId: p,
        name: normalizedName(name),
        createdAtUtc: now(),
      };
      state.dashboards.unshift(dashboard);
      return dashboard;
    });
  },
  async deleteDashboard(p, d) {
    mutateDemoState((state) => {
      requireDashboard(state, p, d);
      state.dashboards = state.dashboards.filter((item) => item.id !== d);
      state.widgets = state.widgets.filter((item) => item.dashboardId !== d);
    });
  },
  async listWidgets(p, d) {
    const state = readDemoState();
    requireDashboard(state, p, d);
    return state.widgets.filter((item) => item.dashboardId === d);
  },
  async createWidget(p, d, input) {
    return mutateDemoState((state) => {
      requireDashboard(state, p, d);
      const data = requireDataset(state, p, input.datasetId);
      queryRows(data.columns, data.rows, input);
      if (!["Kpi", "BarChart", "LineChart", "PieChart"].includes(input.type))
        throw new Error("Choose a supported chart type.");
      const widget: Widget = {
        ...input,
        id: id(),
        title: normalizedName(input.title, 200),
        dashboardId: d,
        createdAtUtc: now(),
      };
      state.widgets.push(widget);
      return widget;
    });
  },
  async deleteWidget(p, d, w) {
    mutateDemoState((state) => {
      requireDashboard(state, p, d);
      if (
        !state.widgets.some(
          (widget) => widget.dashboardId === d && widget.id === w,
        )
      )
        throw new Error("This chart was not found.");
      state.widgets = state.widgets.filter((item) => item.id !== w);
    });
  },
  async widgetData(p, d, w) {
    const state = readDemoState();
    requireDashboard(state, p, d);
    const widget = state.widgets.find(
      (item) => item.dashboardId === d && item.id === w,
    );
    if (!widget) throw new Error("This chart was not found.");
    const data = requireDataset(state, p, widget.datasetId);
    return queryRows(data.columns, data.rows, widget);
  },

  async updateDashboardLayout(p, d, changes) {
    mutateDemoState((state) => {
      requireDashboard(state, p, d);

      const widgets = state.widgets.filter(
          (widget) => widget.dashboardId === d,
      );

      const layoutsById = new Map(
          changes.map((change) => [
            change.widgetId,
            change.newLayout,
          ]),
      );

      if (
          layoutsById.size !== changes.length ||
          layoutsById.size !== widgets.length ||
          widgets.some((widget) => !layoutsById.has(widget.id))
      ) {
        throw new Error(
            "Provided widget IDs do not match the dashboard widgets.",
        );
      }

      for (const widget of widgets) {
        widget.layout = layoutsById.get(widget.id)!;
      }
    });
  },

  async updateDashboardWidgets(p, d, updates) {
    mutateDemoState((state) => {
      requireDashboard(state, p, d);

      const widgets = state.widgets.filter(
          (widget) => widget.dashboardId === d,
      );

      const updatesById = new Map(
          updates.map((update) => [update.widgetId, update]),
      );

      if (
          updatesById.size !== updates.length ||
          updatesById.size !== widgets.length ||
          widgets.some((widget) => !updatesById.has(widget.id))
      ) {
        throw new Error(
            "Provided widget IDs do not match the dashboard widgets.",
        );
      }

      const validated = updates.map((update) => {
        const title = normalizedName(update.title, 200);
        const groupByColumn = update.groupByColumn.trim();
        const measureColumn = update.measureColumn.trim();

        if (
            !groupByColumn ||
            !measureColumn ||
            [...groupByColumn].length > 255 ||
            [...measureColumn].length > 255
        ) {
          throw new Error("Invalid widget column configuration.");
        }

        if (
            !["Kpi", "BarChart", "LineChart", "PieChart"].includes(
                update.type,
            )
        ) {
          throw new Error("Unsupported widget type.");
        }

        const { x, y, width, height } = update.layout;

        if (
            ![x, y, width, height].every(Number.isInteger) ||
            x < 0 ||
            y < 0 ||
            width < 6 ||
            height < 14 ||
            x + width > 24
        ) {
          throw new Error("Invalid widget layout.");
        }

        const dataset = requireDataset(state, p, update.datasetId);

        queryRows(dataset.columns, dataset.rows, {
          groupByColumn,
          measureColumn,
          aggregation: update.aggregation,
        });

        return {
          ...update,
          title,
          groupByColumn,
          measureColumn,
        };
      });

      for (let i = 0; i < validated.length; i++) {
        for (let j = i + 1; j < validated.length; j++) {
          const a = validated[i].layout;
          const b = validated[j].layout;

          const intersects =
              a.x < b.x + b.width &&
              b.x < a.x + a.width &&
              a.y < b.y + b.height &&
              b.y < a.y + a.height;

          if (intersects) {
            throw new Error("Widget layouts must not overlap.");
          }
        }
      }

      const validatedById = new Map(
          validated.map((update) => [update.widgetId, update]),
      );

      for (const widget of widgets) {
        const update = validatedById.get(widget.id)!;

        Object.assign(widget, {
          datasetId: update.datasetId,
          type: update.type,
          title: update.title,
          groupByColumn: update.groupByColumn,
          measureColumn: update.measureColumn,
          aggregation: update.aggregation,
          layout: { ...update.layout },
        });
      }
    });
  },
};
