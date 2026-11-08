import * as monaco from 'monaco-editor';
import { bindEditorTheme } from './editor-theme.js';
import { createMarkdownRenderer } from './snippet-markdown.js';
import { createSnippetEditor } from './admin-snippets.js';

bindEditorTheme(monaco, document);
const renderMarkdown = createMarkdownRenderer({ document,
  colorize: (source, language) => monaco.editor.colorize(source, language, {}) });
createSnippetEditor({ document, window, fetch, confirm, renderMarkdown });
