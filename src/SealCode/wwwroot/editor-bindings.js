import { isPasteShortcut, isUndoRedoShortcut, isUndoShortcut, isCopyShortcut } from './keyboard-shortcuts.js';
import { HubMethods } from './hub-contract.js';

/** Wires editor gestures and the capture-phase copy guard.
 * @param {{editor: import('monaco-editor').editor.IStandaloneCodeEditor,
 * model: import('monaco-editor').editor.ITextModel, monaco: typeof import('monaco-editor'),
 * yjs: import('./yjs-session.js').YjsSession, presence: import('./presence-ui.js').PresenceView,
 * connection: import('@microsoft/signalr').HubConnection, connectedState: string,
 * document: Document, editorHost: HTMLElement, roomId: string,
 * getDisplayName: () => string, getMaxSnapshotCharacters: () => number,
 * getCheckpointBlockedAtLength: () => number | null, clearCheckpointBlock: () => void,
 * getLastSelection: () => boolean | undefined, setLastSelection: (value: boolean | undefined) => void,
 * showSyncError: (kind: string, message: string) => void, clearSyncError: (kind: string) => void,
 * scheduleCursorSend: () => void, hasNonEmptyEditorSelection: () => boolean,
 * undo: () => void, redo: () => void, NodeType?: typeof Node,
 * setTimer?: (callback: () => void, delay: number) => unknown}} options
 */
export function installEditorBindings({ editor, model, monaco, yjs, presence,
  connection, connectedState, document, editorHost, roomId, getDisplayName,
  getMaxSnapshotCharacters, getCheckpointBlockedAtLength, clearCheckpointBlock,
  getLastSelection, setLastSelection, showSyncError, clearSyncError,
  scheduleCursorSend, hasNonEmptyEditorSelection, undo, redo,
  NodeType = Node, setTimer = setTimeout }) {
  let textSizeExceeded = false;
  editor.onDidChangeModelContent((event) => {
    const exceedsTextLimit = model.getValueLength() > getMaxSnapshotCharacters();
    if (exceedsTextLimit !== textSizeExceeded) {
      textSizeExceeded = exceedsTextLimit;
      if (exceedsTextLimit)
        showSyncError('text-size',
          `The document exceeds the ${getMaxSnapshotCharacters()}-character snapshot limit. Reduce its size or download local text before leaving.`);
      else
        clearSyncError('text-size');
    }
    if (yjs.isApplyingRemoteUpdate) return;
    if (getCheckpointBlockedAtLength() !== null &&
        model.getValueLength() < getCheckpointBlockedAtLength() &&
        model.getValueLength() <= getMaxSnapshotCharacters()) {
      clearCheckpointBlock();
    }
    presence.hideRemoteCaretsTemporarily();
    yjs.applyRemoteCursorAdjustment(event.changes);
    presence.markActiveEditor(getDisplayName());
    scheduleCursorSend();
  });

  editor.onKeyDown((keyboardEvent) => {
    const browserEvent = keyboardEvent.browserEvent;
    if (isPasteShortcut(browserEvent)) {
      yjs.undoHistory?.stopCapturing();
      setTimer(() => yjs.undoHistory?.stopCapturing(), 0);
    }
    if (isUndoRedoShortcut(browserEvent)) {
      browserEvent.preventDefault();
      browserEvent.stopImmediatePropagation();
      keyboardEvent.preventDefault();
      keyboardEvent.stopPropagation();
      if (isUndoShortcut(browserEvent)) {
        undo();
      } else {
        redo();
      }
      return;
    }
    if (!isCopyShortcut(browserEvent)) return;
    if (hasNonEmptyEditorSelection()) return;
    browserEvent.preventDefault();
    browserEvent.stopImmediatePropagation();
    keyboardEvent.preventDefault();
    keyboardEvent.stopPropagation();
  });

  editor.onDidPaste?.(() => yjs.undoHistory?.stopCapturing());

  editor.onDidChangeCursorSelection((event) => {
    if (event.reason === monaco.editor.CursorChangeReason.Explicit) {
      yjs.undoHistory?.stopCapturing();
    }
    if (!getDisplayName() || connection.state !== connectedState) {
      return;
    }
    scheduleCursorSend();
    const sel = editor.getSelection();
    if (!sel) return;
    const isMultiLine = sel.startLineNumber !== sel.endLineNumber;
    presence.markSelecting(getDisplayName(), isMultiLine);
    if (getLastSelection() === isMultiLine) return;
    setLastSelection(isMultiLine);
    connection.invoke(HubMethods.UpdateSelection, roomId, isMultiLine).catch((err) => {
      setLastSelection(undefined);
      console.warn('Failed to send selection update.', err);
    });
  });

document.addEventListener(
  'copy',
  (event) => {
    if (!editor) return;
    const target = event.target;
    const isEditorCopy =
      editor.hasTextFocus() || (target instanceof NodeType && editorHost.contains(target));
    if (!isEditorCopy) return;
    if (!hasNonEmptyEditorSelection()) {
      event.preventDefault();
      event.stopImmediatePropagation();
      return;
    }
    if (!getDisplayName()) return;
    if (connection.state !== connectedState) return;
    presence.markCopying(getDisplayName());
    connection.invoke(HubMethods.UpdateCopy, roomId).catch(() => {});
  },
  true
);

}
