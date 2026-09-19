import type { Project } from "../features/projects/types";
import ProjectCard from "../components/ProjectCard";
import {
    Button,
    Group,
    SimpleGrid,
    Stack,
    TextInput,
    Title
} from "@mantine/core";

const mockProjects: Project[] = [
    { id: '1', name: "First project", createdAtUtc: "2026-01-01T12:00:00Z" },
    { id: '2', name: "Second project", createdAtUtc: "2026-02-01T12:00:00Z" },
    { id: '3', name: "Third project", createdAtUtc: "2026-03-01T12:00:00Z" },
    { id: '4', name: "Fourth project", createdAtUtc: "2026-04-01T12:00:00Z" },
    { id: '5', name: "Fifth project", createdAtUtc: "2026-05-01T12:00:00Z" },
    { id: '6', name: "Sixth project", createdAtUtc: "2026-06-01T12:00:00Z" }
];

function ProjectsPage()
{
    return (
        <Stack>
            <Group justify="space-between">
                <Title order={1}>Your Projects</Title>

                <Button>New project</Button>
            </Group>

            <Group>
                <TextInput
                    placeholder="Search projects..."
                />

                <Button variant="default">
                    Sort
                </Button>
            </Group>

            <SimpleGrid
                cols={{ base: 1, sm: 2, lg: 3, xl: 4 }}
                spacing="lg"
            >
                {mockProjects.map((project) => (
                    <ProjectCard
                        key={project.id}
                        project={project}
                    />
                ))}
            </SimpleGrid>
        </Stack>
    );
}

export default ProjectsPage;
