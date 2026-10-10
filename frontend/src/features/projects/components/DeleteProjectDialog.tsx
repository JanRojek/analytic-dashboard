import {Alert, Button, Modal, TextInput} from "@mantine/core";

import type {Project} from "../../../data/types";
import {useState} from "react";

import {useDeleteProject} from "../hooks";

export function DeleteProjectDialog({
    project,
    onClose,
}: {
    project: Project;
    onClose: () => void;
}) {
    const [deleteName, setDeleteName] = useState("");
    const remove = useDeleteProject();

    return (
        <Modal
            opened
            onClose={() => {
                if (!remove.isPending) onClose();
            }}
            closeOnClickOutside={!remove.isPending}
            closeOnEscape={!remove.isPending}
            withCloseButton={!remove.isPending}
            title="Delete this project?"
            centered
        >
            <p className="muted">
                This permanently removes <strong>{project.name}</strong> and its
                datasets, dashboards, and charts. This action cannot be undone.
            </p>

            <TextInput
                mt="lg"
                label={`Type “${project.name}” to confirm`}
                value={deleteName}
                disabled={remove.isPending}
                onChange={(e) => setDeleteName(e.currentTarget.value)}
            />

            {remove.error && (
                <Alert color="red" mt="md">
                    {remove.error.message}
                </Alert>
            )}

            <div className="dialog-actions">
                <Button
                    variant="default"
                    disabled={remove.isPending}
                    onClick={onClose}
                >
                    Keep project
                </Button>

                <Button
                    color="red"
                    disabled={deleteName !== project.name}
                    loading={remove.isPending}
                    onClick={() => {
                        remove.mutate(project.id, {
                            onSuccess: onClose,
                        });
                    }}
                >
                    Delete project
                </Button>
            </div>
        </Modal>
    );
}
