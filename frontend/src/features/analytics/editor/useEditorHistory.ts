import { useCallback, useState } from "react";

import type { EditorSnapshot } from "./editorState";
import {
  createEditorHistory,
  pushEditorChange,
  redoEditorChange,
  undoEditorChange,
} from "./editorHistory";

export function useEditorHistory(initialSnapshot: EditorSnapshot) {
  const [history, setHistory] = useState(() =>
    createEditorHistory(initialSnapshot),
  );

  const apply = useCallback(
    (
      change: (snapshot: EditorSnapshot) => EditorSnapshot,
      groupId?: string,
    ) => {
      setHistory((current) =>
        pushEditorChange(current, change(current.present), groupId),
      );
    },
    [],
  );

  const undo = useCallback(() => {
    setHistory((current) => undoEditorChange(current));
  }, []);

  const redo = useCallback(() => {
    setHistory((current) => redoEditorChange(current));
  }, []);

  return {
    snapshot: history.present,
    canUndo: history.past.length > 0,
    canRedo: history.future.length > 0,
    apply,
    undo,
    redo,
  };
}
