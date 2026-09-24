import { useRef, useState } from "react";
import { useImportDataset } from "../features/data/hooks";
import { Alert, Button, Modal, TextInput } from "@mantine/core";
import { useNavigate } from "react-router-dom";
import { useSession } from "../data/session";
import { createSampleFile, parseCsv } from "../mocks/csv";
import { Icon } from "./Icon";
import "../styles/data.css";

export function ImportDataDialog({
  projectId,
  opened,
  onClose,
}: {
  projectId: string;
  opened: boolean;
  onClose: () => void;
}) {
  const { mode } = useSession();
  const navigate = useNavigate();
  const input = useRef<HTMLInputElement>(null);
  const selection = useRef(0);
  const [file, setFile] = useState<File | null>(null);
  const [name, setName] = useState("");
  const [preview, setPreview] = useState<{
    headers: string[];
    rows: string[][];
    rowCount: number;
  } | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [reading, setReading] = useState(false);
  const [dragging, setDragging] = useState(false);
  const mutation = useImportDataset(projectId);

  function importDataset() {
    if (!file || !name.trim()) return;

    const upload = new File(
        [file],
        `${name.trim().replace(/[\\/:*?"<>|]/g, "-")}.csv`,
        { type: "text/csv" },
    );

    mutation.mutate(upload, {
      onSuccess: (result) => {
        onClose();
        setFile(null);
        setPreview(null);
        navigate(`/projects/${projectId}/data/${result.datasetId}`);
      },
    });
  }

  async function chooseFile(candidate?: File) {
    if (!candidate) return;
    const currentSelection = ++selection.current;
    setError(null);
    mutation.reset();
    setReading(true);
    try {
      if (!candidate.name.toLowerCase().endsWith(".csv"))
        throw new Error(
          "Please choose a CSV file. You can export one from Excel or Google Sheets.",
        );
      if (!candidate.size)
        throw new Error(
          "This file is empty. Choose a file with a header and at least one data row.",
        );
      if (candidate.size > 10 * 1024 * 1024)
        throw new Error(
          "For this preview, choose a CSV file smaller than 10 MB.",
        );
      let parsed;
      try {
        parsed = parseCsv(await candidate.text());
      } catch (parseError) {
        if (
          parseError instanceof Error &&
          parseError.message.includes("20,000 rows")
        )
          throw new Error(
            "This import preview supports up to 20,000 rows. Choose a smaller CSV for this prototype.",
            { cause: parseError },
          );
        throw parseError;
      }
      if (currentSelection !== selection.current) return;
      setFile(candidate);
      setName(candidate.name.replace(/\.csv$/i, "").slice(0, 180));
      setPreview({
        headers: parsed.columns,
        rows: parsed.rows
          .slice(0, 4)
          .map((row) => parsed.columns.map((column) => row[column] ?? "")),
        rowCount: parsed.rows.length,
      });
    } catch (caught) {
      if (currentSelection !== selection.current) return;
      setFile(null);
      setPreview(null);
      setError(
        caught instanceof Error
          ? caught.message
          : "This file could not be read.",
      );
    } finally {
      if (currentSelection === selection.current) setReading(false);
    }
  }

  function close() {
    if (mutation.isPending) return;
    selection.current += 1;
    setFile(null);
    setPreview(null);
    setError(null);
    setReading(false);
    mutation.reset();
    onClose();
  }

  return (
    <Modal
      opened={opened}
      onClose={close}
      title="Add data to your project"
      size="lg"
      centered
      closeOnClickOutside={!mutation.isPending}
      closeOnEscape={!mutation.isPending}
      withCloseButton={!mutation.isPending}
    >
      <div className="import-dialog">
        <div className="import-steps" aria-label="Import progress">
          <span className={!file ? "active" : "complete"}>
            <b>{file ? <Icon name="check" size={12} /> : "1"}</b> Choose a file
          </span>
          <i />
          <span className={file ? "active" : ""}>
            <b>2</b> Review & import
          </span>
        </div>
        <p className="data-muted">
          {file
            ? "A quick look before your data becomes part of the project."
            : "Start with a CSV. We’ll identify your columns and prepare a profile automatically."}
        </p>
        <input
          ref={input}
          type="file"
          accept=".csv,text/csv"
          className="sr-only"
          tabIndex={-1}
          aria-label="Choose a CSV file"
          onChange={(event) => {
            void chooseFile(event.target.files?.[0]);
            event.target.value = "";
          }}
        />
        {!file ? (
          <>
            <button
              type="button"
              className={`import-dropzone ${dragging ? "is-dragging" : ""}`}
              disabled={reading}
              onClick={() => input.current?.click()}
              onDragOver={(event) => {
                event.preventDefault();
                setDragging(true);
              }}
              onDragLeave={() => setDragging(false)}
              onDrop={(event) => {
                event.preventDefault();
                setDragging(false);
                void chooseFile(event.dataTransfer.files[0]);
              }}
            >
              <span className="import-file-icon">
                <Icon name="upload" size={24} />
              </span>
              <strong>
                {reading ? "Reading your file…" : "Drop your CSV here"}
              </strong>
              <span>
                or <u>browse files</u>
              </span>
              <small>CSV with a header row · up to 10 MB / 20,000 rows</small>
            </button>
            <button
              type="button"
              className="sample-data-button"
              disabled={reading}
              onClick={() => void chooseFile(createSampleFile())}
            >
              <span className="sample-data-icon">
                <Icon name="data" size={20} />
              </span>
              <span>
                <strong>Try it with sample data</strong>
                <small>A small commerce dataset, ready to explore.</small>
              </span>
              <Icon name="arrow" />
            </button>
          </>
        ) : (
          <>
            <div className="import-selected-file">
              <span className="data-file-symbol">
                <Icon name="data" size={22} />
              </span>
              <div>
                <strong>{file.name}</strong>
                <small>
                  {(file.size / 1024).toFixed(1)} KB ·{" "}
                  {preview?.rowCount.toLocaleString()} rows ·{" "}
                  {preview?.headers.length} columns
                </small>
              </div>
              <Button
                variant="subtle"
                size="xs"
                loading={reading}
                disabled={mutation.isPending}
                onClick={() => input.current?.click()}
              >
                Change
              </Button>
            </div>
            <TextInput
              label="Dataset name"
              description="A descriptive name makes it easier to find when building a chart."
              value={name}
              maxLength={180}
              onChange={(event) => setName(event.currentTarget.value)}
              disabled={mutation.isPending}
              required
            />
            {preview && (
              <div className="import-preview">
                <div className="data-section-label">
                  FILE PREVIEW <span>First {preview.rows.length} rows</span>
                </div>
                <div
                  className="data-table-scroll"
                  tabIndex={0}
                  aria-label="CSV file preview"
                >
                  <table className="data-table compact">
                    <thead>
                      <tr>
                        {preview.headers.map((header) => (
                          <th key={header}>{header}</th>
                        ))}
                      </tr>
                    </thead>
                    <tbody>
                      {preview.rows.map((row, index) => (
                        <tr key={index}>
                          {row.map((value, column) => (
                            <td key={column}>
                              {value || (
                                <span className="null-value">empty</span>
                              )}
                            </td>
                          ))}
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              </div>
            )}
          </>
        )}
        {(error || mutation.error) && (
          <Alert color="red" icon={<Icon name="warning" />} role="alert">
            {error || mutation.error?.message}
          </Alert>
        )}
        <div className="import-boundary">
          <Icon name={mode === "demo" ? "eye" : "lock"} size={15} />
          <span>
            {mode === "demo"
              ? "Demo workspace: your file is processed and saved in this browser."
              : "Your file will be uploaded to your private project."}
          </span>
        </div>
        <div className="dialog-footer">
          <Button
            variant="default"
            onClick={close}
            disabled={mutation.isPending}
          >
            Cancel
          </Button>
          <Button
            rightSection={<Icon name="arrow" size={16} />}
            disabled={!file || !name.trim() || reading}
            loading={mutation.isPending}
            onClick={importDataset}
          >
            Import dataset
          </Button>
        </div>
      </div>
    </Modal>
  );
}
