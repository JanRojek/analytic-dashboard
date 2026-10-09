import type {Dashboard, Dataset} from "../../../data/types.ts";
import {useDatasetProfile, useDatasets, useDatasetUsage, useDeleteDataset} from "../../data/hooks.ts";
import {Icon} from "../../../components/Icon.tsx";
import {ActionIcon, Alert, Button, Menu, Modal} from "@mantine/core";
import {Link, useParams} from "react-router-dom";
import {useState} from "react";
import {useDashboards, useDeleteDashboard} from "../../dashboards/hooks.ts";
import {EmptyState, ErrorState, LoadingState, PageHeading} from "../../../components/Feedback.tsx";
import {ImportDataDialog} from "../../data/components/ImportDataDialog.tsx";

function formatDate(value: string) {
    return new Date(value).toLocaleDateString("en-GB", {
        day: "numeric",
        month: "short",
        year: "numeric",
    });
}

function DatasetQualityItem({
    projectId,
    dataset,
}: {
  projectId: string;
  dataset: Dataset;
}) {
    const profile = useDatasetProfile(
        projectId,
        dataset.id,
        Boolean(dataset.currentVersion),
    );

    if (!dataset.currentVersion) {
        return (
            <div className="quality-dataset-row is-pending">
        <span className="quality-dataset-icon">
          <Icon name="data" size={18} />
        </span>

                <div>
                    <strong>{dataset.name}</strong>
                    <small>Waiting for a ready version before quality can be checked.</small>
                </div>

                <span className="pill pill-neutral">Pending</span>
            </div>
        );
    }

    if (profile.isPending) {
        return (
            <div className="quality-dataset-row">
        <span className="quality-dataset-icon">
          <Icon name="data" size={18} />
        </span>

                <div>
                    <strong>{dataset.name}</strong>
                    <small>Checking data quality…</small>
                </div>
            </div>
        );
    }

    if (profile.error || !profile.data) {
        return (
            <div className="quality-dataset-row has-issues">
        <span className="quality-dataset-icon">
          <Icon name="warning" size={18} />
        </span>

                <div>
                    <strong>{dataset.name}</strong>
                    <small>Quality profile could not be loaded.</small>
                </div>

                <Button
                    variant="subtle"
                    size="compact-xs"
                    onClick={() => void profile.refetch()}
                >
                    Retry
                </Button>
            </div>
        );
    }

    const missing = profile.data.columns.reduce(
        (total, column) => total + column.nullCount,
        0,
    );

    return (
        <Link
            className={`quality-dataset-row ${missing ? "has-issues" : ""}`}
            to={`/projects/${projectId}/data/${dataset.id}`}
        >
      <span className="quality-dataset-icon">
        <Icon name={missing ? "warning" : "check"} size={18} />
      </span>

            <div>
                <strong>{dataset.name}</strong>
                <small>
                    {missing
                        ? `${missing.toLocaleString()} missing ${
                            missing === 1 ? "value" : "values"
                        } detected`
                        : "No missing values detected"}
                </small>
            </div>

            <span className={`pill ${missing ? "pill-neutral" : ""}`}>
        {missing ? "Needs attention" : "Checked"}
      </span>

            <Icon name="chevron" size={15} />
        </Link>
    );
}

