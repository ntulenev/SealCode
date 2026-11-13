import { bindEditorTheme } from './editor-theme.js';
/** Owns Monaco setup; collaborative operations are supplied by the controller.
 * @param {typeof import('monaco-editor')} monaco
 * @param {HTMLElement} host
 * @param {{language: string, undo: () => void, redo: () => void}} callbacks */
export function createMonacoAdapter(monaco, host, { language, undo, redo }) {
  const model = /** @type {import('monaco-editor').editor.ITextModel & {undo: () => Promise<void>, redo: () => Promise<void>}} */ (monaco.editor.createModel('', language || 'plaintext'));
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
    quickSuggestions: true,
    suggestOnTriggerCharacters: true,
    wordBasedSuggestions: 'currentDocument',
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
