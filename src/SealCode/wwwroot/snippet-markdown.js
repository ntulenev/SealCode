import { parseWhiteboardClipboard } from './whiteboard-clipboard.js';
import { bounds } from './whiteboard-model.js';
import { createShapeElement } from './whiteboard-shape.js';

/** Creates the library's safe Markdown renderer with an explicit syntax highlighter. */
/** @param {{document: Document, colorize: (source: string, language: string) => Promise<string>}} options */
export function createMarkdownRenderer({ document, colorize }) {
  /** Render only plain text and fenced code. Raw HTML and Markdown links cannot execute. */
  return async function renderMarkdown(/** @type {string} */ markdown, /** @type {string} */ language,
    /** @type {HTMLElement} */ target, /** @type {() => boolean} */ isCurrent = () => true) {
    const blocks = markdown.split(/^```([^\r\n]*)\r?\n([\s\S]*?)^```\s*$/gm);
    const code = [];
    const shapes = [];
    for (let i = 0; i < blocks.length; i += 3) {
      if (blocks[i].trim()) {
        const text = document.createElement('p');
        text.className = 'snippet-prose';
        text.textContent = blocks[i].trim();
        target.append(text);
      }
      if (blocks[i + 2] !== undefined) {
        const source = blocks[i + 2].replace(/\r?\n$/, '');
        if (blocks[i + 1].trim() === 'whiteboard') {
          const diagram = parseWhiteboardClipboard(source);
          if (!diagram) throw new Error('Invalid whiteboard snippet.');
          shapes.push(...diagram);
          const svg = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
          svg.setAttribute('class', 'snippet-whiteboard');
          svg.setAttribute('role', 'img');
          svg.setAttribute('aria-label', 'Whiteboard snippet preview');
          const boxes = diagram.map(bounds);
          const x = Math.min(...boxes.map(b => b.x)) - 32;
          const y = Math.min(...boxes.map(b => b.y)) - 32;
          const width = Math.max(...boxes.map(b => b.x + b.width)) - x + 32;
          const height = Math.max(...boxes.map(b => b.y + b.height)) - y + 32;
          svg.setAttribute('viewBox', `${x} ${y} ${width} ${height}`);
          for (const shape of diagram) svg.append(createShapeElement(shape, document));
          target.append(svg);
          const hint = document.createElement('p');
          hint.className = 'snippet-prose';
          hint.textContent = 'Copy snippet, then focus the whiteboard canvas and press Ctrl+V (Cmd+V on Mac).';
          target.append(hint);
          continue;
        }
        const pre = document.createElement('pre');
        pre.className = 'snippet-code';
        pre.textContent = source;
        target.append(pre);
        code.push(source);
        const html = await colorize(source, blocks[i + 1].trim() || language);
        if (!isCurrent()) return null;
        pre.innerHTML = html;
      }
    }
    if (shapes.length) {
      const payload = JSON.stringify({ format: 'sealcode-whiteboard', version: 1, shapes });
      if (code.length || !parseWhiteboardClipboard(payload)) throw new Error('Whiteboard snippets require only diagram fences within clipboard limits.');
      return payload;
    }
    return code.length ? code.join('\n\n') : markdown.trim();
  };
}
