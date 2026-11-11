import * as monaco from 'monaco-editor';
import * as Y from 'yjs';
import { bindEditorTheme } from './editor-theme.js';
import { createWhiteboardView } from './whiteboard-view.js';

// This entry point deliberately has no room application, transport, or API client.
/** @type {typeof globalThis & { MonacoEnvironment: import('monaco-editor').Environment }} */ (globalThis).MonacoEnvironment = {
  getWorker(_module, label) {
    const name = ['javascript', 'typescript'].includes(label) ? 'ts' : 'editor';
    const entry = new URL(import.meta.url);
    return new Worker(new URL(`${name}.worker.js${entry.search}`, entry), { type: 'module' });
  }
};

const sample = `// Try editing this code. Your changes stay in this tab.
// Collect orders that are ready to ship.
const orders = [
  { id: 'SC-101', paid: true, items: 3 },
  { id: 'SC-102', paid: false, items: 1 },
  { id: 'SC-103', paid: true, items: 2 },
];

function readyToShip(orders) {
  return orders
    .filter(order => order.paid)
    .map(order => ({
      orderId: order.id,
      status: 'ready',
    }));
}

const shipments = readyToShip(orders);
console.log(shipments);
`;
const editor = monaco.editor.create(document.getElementById('demoEditor'), {
  value: sample,
  language: 'javascript',
  automaticLayout: true,
  minimap: { enabled: false },
  fontSize: 14,
  padding: { top: 28, bottom: 16 },
  scrollBeyondLastLine: false
});
const unbindTheme = bindEditorTheme(monaco, document);
const model = editor.getModel();
const cursor = document.createElement('span');
cursor.className = 'demo-code-cursor';
cursor.textContent = 'Maya · demo';
cursor.setAttribute('aria-hidden', 'true');
let position = { lineNumber: 10, column: 10 };
const widget = {
  getId: () => 'demo-collaborator',
  getDomNode: () => cursor,
  getPosition: () => ({ position, preference: [monaco.editor.ContentWidgetPositionPreference.EXACT] })
};
editor.addContentWidget(widget);

const boardHost = document.getElementById('demoBoard');
const board = createWhiteboardView(boardHost);
const doc = new Y.Doc();
const binding = board.bind(doc);
const undo = new Y.UndoManager(binding.scope, { trackedOrigins: new Set([binding]) });
board.beforeChange = () => undo.stopCapturing();
board.undo = () => { undo.undo(); return true; };
board.redo = () => { undo.redo(); return true; };
board.setReadOnly(false);

function seedBoard() {
  doc.transact(() => {
    binding.scope.clear();
    const shapes = [
      ['api', 'rectangle', 80, 130, 180, 85, 'Orders API'],
      ['queue', 'queue', 380, 130, 170, 85, 'Order queue'],
      ['worker', 'rectangle', 670, 130, 180, 85, 'Shipping service'],
      ['db', 'database', 80, 330, 180, 100, 'Orders DB'],
      ['publish', 'arrow', 260, 172, 120, 0, 'publish'],
      ['consume', 'arrow', 550, 172, 120, 0, 'consume'],
      ['store', 'bidirectional-arrow', 170, 215, 0, 115, 'read / write']
    ];
    for (const [id, type, x, y, width, height, text] of shapes) {
      binding.scope.set(String(id), { id, type, x, y, width, height, text });
    }
  }, 'seed');
  undo.clear();
  boardHost.querySelector('button[aria-label="Fit diagram"]')?.dispatchEvent(new MouseEvent('click'));
}
seedBoard();
document.getElementById('resetCode').addEventListener('click', () => editor.setValue(sample));
document.getElementById('resetBoard').addEventListener('click', seedBoard);

let step = 0;
let paused = false;
const motion = window.matchMedia('(prefers-reduced-motion: reduce)');
const pauseButton = document.getElementById('pauseDemo');
function updateCursors() {
  const points = [[10, 10], [11, 20], [13, 12], [18, 22], [5, 17]];
  const [lineNumber, column] = points[step % points.length];
  position = model.validatePosition({ lineNumber, column });
  editor.layoutContentWidget(widget);
  // Follow the queue even when the visitor moves it.
  const queue = binding.scope.get('queue');
  board.setRemoteCursor('Leo · demo', {
    x: (queue?.x ?? 380) + (queue?.width ?? 170) + 16 + Math.sin(step) * 24,
    y: (queue?.y ?? 130) + 60 + Math.cos(step) * 28
  });
  step++;
}
updateCursors();
const timer = window.setInterval(() => {
  if (!paused && !motion.matches && !document.hidden) updateCursors();
}, 1800);
pauseButton.addEventListener('click', () => {
  paused = !paused;
  pauseButton.setAttribute('aria-pressed', String(paused));
  pauseButton.textContent = paused ? 'Resume demo cursors' : 'Pause demo cursors';
});
// Refit on viewport changes, without replacing the visitor's diagram.
const resize = new ResizeObserver(() => {
  boardHost.querySelector('button[aria-label="Fit diagram"]')?.dispatchEvent(new MouseEvent('click'));
});
resize.observe(boardHost);
window.addEventListener('pagehide', (event) => {
  if (event.persisted) return;
  window.clearInterval(timer);
  resize.disconnect();
  unbindTheme();
  editor.dispose();
  model.dispose();
  undo.destroy();
  binding.destroy();
  doc.destroy();
});
