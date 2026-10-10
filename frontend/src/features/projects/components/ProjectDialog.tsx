import type {Project} from "../../../data/types.ts";
import {useNavigate} from "react-router-dom";
import {type FormEvent, useState} from "react";
import {useCreateProject, useRenameProject} from "../hooks.ts";
import {Alert, Button, Modal, Textarea, TextInput} from "@mantine/core";

export function ProjectDialog({
    opened,
    onClose,
    project
}: {
    opened: boolean;
    onClose: () => void;
    project?: Project;
}) {
    const navigate = useNavigate();
    const [name, setName] = useState(project?.name || "");
    const [description, setDescription] = useState("");

    const createProject = useCreateProject();
    const renameProject = useRenameProject();

    const isPending = createProject.isPending || renameProject.isPending;
    const error = project ? renameProject.error : createProject.error;

    function submit(e: FormEvent) {
        e.preventDefault();

        const trimmedName = name.trim();

        if (!trimmedName || isPending) return;

        if (project) {
            renameProject.mutate(
                {
                    projectId: project.id,
                    name: trimmedName,
                },
                {
                    onSuccess: () => {
                        onClose();
                    },
                },
            );

            return;
        }

        createProject.mutate(
            {
                name: trimmedName,
                description: description.trim(),
            },
            {
                onSuccess: (createdProject) => {
                    onClose();
                    navigate(`/projects/${createdProject.id}`);
                },
            },
        );
    }

    return (
        <Modal
            opened={opened}
            onClose={() => {
                if (!isPending) onClose();
            }}
            closeOnClickOutside={!isPending}
            closeOnEscape={!isPending}
            withCloseButton={!isPending}
            title={project ? "Rename project" : "Make room for a new question"}
            centered
            size="md"
        >
            <form onSubmit={submit}>
                <div className="new-project-intro">
                    <p className="muted">
                        {project
                            ? "Give this project a name that makes it easy to recognize."
                            : "Start with a name. You can add your first data source once the project is ready."}
                    </p>
                </div>

                <TextInput
                    label="Project name"
                    placeholder="e.g. Retail performance"
                    value={name}
                    onChange={(e) => setName(e.currentTarget.value)}
                    disabled={isPending}
                    autoFocus
                    required
                />

                {!project && (
                    <Textarea
                        mt="md"
                        label="What are you exploring?"
                        description="Optional"
                        placeholder="A short note about the question or topic behind this project."
                        value={description}
                        onChange={(e) => setDescription(e.currentTarget.value)}
                        disabled={isPending}
                        autosize
                        minRows={3}
                    />
                )}

                {error && (
                    <Alert color="red" mt="md">
                        {error instanceof Error
                            ? error.message
                            : "Something went wrong. Please try again."}
                    </Alert>
                )}

                <div className="dialog-actions">
                    <Button
                        variant="default"
                        type="button"
                        disabled={isPending}
                        onClick={onClose}
                    >
                        Cancel
                    </Button>

                    <Button
                        type="submit"
                        loading={isPending}
                        disabled={!name.trim()}
                    >
                        {project ? "Save changes" : "Create project"}
                    </Button>
                </div>
            </form>
        </Modal>
    );
}
