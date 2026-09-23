type Id = string | number | undefined;

export const queryKeys = {
    projects: {
        all: ["projects"] as const,
        detail: (projectId: Id) => ["project", projectId] as const,
    },

    datasets: {
        list: (projectId: Id) => ["datasets", projectId] as const,

        detail: (projectId: Id, datasetId: Id) =>
            ["dataset", projectId, datasetId] as const,

        usage: (projectId: Id, datasetId: Id) =>
            ["dataset-usage", projectId, datasetId] as const,

        profile: (projectId: Id, datasetId: Id) =>
            ["profile", projectId, datasetId] as const,

        preparedPreview: (
            projectId: Id,
            datasetId: Id,
            steps: unknown,
        ) => ["prepared-preview", projectId, datasetId, steps] as const,
    },

    dashboards: {
        list: (projectId: Id) => ["dashboards", projectId] as const,

        detail: (projectId: Id, dashboardId: Id) =>
            ["dashboard", projectId, dashboardId] as const,

        widgets: (projectId: Id, dashboardId: Id) =>
            ["widgets", projectId, dashboardId] as const,

        widgetData: (
            projectId: Id,
            dashboardId: Id,
            widgetId: Id,
        ) => ["widget-data", projectId, dashboardId, widgetId] as const,
    },

    analytics: {
        chartData: (
            projectId: Id,
            datasetId: Id,
            config: unknown,
        ) => ["chart-data", projectId, datasetId, config] as const,

        exploration: (
            projectId: Id,
            datasetId: Id,
            versionId: Id,
            config: unknown,
        ) =>
            [
                "exploration",
                projectId,
                datasetId,
                versionId,
                config,
            ] as const,
    },
} as const;
