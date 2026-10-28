import { Button, Tooltip } from "@mantine/core";

import { Brand } from "../../../components/Brand";
import { Icon } from "../../../components/Icon";

type DashboardWorkspaceHeaderProps = {
    dashboardName: string;
    editing: boolean;
    isDemo: boolean;
    dirty: boolean;
    saveStatus: string;
    onBack: () => void;
    onPreview: () => void;
    onSave: () => void;
    onEdit: () => void;
};

export function DashboardWorkspaceHeader({
    dashboardName,
    editing,
    isDemo,
    dirty,
    saveStatus,
    onBack,
    onPreview,
    onSave,
    onEdit,
}: DashboardWorkspaceHeaderProps) {
    return (
        <header className="canvas-header">
            <div
                className="canvas-header-start"
                onClickCapture={(event) => {
                    if (
                        dirty &&
                        (event.target as HTMLElement).closest("a") &&
                        !window.confirm(
                            "Leave this editor? Your unsaved draft will remain in this browser.",
                        )
                    ) {
                        event.preventDefault();
                    }
                }}
            >
                <Brand />
                <span className="canvas-header-divider" />

                <button
                    type="button"
                    className="canvas-back"
                    onClick={onBack}
                >
                    <Icon name="back" size={16} />
                    <span>Dashboards</span>
                </button>
            </div>

            <div className="canvas-document-name">
                <Icon name="dashboard" size={17} />
                <strong>{dashboardName}</strong>
                <span className="canvas-mode-label">
          {editing ? "Editor" : "Presentation"}
        </span>
            </div>

            <div className="canvas-actions">
                {isDemo && <span className="canvas-demo-label">Demo</span>}

                {editing ? (
                    <>
            <span className="canvas-save-status" role="status">
              <span
                  className={
                      dirty ? "save-indicator unsaved" : "save-indicator"
                  }
              />
                {saveStatus}
            </span>

                        <Tooltip label="View your saved dashboard">
                            <Button
                                variant="default"
                                size="sm"
                                leftSection={<Icon name="eye" size={16} />}
                                onClick={onPreview}
                            >
                                Preview
                            </Button>
                        </Tooltip>

                        <Button
                            size="sm"
                            leftSection={<Icon name="check" size={16} />}
                            onClick={onSave}
                        >
                            Save changes
                        </Button>
                    </>
                ) : (
                    <Button
                        variant="default"
                        leftSection={<Icon name="edit" size={16} />}
                        onClick={onEdit}
                    >
                        Edit dashboard
                    </Button>
                )}
            </div>
        </header>
    );
}
