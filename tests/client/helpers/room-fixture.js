import * as Y from 'yjs';
import { createRoomApplication } from '../../../src/SealCode/wwwroot/room-application.js';

export class Element {
  constructor(tag = 'div') {
    this.tag = tag;
    this.children = [];
    this.handlers = new Map();
    this.dataset = {};
    this.textContent = '';
    this.disabled = false;
    this._value = '';
    const classes = new Set();
    this.classList = {
      add: (name) => classes.add(name),
      remove: (name) => classes.delete(name),
      contains: (name) => classes.has(name),
      toggle(name, force = !classes.has(name)) {
        if (force) classes.add(name); else classes.delete(name);
      }
    };
  }
  addEventListener(name, callback) {
    const callbacks = this.handlers.get(name) ?? [];
    callbacks.push(callback);
    this.handlers.set(name, callbacks);
  }
  removeEventListener(name, callback) {
    this.handlers.set(name, (this.handlers.get(name) ?? []).filter((fn) => fn !== callback));
  }
  fire(name, options = {}) {
    const event = { target: this, preventDefault() {}, stopImmediatePropagation() {}, ...options };
    return Promise.all((this.handlers.get(name) ?? []).map((fn) => fn(event)));
  }
  append(...children) { this.children.push(...children); }
  appendChild(child) { this.append(child); }
  get options() { return this.children; }
  set innerHTML(_) { this.children = []; this._value = ''; }
  get value() { return this._value || this.children[this.selectedIndex ?? 0]?.value || ''; }
  set value(value) { this._value = value; }
  querySelector() { return this.label ??= new Element(); }
  contains(node) { return node === this || this.children.some((child) => child.contains(node)); }
  focus() { this.focused = true; }
  click() { return this.fire('click'); }
  remove() { this.removed = true; }
}

export function editorFixture(theme = 'dark') {
  const view = new Element();
  view.getComputedStyle = () => ({ getPropertyValue: () => '#123456' });
  const nodes = new Map();
  const document = new Element();
  document.defaultView = view;
  document.documentElement = { dataset: { theme } };
  document.createElement = (tag) => new Element(tag);
  document.getElementById = (id) => nodes.get(id) ?? null;
  document.head = { appendChild: (node) => nodes.set(node.id, node) };
  document.body = new Element();
  const host = new Element();
  host.ownerDocument = document;
  const callbacks = {};
  const model = {
    value: '',
    getValue() { return this.value; },
    getValueLength() { return this.value.length; },
    getPositionAt(offset) {
      const lines = this.value.slice(0, offset).split('\n');
      return { lineNumber: lines.length, column: lines.at(-1).length + 1 };
    },
    getOffsetAt: ({ lineNumber, column }) => model.value.split('\n').slice(0, lineNumber - 1).reduce((n, line) => n + line.length + 1, column - 1),
    getLineMaxColumn: (line) => (model.value.split('\n')[line - 1]?.length ?? 0) + 1,
    update(value) {
      const old = this.value;
      this.value = value;
      callbacks.content?.({ changes: [{ rangeOffset: 0, rangeLength: old.length, text: value }] });
    },
    setValue(value) { this.binding.replace(value); }
  };
  const editor = {
    readOnly: true,
    position: { lineNumber: 1, column: 1 },
    selection: { startLineNumber: 1, endLineNumber: 1, isEmpty: () => true },
    getModel: () => model,
    getOption: () => editor.readOnly,
    updateOptions: ({ readOnly }) => { editor.readOnly = readOnly; },
    getPosition: () => editor.position,
    getSelection: () => editor.selection,
    getSelections: () => editor.selections ?? [editor.selection],
    hasTextFocus: () => editor.focused ?? true,
    deltaDecorations: (_, next) => next.map((__, index) => `caret-${index}`),
    onDidChangeModelContent: (fn) => { callbacks.content = fn; },
    onKeyDown: (fn) => { callbacks.key = fn; },
    onDidPaste: (fn) => { callbacks.paste = fn; },
    onDidChangeCursorSelection: (fn) => { callbacks.selection = fn; },
    onDidDispose: (fn) => { callbacks.dispose = fn; }
  };
  const themes = [];
  const monaco = {
    Range: class { constructor(...coordinates) { this.coordinates = coordinates; } },
    editor: {
      EditorOption: { readOnly: 'readOnly' },
      CursorChangeReason: { Explicit: 3 },
      TrackedRangeStickiness: { NeverGrowsWhenTypingAtEdges: 1 },
      createModel: (value, language) => { model.value = value; model.language = language; return model; },
      create: (_, options) => { editor.options = options; return editor; },
      setModelLanguage: (_, language) => { model.language = language; },
      defineTheme: (name, definition) => themes.push({ name, definition }),
      setTheme: (name) => { editor.theme = name; }
    }
  };
  return { document, view, nodes, host, model, editor, monaco, callbacks, themes };
}

export const flush = () => new Promise(setImmediate);