export default function ProjectOverviewPage() {
    const { projectId = "" } = useParams();

    const [importOpen, setImportOpen] = useState(false);
    const [deletingDataset, setDeletingDataset] = useState<Dataset | null>(null);
    const [deletingDashboard, setDeletingDashboard] =
        useState<Dashboard | null>(null);

    const datasets = useDatasets(projectId);
    const dashboards = useDashboards(projectId);
    const datasetUsage = useDatasetUsage(projectId, deletingDataset?.id);
    const removeDataset = useDeleteDataset(projectId);
    const removeDashboard = useDeleteDashboard(projectId);

    const ready =
        datasets.data?.filter((dataset) => dataset.currentVersion) ?? [];

    const rowCount = ready.reduce(
        (total, dataset) => total + (dataset.currentVersion?.rowCount ?? 0),
        0,
    );

    const dataPath = `/projects/${projectId}/data`;

    const isEmptyProject =
        !datasets.isPending &&
        !dashboards.isPending &&
        (datasets.data?.length ?? 0) === 0 &&
        (dashboards.data?.length ?? 0) === 0;

    return (
        <div className="page-content overview-page">
            <PageHeading
                title="Overview"
                description="Your project's data, analysis, and dashboards in one place."
                actions={
                    <Button
                        leftSection={<Icon name="plus" size={16} />}
                        onClick={() => setImportOpen(true)}
                    >
                        Add data
                    </Button>
                }
            />

            <div className="overview-metrics">
                <div>
          <span>
            <Icon name="data" size={17} /> Datasets
          </span>
                    <strong>{datasets.data?.length ?? "—"}</strong>
                    <small>{ready.length} ready to work with</small>
                </div>

                <div>
          <span>
            <Icon name="list" size={17} /> Rows
          </span>
                    <strong>{rowCount.toLocaleString()}</strong>
                    <small>Across ready datasets</small>
                </div>

                <div>
          <span>
            <Icon name="dashboard" size={17} /> Dashboards
          </span>
                    <strong>{dashboards.data?.length ?? "—"}</strong>
                    <small>Created in this project</small>
                </div>
            </div>

            {isEmptyProject && (
                <>
                    <div className="section-heading overview-section-heading">
                        <h2>Where would you like to start?</h2>
                        <span className="muted">One step leads to the next.</span>
                    </div>

                    <div className="journey-cards">
                        <button onClick={() => setImportOpen(true)}>
                            <span className="journey-step">01</span>
                            <span className="journey-icon">
                <Icon name="upload" size={22} />
              </span>
                            <h3>Bring in your data</h3>
                            <p>
                                Add a data source and we’ll help you inspect what’s inside.
                            </p>
                            <span className="text-link">
                Add data <Icon name="arrow" size={16} />
              </span>
                        </button>

                        <Link to={dataPath}>
                            <span className="journey-step">02</span>
                            <span className="journey-icon">
                <Icon name="chart" size={22} />
              </span>
                            <h3>Prepare and understand it</h3>
                            <p>
                                Review columns, profile quality, and prepare your data for
                                analysis.
                            </p>
                            <span className="text-link">
                Open data workspace <Icon name="arrow" size={16} />
              </span>
                        </Link>

                        <Link to={`/projects/${projectId}/dashboards`}>
                            <span className="journey-step">03</span>
                            <span className="journey-icon">
                <Icon name="dashboard" size={22} />
              </span>
                            <h3>Build the bigger picture</h3>
                            <p>
                                Turn your analysis into dashboards you can return to and share.
                            </p>
                            <span className="text-link">
                Open dashboards <Icon name="arrow" size={16} />
              </span>
                        </Link>
                    </div>
                </>
            )}

            {!!datasets.data?.length && (
                <section className="quality-panel">
                    <div className="panel-heading quality-panel-heading">
                        <div>
                            <h2>Data quality</h2>
                            <p>
                                Review detected issues dataset by dataset before you build on
                                top of them.
                            </p>
                        </div>

                        <Link to={dataPath} className="text-link">
                            View all data <Icon name="arrow" size={15} />
                        </Link>
                    </div>

                    <div className="quality-dataset-list">
                        {datasets.data.map((dataset) => (
                            <DatasetQualityItem
                                key={dataset.id}
                                projectId={projectId}
                                dataset={dataset}
                            />
                        ))}
                    </div>
                </section>
            )}

            {!isEmptyProject && (
                <div className="overview-bottom-grid">
                    <section className="panel">
                        <div className="panel-heading">
                            <h2>Your data</h2>
                            <Link to={dataPath} className="text-link">
                                View all <Icon name="arrow" size={15} />
                            </Link>
                        </div>

                        {datasets.isPending ? (
                            <LoadingState />
                        ) : datasets.error ? (
                            <ErrorState
                                error={datasets.error}
                                retry={() => void datasets.refetch()}
                            />
                        ) : !datasets.data?.length ? (
                            <EmptyState
                                icon="data"
                                title="No datasets yet"
                                description="Add a data source to start preparing and exploring your data."
                                action={
                                    <Button variant="default" onClick={() => setImportOpen(true)}>
                                        Add data
                                    </Button>
                                }
                            />
                        ) : (
                            datasets.data.slice(0, 4).map((dataset) => (
                                <div className="resource-row" key={dataset.id}>
                                    <Link
                                        to={`${dataPath}/${dataset.id}`}
                                        className="resource-row-main"
                                    >
                    <span className="resource-icon">
                      <Icon name="data" size={19} />
                    </span>

                                        <div>
                                            <strong>{dataset.name}</strong>
                                            <small>
                                                {dataset.currentVersion
                                                    ? `${dataset.currentVersion.rowCount.toLocaleString()} rows · ${dataset.currentVersion.columnCount} columns`
                                                    : "Waiting for a ready version"}
                                            </small>
                                        </div>

                                        <span
                                            className={`pill ${
                                                !dataset.currentVersion ? "pill-neutral" : ""
                                            }`}
                                        >
                      {dataset.currentVersion ? "Ready" : "Pending"}
                    </span>
                                    </Link>

                                    <Menu position="bottom-end" shadow="md">
                                        <Menu.Target>
                                            <ActionIcon
                                                variant="subtle"
                                                color="gray"
                                                aria-label={`Options for ${dataset.name}`}
                                            >
                                                <Icon name="more" />
                                            </ActionIcon>
                                        </Menu.Target>

                                        <Menu.Dropdown>
                                            <Menu.Item
                                                component={Link}
                                                to={`${dataPath}/${dataset.id}`}
                                                leftSection={<Icon name="data" size={15} />}
                                            >
                                                Open dataset
                                            </Menu.Item>

                                            <Menu.Item
                                                component={Link}
                                                to={`/projects/${projectId}/explore?dataset=${dataset.id}`}
                                                disabled={!dataset.currentVersion}
                                                leftSection={<Icon name="chart" size={15} />}
                                            >
                                                Explore dataset
                                            </Menu.Item>

                                            <Menu.Divider />

                                            <Menu.Item
                                                color="red"
                                                leftSection={<Icon name="trash" size={15} />}
                                                onClick={() => {
                                                    removeDataset.reset();
                                                    setDeletingDataset(dataset);
                                                }}
                                            >
                                                Delete dataset
                                            </Menu.Item>
                                        </Menu.Dropdown>
                                    </Menu>
                                </div>
                            ))
                        )}
                    </section>

                    <section className="panel">
                        <div className="panel-heading">
                            <h2>Your dashboards</h2>
                            <Link
                                to={`/projects/${projectId}/dashboards`}
                                className="text-link"
                            >
                                View all <Icon name="arrow" size={15} />
                            </Link>
                        </div>

                        {dashboards.isPending ? (
                            <LoadingState />
                        ) : dashboards.error ? (
                            <ErrorState
                                error={dashboards.error}
                                retry={() => void dashboards.refetch()}
                            />
                        ) : !dashboards.data?.length ? (
                            <EmptyState
                                icon="dashboard"
                                title="No dashboards yet"
                                description="Create a dashboard when you are ready to bring your analysis together."
                                action={
                                    <Button
                                        component={Link}
                                        to={`/projects/${projectId}/dashboards`}
                                        variant="default"
                                    >
                                        Create a dashboard
                                    </Button>
                                }
                            />
                        ) : (
                            dashboards.data.slice(0, 4).map((dashboard) => (
                                <div className="resource-row" key={dashboard.id}>
                                    <Link
                                        to={`/projects/${projectId}/dashboards/${dashboard.id}/edit`}
                                        className="resource-row-main"
                                    >
                    <span className="resource-icon">
                      <Icon name="dashboard" size={19} />
                    </span>

                                        <div>
                                            <strong>{dashboard.name}</strong>
                                            <small>Created {formatDate(dashboard.createdAtUtc)}</small>
                                        </div>
                                    </Link>

                                    <Menu position="bottom-end" shadow="md">
                                        <Menu.Target>
                                            <ActionIcon
                                                variant="subtle"
                                                color="gray"
                                                aria-label={`Options for ${dashboard.name}`}
                                            >
                                                <Icon name="more" />
                                            </ActionIcon>
                                        </Menu.Target>

                                        <Menu.Dropdown>
                                            <Menu.Item
                                                component={Link}
                                                to={`/projects/${projectId}/dashboards/${dashboard.id}/edit`}
                                                leftSection={<Icon name="edit" size={15} />}
                                            >
                                                Edit dashboard
                                            </Menu.Item>

                                            <Menu.Divider />

                                            <Menu.Item
                                                color="red"
                                                leftSection={<Icon name="trash" size={15} />}
                                                onClick={() => {
                                                    removeDashboard.reset();
                                                    setDeletingDashboard(dashboard);
                                                }}
                                            >
                                                Delete dashboard
                                            </Menu.Item>
                                        </Menu.Dropdown>
                                    </Menu>
                                </div>
                            ))
                        )}
                    </section>
                </div>
            )}

            <ImportDataDialog
                projectId={projectId}
                opened={importOpen}
                onClose={() => setImportOpen(false)}
            />

            <Modal
                opened={!!deletingDataset}
                onClose={() => {
                    if (!removeDataset.isPending) setDeletingDataset(null);
                }}
                title="Delete this dataset?"
                centered
                closeOnClickOutside={!removeDataset.isPending}
                closeOnEscape={!removeDataset.isPending}
                withCloseButton={!removeDataset.isPending}
            >
                <p className="overview-dialog-description">
                    “{deletingDataset?.name}” and its data will be permanently removed.
                    This cannot be undone.
                </p>

                {datasetUsage.isPending && (
                    <p className="overview-dialog-description" role="status">
                        Checking whether dashboards use this dataset…
                    </p>
                )}

                {datasetUsage.isError && (
                    <ErrorState
                        error={datasetUsage.error}
                        retry={() => void datasetUsage.refetch()}
                    />
                )}

                {!!datasetUsage.data?.length && (
                    <Alert
                        color="orange"
                        title="This dataset is still in use"
                        icon={<Icon name="warning" />}
                    >
                        Remove its charts from these dashboards before deleting the
                        dataset:
                        <ul className="overview-usage-list">
                            {datasetUsage.data.map(({ dashboard, count }) => (
                                <li key={dashboard.id}>
                                    <Link
                                        to={`/projects/${projectId}/dashboards/${dashboard.id}/edit`}
                                    >
                                        {dashboard.name}
                                    </Link>{" "}
                                    · {count} {count === 1 ? "chart" : "charts"}
                                </li>
                            ))}
                        </ul>
                    </Alert>
                )}

                {removeDataset.error && <ErrorState error={removeDataset.error} />}

                <div className="dialog-actions">
                    <Button
                        variant="default"
                        disabled={removeDataset.isPending}
                        onClick={() => setDeletingDataset(null)}
                    >
                        Keep dataset
                    </Button>

                    <Button
                        color="red"
                        loading={removeDataset.isPending}
                        disabled={
                            datasetUsage.isPending ||
                            datasetUsage.isError ||
                            datasetUsage.isFetching ||
                            !!datasetUsage.data?.length
                        }
                        onClick={() => {
                            if (deletingDataset) {
                                removeDataset.mutate(deletingDataset.id, {
                                    onSuccess: () => setDeletingDataset(null),
                                });
                            }
                        }}
                    >
                        Delete dataset
                    </Button>
                </div>
            </Modal>

            <Modal
                opened={!!deletingDashboard}
                onClose={() => {
                    if (!removeDashboard.isPending) setDeletingDashboard(null);
                }}
                title="Delete this dashboard?"
                centered
                closeOnClickOutside={!removeDashboard.isPending}
                closeOnEscape={!removeDashboard.isPending}
                withCloseButton={!removeDashboard.isPending}
            >
                <p className="overview-dialog-description">
                    “{deletingDashboard?.name}” will be permanently removed. This action
                    cannot be undone.
                </p>

                {removeDashboard.error && <ErrorState error={removeDashboard.error} />}

                <div className="dialog-actions">
                    <Button
                        variant="default"
                        disabled={removeDashboard.isPending}
                        onClick={() => setDeletingDashboard(null)}
                    >
                        Keep dashboard
                    </Button>

                    <Button
                        color="red"
                        loading={removeDashboard.isPending}
                        onClick={() => {
                            if (deletingDashboard) {
                                removeDashboard.mutate(deletingDashboard.id, {
                                    onSuccess: () => setDeletingDashboard(null),
                                });
                            }
                        }}
                    >
                        Delete dashboard
                    </Button>
                </div>
            </Modal>
        </div>
    );
}
