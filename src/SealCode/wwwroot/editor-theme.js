/** Keep Monaco in sync without replacing its model, selection, or undo history.
 * @param {typeof import('monaco-editor')} monaco
 * @param {Document} document
 */
export function bindEditorTheme(monaco, document) {
  const view = document.defaultView;
  const apply = () => {
    const light = document.documentElement.dataset.theme === 'light';
    const name = light ? 'sealcode-light' : 'sealcode-dark';
    const styles = view.getComputedStyle(document.documentElement);
    /** @param {string} name */
    const color = (name) => styles.getPropertyValue(name).trim();
    monaco.editor.defineTheme(name, {
      base: light ? 'vs' : 'vs-dark',
      inherit: true,
      rules: [],
      colors: {
        'editor.background': color('--editor-bg'),
        'editor.foreground': color('--text'),
        'editorLineNumber.foreground': color('--muted'),
        'editorLineNumber.activeForeground': color('--text'),
        'editorCursor.foreground': color('--accent'),
        'editor.selectionBackground': color('--selection'),
        'editor.lineHighlightBackground': color('--panel'),
        'editorWidget.background': color('--panel'),
        'editorWidget.border': color('--border'),
        'editorSuggestWidget.background': color('--panel'),
        'editorSuggestWidget.foreground': color('--text'),
        'editorSuggestWidget.border': color('--border'),
        'editorSuggestWidget.selectedBackground': color('--selection')
      }
    });
    monaco.editor.setTheme(name);
  };
  apply();
  view.addEventListener('sealcode:themechange', apply);
  return () => view.removeEventListener('sealcode:themechange', apply);
}
