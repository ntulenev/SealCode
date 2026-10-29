// @ts-nocheck
// TODO: type the Monaco editor integration callbacks.
import { bindEditorTheme } from './editor-theme.js';
/** Owns Monaco setup; collaborative operations are supplied by the controller. */
export function createMonacoAdapter(monaco, host, { language, undo, redo }) {
  const model = monaco.editor.createModel('', language || 'plaintext');
  model.undo = async () => {
    undo();
  };
  model.redo = async () => {
    redo();
  };
  const editor = monaco.editor.create(host, {
    model,
    theme: host.ownerDocument.documentElement.dataset.theme === 'light' ? 'vs' : 'vs-dark',
    minimap: { enabled: false },
    automaticLayout: true,
    fixedOverflowWidgets: true,
    emptySelectionClipboard: false,
    fontFamily: 'Cascadia Code, SFMono-Regular, Consolas, Liberation Mono, monospace',
    fontSize: 14,
    lineHeight: 23,
    padding: { top: 16, bottom: 16 },
    readOnly: true
  });
  const disposeTheme = bindEditorTheme(monaco, host.ownerDocument);
  editor.onDidDispose(disposeTheme);
  return { model, editor };
}
