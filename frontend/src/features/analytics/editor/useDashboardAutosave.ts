import { useCallback, useEffect, useRef, useState } from "react";

import { ApiError } from "../../../data/api";
import { useUpdateDashboardWidgets } from "../../dashboards/hooks";
import { toDashboardWidgetUpdates } from "./editorSnapshot";
import {
  clearEditorSnapshotDraft,
  persistEditorSnapshotDraft,
  readEditorSnapshotDraft,
} from "./editorSnapshotDraft";
import type { EditorSnapshot } from "./editorState";

const AUTOSAVE_DELAY_MS = 800;

export type DashboardAutosaveStatus =
    | "saved"
    | "pending"
    | "saving"
    | "invalid"
    | "error"
    | "conflict";

type DashboardAutosaveOptions = {
  enabled: boolean;
  suspend: boolean;
  projectId: string;
  dashboardId: string;
  draftKey: string;
  snapshot: EditorSnapshot;
  baseRevision: number;
  serverRevision: number;
  hasRecoveredDraft: boolean;
};

const CONFLICT_MESSAGE =
    "This dashboard changed on the server. Your local draft is safe, but automatic saving has stopped.";

export function useDashboardAutosave({
  enabled,
  suspend,
  projectId,
  dashboardId,
  draftKey,
  snapshot,
  baseRevision,
  serverRevision,
  hasRecoveredDraft,
}: DashboardAutosaveOptions) {
  const mutation = useUpdateDashboardWidgets(projectId);
  const mutateRef = useRef(mutation.mutateAsync);
  mutateRef.current = mutation.mutateAsync;

  const initialConflict =
      enabled && hasRecoveredDraft && baseRevision !== serverRevision;

  const [status, setStatus] = useState<DashboardAutosaveStatus>(() =>
      initialConflict ? "conflict" : hasRecoveredDraft ? "pending" : "saved",
  );
  const [error, setError] = useState<string | null>(() =>
      initialConflict ? CONFLICT_MESSAGE : null,
  );

  const mountedRef = useRef(false);
  const enabledRef = useRef(enabled);
  enabledRef.current = enabled;
  const suspendedRef = useRef(suspend);
  suspendedRef.current = suspend;

  const latestSnapshotRef = useRef(snapshot);
  latestSnapshotRef.current = snapshot;
  const observedSnapshotRef = useRef(snapshot);
  const acknowledgedSnapshotRef = useRef<EditorSnapshot | null>(
      hasRecoveredDraft ? null : snapshot,
  );
  const revisionRef = useRef(baseRevision);
  const pendingRef = useRef(hasRecoveredDraft);
  const blockedRef = useRef(initialConflict);
  const savingRef = useRef(false);
  const firstEffectRef = useRef(true);
  const lastEditAtRef = useRef(Date.now());
  const timerRef = useRef<ReturnType<typeof setTimeout> | null>(null);
  const executeSaveRef = useRef<() => Promise<void>>(async () => {});

  const schedule = useCallback((delayMs: number) => {
    if (timerRef.current !== null) {
      clearTimeout(timerRef.current);
      timerRef.current = null;
    }

    if (
        !enabledRef.current ||
        suspendedRef.current ||
        blockedRef.current ||
        !pendingRef.current
    ) {
      return;
    }

    timerRef.current = setTimeout(() => {
      timerRef.current = null;
      void executeSaveRef.current();
    }, delayMs);
  }, []);

  const executeSave = useCallback(async () => {
    if (
        !enabledRef.current ||
        suspendedRef.current ||
        blockedRef.current ||
        savingRef.current ||
        !pendingRef.current
    ) {
      return;
    }

    const submittedSnapshot = latestSnapshotRef.current;
    const revisionAtSubmission = revisionRef.current;
    const updates = toDashboardWidgetUpdates(submittedSnapshot);

    if (
        updates.some(
            (widget) =>
                !widget.title.trim() ||
                !widget.groupByColumn ||
                !widget.measureColumn,
        )
    ) {
      setStatus("invalid");
      setError("Complete every chart's title, group and measure to save.");
      return;
    }

    savingRef.current = true;
    setStatus("saving");
    setError(null);

    try {
      const result = await mutateRef.current({
        dashboardId,
        updates,
        expectedRevision: revisionAtSubmission,
      });

      revisionRef.current = result.revision;
      acknowledgedSnapshotRef.current = submittedSnapshot;

      const latestSnapshot = latestSnapshotRef.current;
      const hasNewerChanges = latestSnapshot !== submittedSnapshot;
      pendingRef.current = hasNewerChanges;

      if (!mountedRef.current) {
        try {
          const stored = readEditorSnapshotDraft(draftKey);
          const belongsToThisSession =
              stored?.baseRevision === revisionAtSubmission &&
              JSON.stringify(stored.snapshot) === JSON.stringify(latestSnapshot);

          if (belongsToThisSession) {
            if (hasNewerChanges) {
              persistEditorSnapshotDraft(draftKey, latestSnapshot, result.revision);
            } else {
              clearEditorSnapshotDraft(draftKey);
            }
          }
        } catch {}

        return;
      }

      try {
        if (hasNewerChanges) {
          persistEditorSnapshotDraft(draftKey, latestSnapshot, result.revision);
        } else {
          clearEditorSnapshotDraft(draftKey);
        }
      } catch {
        setStatus("error");
        setError(
            "The server saved your changes, but local recovery could not be updated.",
        );
        return;
      }

      setStatus(hasNewerChanges ? "pending" : "saved");
      setError(null);

      if (hasNewerChanges) {
        const remainingDelay = Math.max(
            0,
            AUTOSAVE_DELAY_MS - (Date.now() - lastEditAtRef.current),
        );
        schedule(remainingDelay);
      }
    } catch (cause) {
      if (!mountedRef.current) return;

      const conflict = cause instanceof ApiError && cause.status === 409;
      blockedRef.current = conflict;
      setStatus(conflict ? "conflict" : "error");
      setError(
          conflict
              ? CONFLICT_MESSAGE
              : cause instanceof Error
                  ? cause.message
                  : "The dashboard could not be saved.",
      );
    } finally {
      savingRef.current = false;
    }
  }, [dashboardId, draftKey, schedule]);

  executeSaveRef.current = executeSave;

  useEffect(() => {
    mountedRef.current = true;

    if (enabledRef.current && pendingRef.current && !blockedRef.current) {
      schedule(AUTOSAVE_DELAY_MS);
    }

    return () => {
      mountedRef.current = false;
      if (timerRef.current !== null) {
        clearTimeout(timerRef.current);
        timerRef.current = null;
      }
    };
  }, [schedule]);

  useEffect(() => {
    if (!enabled) return;

    if (suspend) {
      if (timerRef.current !== null) {
        clearTimeout(timerRef.current);
        timerRef.current = null;
      }
      return;
    }

    if (pendingRef.current && !blockedRef.current) {
      const remainingDelay = Math.max(
          0,
          AUTOSAVE_DELAY_MS - (Date.now() - lastEditAtRef.current),
      );
      schedule(remainingDelay);
    }
  }, [enabled, suspend, schedule]);

  useEffect(() => {
    if (!enabled) {
      schedule(0);
      return;
    }

    const firstEffect = firstEffectRef.current;
    firstEffectRef.current = false;

    if (firstEffect && !hasRecoveredDraft) return;
    if (!firstEffect && observedSnapshotRef.current === snapshot) return;

    observedSnapshotRef.current = snapshot;
    lastEditAtRef.current = Date.now();

    if (
        !savingRef.current &&
        !blockedRef.current &&
        acknowledgedSnapshotRef.current === snapshot
    ) {
      pendingRef.current = false;
      schedule(0);

      try {
        clearEditorSnapshotDraft(draftKey);
        setStatus("saved");
        setError(null);
      } catch {
        setStatus("error");
        setError("Your browser could not clear the saved recovery draft.");
      }
      return;
    }

    pendingRef.current = true;

    let recoverySaved = true;
    try {
      persistEditorSnapshotDraft(draftKey, snapshot, revisionRef.current);
    } catch {
      recoverySaved = false;
      setStatus("error");
      setError("Your browser could not preserve the latest dashboard changes.");
    }

    if (blockedRef.current) return;

    if (recoverySaved) {
      setStatus(savingRef.current ? "saving" : "pending");
      setError(null);
    }
    schedule(AUTOSAVE_DELAY_MS);
  }, [enabled, hasRecoveredDraft, snapshot, draftKey, schedule]);

  const retry = useCallback(() => {
    if (blockedRef.current) return;

    if (!pendingRef.current) {
      try {
        clearEditorSnapshotDraft(draftKey);
        setError(null);
        setStatus("saved");
      } catch {
        setError("Your browser could not clear the saved recovery draft.");
      }
      return;
    }

    setError(null);
    setStatus("pending");
    schedule(0);
  }, [draftKey, schedule]);

  return { status, error, retry };
}
