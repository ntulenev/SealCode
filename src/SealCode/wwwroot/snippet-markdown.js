/** Creates the library's safe Markdown renderer with an explicit syntax highlighter. */
/** @param {{document: Document, colorize: (source: string, language: string) => Promise<string>}} options */
export function createMarkdownRenderer({ document, colorize }) {
  /** Render only plain text and fenced code. Raw HTML and Markdown links cannot execute. */
  return async function renderMarkdown(/** @type {string} */ markdown, /** @type {string} */ language,
    /** @type {HTMLElement} */ target, /** @type {() => boolean} */ isCurrent = () => true) {
    const blocks = markdown.split(/^```([^\r\n]*)\r?\n([\s\S]*?)^```\s*$/gm);
    const code = [];
    for (let i = 0; i < blocks.length; i += 3) {
      if (blocks[i].trim()) {
        const text = document.createElement('p');
        text.className = 'snippet-prose';
        text.textContent = blocks[i].trim();
        target.append(text);
      }
      if (blocks[i + 2] !== undefined) {
        const pre = document.createElement('pre');
        pre.className = 'snippet-code';
        const source = blocks[i + 2].replace(/\r?\n$/, '');
        pre.textContent = source;
        target.append(pre);
        code.push(source);
        const html = await colorize(source, blocks[i + 1].trim() || language);
        if (!isCurrent()) return null;
        pre.innerHTML = html;
      }
    }
    return code.length ? code.join('\n\n') : markdown.trim();
  };
}
