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
    theme: 'vs-dark',
    minimap: { enabled: false },
    automaticLayout: true,
    fixedOverflowWidgets: true,
    emptySelectionClipboard: false,
    fontFamily: 'IBM Plex Mono, monospace',
    fontSize: 14,
    readOnly: true
  });
  return { model, editor };
}
