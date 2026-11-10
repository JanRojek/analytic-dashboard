import type { EditorSnapshot } from "./editorState";

const MAX_HISTORY_LENGTH = 50;

export interface EditorHistory {
  past: EditorSnapshot[];
  present: EditorSnapshot;
  future: EditorSnapshot[];
  lastChangeGroup: string | null;
}

export function createEditorHistory(initial: EditorSnapshot): EditorHistory {
  return {
    past: [],
    present: initial,
    future: [],
    lastChangeGroup: null,
  };
}

export function pushEditorChange(
  history: EditorHistory,
  next: EditorSnapshot,
  groupId?: string,
): EditorHistory {
  if (next === history.present) {
    return history;
  }

  const continuesGroup =
    Boolean(groupId) &&
    groupId === history.lastChangeGroup &&
    history.past.length > 0 &&
    history.future.length === 0;

  if (continuesGroup) {
    return {
      ...history,
      present: next,
    };
  }

  return {
    past: [...history.past, history.present].slice(-MAX_HISTORY_LENGTH),
    present: next,
    future: [],
    lastChangeGroup: groupId ?? null,
  };
}

export function undoEditorChange(history: EditorHistory): EditorHistory {
  if (history.past.length === 0) {
    return history;
  }

  const previous = history.past[history.past.length - 1];

  return {
    past: history.past.slice(0, -1),
    present: previous,
    future: [history.present, ...history.future],
    lastChangeGroup: null,
  };
}

export function redoEditorChange(history: EditorHistory): EditorHistory {
  if (history.future.length === 0) {
    return history;
  }

  const [next, ...remaining] = history.future;

  return {
    past: [...history.past, history.present].slice(-MAX_HISTORY_LENGTH),
    present: next,
    future: remaining,
    lastChangeGroup: null,
  };
}
