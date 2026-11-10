import { test } from 'node:test';
import assert from 'node:assert/strict';
import { createMarkdownRenderer } from '../../src/SealCode/wwwroot/snippet-markdown.js';

function fixture(colorize = async (source, language) => `<span>${language}:${source}</span>`) {
  const target = { children: [], append(item) { this.children.push(item); } };
  const element = tag => ({ tag, textContent: '', innerHTML: '', children: [], attributes: {},
    setAttribute(key, value) { this.attributes[key] = value; }, append(item) { this.children.push(item); } });
  const render = createMarkdownRenderer({ document: { createElement: element, createElementNS: (_, tag) => element(tag) }, colorize });
  return { target, render };
}

test('prose and raw HTML stay text without invoking the highlighter', async () => {
  const { target, render } = fixture(() => { throw new Error('Must not highlight prose'); });
  const source = '<script>alert(1)</script> [link](javascript:alert(1))';
  assert.equal(await render(`  ${source}  `, 'sql', target), source);
  assert.equal(target.children[0].textContent, source);
  assert.equal(target.children[0].innerHTML, '');
  assert.equal(await render('   ', 'sql', target), '');
});

test('whiteboard fences render safe SVG and copy a validated multi-shape payload', async () => {
  const shape = { id: 'api', type: 'rectangle', x: -100, y: 20, width: 140, height: 80, text: '<script>API</script>' };
  const payload = { format: 'sealcode-whiteboard', version: 1, shapes: [shape] };
  const fence = `\`\`\`whiteboard\n${JSON.stringify(payload)}\n\`\`\``;
  const { target, render } = fixture(() => { throw new Error('Must not highlight a diagram'); });
  const copied = JSON.parse(await render(`${fence}\n\n${fence}`, 'whiteboard', target));
  assert.equal(copied.shapes.length, 2);
  const svg = target.children[0];
  assert.equal(svg.tag, 'svg');
  assert.equal(svg.attributes.viewBox, '-132 -12 204 144');
  assert.equal(svg.children[0].children.at(-1).children[0].textContent, shape.text);
  assert.equal(svg.children[0].innerHTML, '');
  await assert.rejects(render('```whiteboard\n{}\n```', 'whiteboard', target), /Invalid whiteboard/);
  await assert.rejects(fixture().render(`${fence}\n\n\`\`\`sql\nSELECT 1;\n\`\`\``, 'sql', target), /only diagram fences/);
});

test('fences preserve indentation, use explicit or fallback language and combine copied code', async () => {
  const calls = [];
  const { target, render } = fixture(async (source, language) => { calls.push([source, language]); return 'highlighted'; });
  const source = 'Prompt\n```sql\r\n  SELECT 1;\r\n```\n\n```\nSELECT 2;\n```';
  assert.equal(await render(source, 'javascript', target), '  SELECT 1;\n\nSELECT 2;');
  assert.deepEqual(calls, [['  SELECT 1;', 'sql'], ['SELECT 2;', 'javascript']]);
  assert.equal(target.children.filter(item => item.tag === 'pre').length, 2);
  assert.equal(target.children[1].innerHTML, 'highlighted');
});

test('stale highlighter completion cannot replace the current preview', async () => {
  let finish;
  let current = true;
  const { target, render } = fixture(() => new Promise(resolve => { finish = resolve; }));
  const pending = render('```sql\nSELECT 1;\n```', 'sql', target, () => current);
  current = false; finish('stale HTML');
  assert.equal(await pending, null);
  assert.equal(target.children[0].innerHTML, '');
});

test('highlighter failures propagate while retaining the plain source', async () => {
  const { target, render } = fixture(async () => { throw new Error('Highlight failed'); });
  await assert.rejects(render('```sql\nSELECT 1;\n```', 'sql', target), /Highlight failed/);
  assert.equal(target.children[0].textContent, 'SELECT 1;');
});
