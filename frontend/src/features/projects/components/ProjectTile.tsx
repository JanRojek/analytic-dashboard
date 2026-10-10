import type {Project} from "../../../data/types.ts";
import {useDatasets} from "../../data/hooks.ts";
import {useDashboards} from "../../dashboards/hooks.ts";
import {Link} from "react-router-dom";
import {Icon} from "../../../components/Icon.tsx";
import {ActionIcon, Menu} from "@mantine/core";

function formatDate(value: string) {
    return new Date(value).toLocaleDateString("en-GB", {
        day: "numeric",
        month: "short",
        year: "numeric",
    });
}

export function ProjectTile({
    project,
    index,
    onRename,
    onDelete,
}: {
    project: Project;
    index: number;
    onRename: () => void;
    onDelete: () => void;
}) {
    const datasets = useDatasets(project.id);
    const dashboards = useDashboards(project.id);
    return (
        <article className={`project-card project-tone-${index % 3}`}>
            <Link to={`/projects/${project.id}`} className="project-card-link">
                <div className="project-preview">
                    <div className="preview-window">
                        <div className="preview-window-head">
                            <span/>
                            <span/>
                            <span/>
                            <i>PROJECT OVERVIEW</i>
                        </div>
                        <div className="project-preview-template" aria-hidden="true">
                            <div className="preview-template-kpi">
                                <span/>
                                <strong/>
                            </div>

                            <div className="preview-template-chart">
                                <span/>
                                <span/>
                                <span/>
                                <span/>
                                <span/>
                                <span/>
                            </div>

                            <div className="preview-template-lines">
                                <span/>
                                <span/>
                                <span/>
                            </div>
                        </div>
                    </div>
                    <span className="project-preview-tag">
            {datasets.data?.length ? "IN PROGRESS" : "GETTING STARTED"}
          </span>
                </div>
                <div className="project-card-content">
                    <h3>{project.name}</h3>
                    <p>
                        {project.description ||
                            "A shared home for your data and your next discovery."}
                    </p>
                    <div className="project-card-meta">
            <span>
              <Icon name="data" size={14}/>
                {datasets.data?.length ?? "—"}{" "}
                {datasets.data?.length === 1 ? "dataset" : "datasets"}
            </span>
                        <span>
              <Icon name="dashboard" size={14}/>
                            {dashboards.data?.length ?? "—"}{" "}
                            {dashboards.data?.length === 1 ? "dashboard" : "dashboards"}
            </span>
                    </div>
                </div>
            </Link>
            <div className="project-card-footer">
                <span>Created {formatDate(project.createdAtUtc)}</span>
                <Menu position="bottom-end" shadow="md">
                    <Menu.Target>
                        <ActionIcon
                            variant="subtle"
                            color="gray"
                            aria-label={`Options for ${project.name}`}
                        >
                            <Icon name="more"/>
                        </ActionIcon>
                    </Menu.Target>
                    <Menu.Dropdown>
                        <Menu.Item
                            leftSection={<Icon name="edit" size={15}/>}
                            onClick={onRename}
                        >
                            Rename project
                        </Menu.Item>
                        <Menu.Item
                            color="red"
                            leftSection={<Icon name="trash" size={15}/>}
                            onClick={onDelete}
                        >
                            Delete project
                        </Menu.Item>
                    </Menu.Dropdown>
                </Menu>
            </div>
        </article>
    );
}
