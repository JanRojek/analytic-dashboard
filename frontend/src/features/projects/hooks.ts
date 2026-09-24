import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useSession } from "../../data/session";
import { queryKeys } from "../../data/queryKeys";

export function useProjects() {
    const { adapter } = useSession();

    return useQuery({
        queryKey: queryKeys.projects.all,
        queryFn: () => adapter.listProjects(),
    });
}

export function useCreateProject() {
    const { adapter } = useSession();
    const queryClient = useQueryClient();

    return useMutation({
        mutationFn: ({
                         name,
                         description,
                     }: {
            name: string;
            description: string;
        }) => adapter.createProject(name, description),

        onSuccess: async (project) => {
            await queryClient.invalidateQueries({
                queryKey: queryKeys.projects.all,
            });

            await queryClient.invalidateQueries({
                queryKey: queryKeys.projects.detail(project.id),
            });
        },
    });
}

export function useRenameProject() {
    const { adapter } = useSession();
    const queryClient = useQueryClient();

    return useMutation({
        mutationFn: ({
                         projectId,
                         name,
                     }: {
            projectId: string;
            name: string;
        }) => adapter.renameProject(projectId, name),

        onSuccess: async (project) => {
            await queryClient.invalidateQueries({
                queryKey: queryKeys.projects.all,
            });

            await queryClient.invalidateQueries({
                queryKey: queryKeys.projects.detail(project.id),
            });
        },
    });
}

export function useDeleteProject() {
    const { adapter } = useSession();
    const queryClient = useQueryClient();

    return useMutation({
        mutationFn: (projectId: string) => adapter.deleteProject(projectId),

        onSuccess: async (_, projectId) => {
            queryClient.removeQueries({
                predicate: (query) => query.queryKey[1] === projectId,
            });

            await queryClient.invalidateQueries({
                queryKey: queryKeys.projects.all,
            });
        },
    });
}
