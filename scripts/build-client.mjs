import { build } from 'esbuild';
await build({
  entryPoints: {
    room: 'src/SealCode/wwwroot/room.js',
    'editor.worker': 'node_modules/monaco-editor/esm/vs/editor/editor.worker.js',
    'json.worker': 'node_modules/monaco-editor/esm/vs/language/json/json.worker.js',
    'css.worker': 'node_modules/monaco-editor/esm/vs/language/css/css.worker.js',
    'html.worker': 'node_modules/monaco-editor/esm/vs/language/html/html.worker.js',
    'ts.worker': 'node_modules/monaco-editor/esm/vs/language/typescript/ts.worker.js'
  },
  bundle: true, format: 'esm', splitting: true, minify: true, sourcemap: false,
  outdir: 'src/SealCode/wwwroot/assets', loader: { '.ttf': 'file' },
  metafile: true
}).then(result => {
  const yjsPaths = Object.keys(result.metafile.inputs).filter(path => path.endsWith('yjs.mjs'));
  if (yjsPaths.length !== 1) throw new Error(`Expected one Yjs instance, found ${yjsPaths.length}`);
});
