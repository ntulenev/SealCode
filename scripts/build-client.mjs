import { build } from 'esbuild';
import { rmSync } from 'node:fs';
rmSync('src/SealCode/wwwroot/assets', { recursive: true, force: true });
await build({
  entryPoints: {
    'demo-main': 'src/SealCode/wwwroot/demo-main.js',
    'snippet-launcher-main': 'src/SealCode/wwwroot/snippet-launcher-main.js',
    'admin-snippets-main': 'src/SealCode/wwwroot/admin-snippets-main.js',
    'snippets-main': 'src/SealCode/wwwroot/snippets-main.js',
    room: 'src/SealCode/wwwroot/room.js',
    whiteboard: 'src/SealCode/wwwroot/whiteboard.js',
    'admin-main': 'src/SealCode/wwwroot/admin-main.js',
    'admin-limits-main': 'src/SealCode/wwwroot/admin-limits-main.js',
    'admin-login-main': 'src/SealCode/wwwroot/admin-login-main.js',
    'admin-token-main': 'src/SealCode/wwwroot/admin-token-main.js',
    'editor.worker': 'node_modules/monaco-editor/esm/vs/editor/editor.worker.js',
    'json.worker': 'node_modules/monaco-editor/esm/vs/language/json/json.worker.js',
    'css.worker': 'node_modules/monaco-editor/esm/vs/language/css/css.worker.js',
    'html.worker': 'node_modules/monaco-editor/esm/vs/language/html/html.worker.js',
    'ts.worker': 'node_modules/monaco-editor/esm/vs/language/typescript/ts.worker.js'
  },
  bundle: true,
  format: 'esm',
  splitting: true,
  minify: true,
  sourcemap: false,
  outdir: 'src/SealCode/wwwroot/assets',
  loader: { '.ttf': 'file' },
  metafile: true
}).then((result) => {
  const yjsPaths = Object.keys(result.metafile.inputs).filter((path) =>
    path.endsWith('yjs.mjs')
  );
  if (yjsPaths.length !== 1)
    throw new Error(`Expected one Yjs instance, found ${yjsPaths.length}`);
});