/** Real Yjs sessions and controllers; DOM, editor and network are the boundaries. */
export async function roomFixture(context, options = {}) {
  context.mock.timers.enable({ apis: ['setTimeout', 'Date'], now: 1000 });
  context.mock.method(console, 'warn', () => {});
  const f = editorFixture();
  const nodes = f.nodes;
  const ids = ['connectionStatusEl', 'roomNameEl', 'createdByEl', 'participantsList',
    'languageSelect', 'versionNumberEl', 'joinOverlay', 'joinBtn', 'displayNameInput',
    'joinError', 'syncError', 'deleteRoomBtn', 'deleteRoomError', 'downloadLocalText', 'generateDisplayName'];
  for (const id of ids) nodes.set(id, new Element());
  const elements = Object.fromEntries(nodes);
  elements.editorHost = f.host;
  elements.displayNameInput.value = 'Alice';
  const previousNode = Object.getOwnPropertyDescriptor(globalThis, 'Node');
  Object.defineProperty(globalThis, 'Node', { configurable: true, value: Element });
  const server = new Y.Doc();
  server.getText('monaco').insert(0, options.text ?? 'seed');
  let revision = 0;
  let metadata = 0;
  let language = 'sql';
  const calls = [];
  const handlers = new Map();
  const failures = new Map();
  const snapshot = () => ({
    Name: 'Interview', CreatedBy: 'Admin', Language: language, Text: '',
    Version: revision + metadata + 1, Users: ['Alice'], DocumentRevision: revision,
    MetadataRevision: metadata, PresenceRevision: 1, CheckpointRevision: revision,
    YjsState: Buffer.from(Y.encodeStateAsUpdate(server)).toString('base64'), YjsUpdates: []
  });
  const hub = {
    state: 'Disconnected', connectionId: 'connection-1',
    on: (name, fn) => handlers.set(name, fn),
    emit: (name, ...args) => handlers.get(name)?.(...args),
    onclose: (fn) => { hub.closed = fn; },
    onreconnecting: (fn) => { hub.reconnecting = fn; },
    onreconnected: (fn) => { hub.reconnected = fn; },
    async start() {
      if (options.startError) throw new Error('offline');
      hub.state = 'Connected';
    },
    async stop() { hub.state = 'Disconnected'; hub.closed(); },
    async invoke(method, ...args) {
      calls.push({ method, args });
      if (failures.has(method)) throw failures.get(method);
      if (method === 'JoinRoom' || method === 'SyncYjs') return snapshot();
      if (method === 'GetRoomRevisions') return { DocumentRevision: revision, MetadataRevision: metadata, PresenceRevision: 1 };
      if (method === 'UpdateYjs') {
        Y.applyUpdate(server, Buffer.from(args[1], 'base64'));
        hub.emit('YjsUpdated', args[1], ++revision, revision + metadata + 1, 'Alice');
      }
      if (method === 'CheckpointYjs') { hub.emit('YjsCheckpointed', args[3]); return true; }
      if (method === 'SetLanguage') {
        language = args[1];
        hub.emit('LanguageUpdated', language, revision + ++metadata + 1, metadata);
      }
    }
  };
  const signalR = {
    HubConnectionState: { Connected: 'Connected', Disconnected: 'Disconnected' },
    HubConnectionBuilder: class {
      withUrl() { return this; }
      withAutomaticReconnect() { return this; }
      build() { return hub; }
    }
  };
  class Binding {
    constructor(text, model) {
      this.text = text;
      this.model = model;
      model.binding = this;
      this.render = () => model.update(text.toString());
      text.observe(this.render);
      this.render();
    }
    replace(value) {
      this.text.doc.transact(() => {
        this.text.delete(0, this.text.length);
        this.text.insert(0, value);
      }, this);
    }
    destroy() { this.text.unobserve(this.render); }
  }
  const requests = [];
  context.mock.method(globalThis, 'fetch', async (url) => {
    requests.push(url);
    if (options.fetchError) throw new Error('network');
    const data = url === '/features' ? options.features ?? {} : url === '/languages' ? ['sql', 'csharp'] : { CanDelete: true };
    return { ok: true, json: async () => data };
  });
  f.view.location = { assign() {} };
  f.view.confirm = () => false;
  const board = options.board ? {
    readOnly: true, downloads: 0,
    setReadOnly(value) { this.readOnly = value; },
    bind(doc) { const scope = doc.getMap('whiteboard'); this.scope = scope; return this.binding = { scope, destroy() {} }; },
    download() { this.downloads++; }
  } : null;
  createRoomApplication({ ...f, signalR, Y, MonacoBinding: Binding,
    Awareness: class { setLocalStateField() {} destroy() {} },
    window: f.view, roomId: 'room', elements, board });
  context.after(async () => {
    hub.emit('RoomKilled', 'cleanup');
    await flush();
    f.callbacks.dispose?.();
    server.destroy();
    if (previousNode) Object.defineProperty(globalThis, 'Node', previousNode);
    else delete globalThis.Node;
  });
  await flush();
  async function tick(milliseconds) { context.mock.timers.tick(milliseconds); await flush(); }
  async function join() { await elements.joinBtn.click(); await flush(); }
  return { ...f, elements, hub, calls, failures, server, snapshot, requests, board, join, tick };
}
