import { useQuery } from "@tanstack/react-query";
import { useSession } from "../../data/session";
import { queryKeys } from "../../data/queryKeys";
import type { QueryConfig } from "../../data/types";

export function useChartData(
    projectId: string,
    datasetId: string,
    config: QueryConfig,
    enabled = true,
) {
    const { adapter } = useSession();

    return useQuery({
        queryKey: queryKeys.analytics.chartData(
            projectId,
            datasetId,
            config,
        ),
        queryFn: () => adapter.query(projectId, datasetId, config),
        enabled,
    });
}

export function useExplorationResults(
    projectId: string,
    datasetId: string,
    versionId: string | undefined,
    config: QueryConfig,
    enabled = true,
) {
    const { adapter } = useSession();

    return useQuery({
        queryKey: queryKeys.analytics.exploration(
            projectId,
            datasetId,
            versionId,
            config,
        ),
        queryFn: () => adapter.query(projectId, datasetId, config),
        enabled,
    });
}
