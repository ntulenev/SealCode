/** Wraps explicit paragraphs and long words without changing the stored label.
 * @param {string} text @param {number} width @param {number} fontSize
 * @param {(text: string, fontSize: number) => number} measure */
export function wrapText(text, width, fontSize, measure) {
  const lines = [];
  for (const paragraph of text.replace(/\r\n?/g, '\n').replace(/\t/g, '    ').split('\n')) {
    let line = '';
    for (const token of paragraph.match(/\s+|\S+/gu) ?? []) {
      if (line && measure(line + token, fontSize) > width) {
        // Keep separating spaces in the SVG text content, even at a soft wrap.
        lines.push(line + (token.trim() ? '' : token));
        line = '';
        if (!token.trim()) continue;
      }
      for (const character of token) {
        if (line && measure(line + character, fontSize) > width) {
          lines.push(line);
          line = '';
        }
        line += character;
      }
    }
    lines.push(line);
  }
  return lines;
}

/** Fits wrapped lines to the available label area, reducing the font when needed.
 * @param {string} text @param {number} width @param {number} height
 * @param {(text: string, fontSize: number) => number} [measure] */
export function layoutText(text, width, height, measure = (value, size) => Array.from(value).length * size * 0.6) {
  let fontSize = 18;
  let lines = wrapText(text, Math.max(1, width), fontSize, measure);
  while (fontSize > 8 && lines.length * fontSize * 1.22 > height) {
    fontSize--;
    lines = wrapText(text, Math.max(1, width), fontSize, measure);
  }
  return { lines, fontSize, lineHeight: fontSize * 1.22 };
}
