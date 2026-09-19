import { AspectRatio, Box, Paper, Stack, Text } from "@mantine/core";
import type { Project } from "../features/projects/types";
import classes from "./ProjectCard.module.css";

function ProjectCard({ project }: { project: Project }) {
    const formattedDate = new Date(project.createdAtUtc).toLocaleDateString(
        "en-GB",
        {
            day: "numeric",
            month: "short",
            year: "numeric",
        }
    );

    return (
        <Paper
            component="article"
            className={classes.card}
            withBorder
            radius="md"
            p={0}
        >
            <AspectRatio ratio={16 / 9}>
                <Box bg="gray.1" />
            </AspectRatio>

            <Stack gap={4} p="md">
                <Text fw={600}>
                    {project.name}
                </Text>

                <Text size="sm" c="dimmed">
                    Created at: {formattedDate}
                </Text>
            </Stack>
        </Paper>
    );
}

export default ProjectCard;
