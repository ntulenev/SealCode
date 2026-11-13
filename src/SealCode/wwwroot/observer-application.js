import { HubObserverMethods, HubObserverEvents } from './hub-contract.js';
import { applyObserverTextDelta } from './observer-text.js';
import { uint8FromBase64 } from './encoding.js';
import { createObserverPresence } from './observer-presence.js';

/** Bounds reconnect backoff, including jitter, to thirty seconds.
 * @param {{previousRetryCount: number}} context
 * @param {() => number} [random]
 */
export function observerRetryDelay(context, random = Math.random) {
  return Math.min(30000, 1000 * 2 ** Math.min(context.previousRetryCount, 5) * (0.8 + random() * 0.4));
}

/** Composes the one-way guest document view and its recovery lifecycle.
 * Dependencies are injected so network failures, timer windows and asynchronous
 * editor startup are covered without a browser or a real server.
 * @param {import('./client-types.js').ObserverApplicationOptions} options
 */
export function createObserverApplication({ window, document, connection, Y, createWhiteboardView,
  loadMonaco, connectedState, now = Date.now, random = Math.random }) {
  /** @param {string} id */
  function element(id) {
    return /** @type {HTMLElement} */ (document.getElementById(id));
  }
  const token = window.location.hash.slice(1);
  const status = element('watchStatus');
  const setCount = createObserverPresence(document);
  /** @type {import('yjs').Doc | undefined} */
  let doc;
  /** @type {ReturnType<typeof createWhiteboardView> | undefined} */
  let board;
  /** @type {ReturnType<ReturnType<typeof createWhiteboardView>['bind']> | undefined} */
  let boardBinding;
  /** @type {import('monaco-editor').editor.IStandaloneCodeEditor | undefined} */
  let editor;
  /** @type {import('monaco-editor').editor.ITextModel | undefined} */
  let model;
  /** @type {typeof import('monaco-editor') | undefined} */
  let monaco;
  let unbindText = () => {};
  let revision = -1;
  let metadataRevision = -1;
  let presenceRevision = -1;
  let recovering = false;
  let awaitingSnapshot = true;
  let terminal = false;
  /** @type {number | undefined} */
  let probeTimer;
  /** @type {number | undefined} */
  let recoveryTimer;
  /** @type {number | undefined} */
  let startTimer;
  let retryMessage = '';
  let lastSnapshotAt = 0;
  let processing = Promise.resolve();
  let bufferedMessages = 0;
  let bufferedBytes = 0;

  function disconnected() { status.textContent = retryMessage || 'No connection — displayed data may be out of date.'; }
  /** @param {unknown} error */
  function unavailable(error) {
    const message = String(error && typeof error === 'object' && 'message' in error ? error.message : error);
    if (/ObserverUnavailable|Forbidden/.test(message)) {
      terminal = true;
      status.textContent = 'Guest access is unavailable. The link may have been revoked or the room closed.';
      void connection.stop();
      return true;
    }
    if (/ObserverLimitReached/.test(message)) retryMessage = 'The guest limit has been reached. Try again later.';
    status.textContent = retryMessage || 'Waiting to synchronize…';
    return false;
  }
  function recover() {
    if (terminal || recovering || recoveryTimer) return;
    awaitingSnapshot = true;
    status.textContent = 'Synchronizing…';
    const delay = Math.max(300, 16000 - (now() - lastSnapshotAt)) + random() * 1000;
    recoveryTimer = window.setTimeout(async () => {
      recoveryTimer = undefined;
      if (terminal || connection.state !== connectedState) return;
      recovering = true;
      try { await connection.invoke(HubObserverMethods.SyncObserver); lastSnapshotAt = now(); }
      catch (error) { if (!unavailable(error)) { recovering = false; recover(); } }
      finally { recovering = false; }
    }, delay);
  }
  async function makeEditor() {
    if (editor) return;
    monaco = await loadMonaco();
    if (terminal) return;
    model = monaco.editor.createModel('', 'plaintext');
    editor = monaco.editor.create(element('monacoEditor'), {
      model, readOnly: true, domReadOnly: true, automaticLayout: true,
      minimap: { enabled: false }, theme: document.documentElement.dataset.theme === 'light' ? 'vs' : 'vs-dark'
    });
    new window.MutationObserver(() => monaco.editor.setTheme(document.documentElement.dataset.theme === 'light' ? 'vs' : 'vs-dark'))
      .observe(document.documentElement, { attributes: true, attributeFilter: ['data-theme'] });
  }
  function renderText() {
    // One-way binding: no Monaco/Yjs change listener can write back to the server.
    if (model && doc) {
      const view = editor.saveViewState();
      model.setValue(doc.getText('monaco').toString());
      editor.restoreViewState(view);
    }
  }
  /** @param {import('./client-types.js').ObserverMessage} message */
  function metadata(message) {
    if (message.MetadataRevision >= metadataRevision) {
      metadataRevision = message.MetadataRevision;
      element('watchLanguage').textContent = message.Language;
      if (model) monaco.editor.setModelLanguage(model, message.Language);
    }
    if (message.PresenceRevision >= presenceRevision) {
      presenceRevision = message.PresenceRevision;
      element('participantsList').replaceChildren(...message.Users.map(name => {
        const item = document.createElement('li'); item.textContent = name; return item;
      }));
    }
    setCount(message.ObserverCount, message.ObserverRevision);
  }
  /** @param {import('./client-types.js').ObserverMessage} message */
  async function apply(message) {
    if (terminal) return;
    if (message.Type === 'Count') { setCount(message.ObserverCount, message.ObserverRevision); return; }
    if (message.Type === 'RequiresResync') { recover(); return; }
    if (message.Type === 'Snapshot') {
      const next = new Y.Doc();
      try {
        if (message.YjsState) Y.applyUpdate(next, uint8FromBase64(message.YjsState));
        for (const update of message.YjsUpdates) Y.applyUpdate(next, uint8FromBase64(update.Update));
        if (!message.YjsState && !message.YjsUpdates.length && message.Text) next.getText('monaco').insert(0, message.Text);
      } catch (error) { next.destroy(); throw error; }
      unbindText();
      boardBinding?.destroy();
      doc?.destroy();
      doc = next;
      revision = message.DocumentRevision;
      metadataRevision = presenceRevision = -1;
      element('roomName').textContent = message.Name;
      if (message.Kind === 'whiteboard') {
        element('watchEditor').classList.add('hidden');
        const host = element('whiteboard');
        host.classList.remove('hidden');
        board ??= createWhiteboardView(host);
        board.setReadOnly(true);
        boardBinding = board.bind(doc);
      } else {
        await makeEditor();
        if (terminal) return;
        renderText();
        const text = doc.getText('monaco');
        /** @param {import('yjs').YTextEvent} event */
        const updateText = event => {
          if (model) applyObserverTextDelta(model, text, event.delta);
        };
        text.observe(updateText);
        unbindText = () => text.unobserve(updateText);
      }
      metadata(message);
      awaitingSnapshot = false;
      status.textContent = 'Live · Read only';
      return;
    }
    if (awaitingSnapshot || !doc) return;
    for (const update of message.YjsUpdates) {
      if (update.Revision <= revision) continue;
      if (update.Revision !== revision + 1) { recover(); return; }
      Y.applyUpdate(doc, uint8FromBase64(update.Update));
      revision = update.Revision;
    }
    if (message.DocumentRevision > revision) { recover(); return; }
    metadata(message);
  }
  connection.on(HubObserverEvents.ObserverMessage, payload => {
    // Typed JSON avoids re-escaping an entire large snapshot as a JSON string.
    const size = typeof payload === 'string' ? payload.length : JSON.stringify(payload).length;
    // Bound application buffers even while Monaco's module is loading.
    if (bufferedMessages >= 256 || bufferedBytes + size > 20 * 1024 * 1024) { recover(); return; }
    bufferedMessages++;
    bufferedBytes += size;
    processing = processing.then(() => apply(typeof payload === 'string' ? JSON.parse(payload) : payload)).catch(() => recover()).finally(() => {
      bufferedMessages--; bufferedBytes -= size;
    });
  });
  async function join() {
    if (terminal) return;
    retryMessage = '';
    awaitingSnapshot = true;
    try { await connection.invoke(HubObserverMethods.JoinObserver, token); lastSnapshotAt = 0; }
    catch (error) { if (!unavailable(error)) { await connection.stop(); } }
  }
  function scheduleProbe() {
    window.clearTimeout(probeTimer);
    if (terminal) return;
    probeTimer = window.setTimeout(async () => {
      if (connection.state === connectedState) {
        try {
          const remote = await connection.invoke(HubObserverMethods.GetObserverRevisions);
          setCount(remote.ObserverCount, remote.ObserverRevision);
          const r = remote.Revisions;
          if (awaitingSnapshot || r.DocumentRevision > revision || r.MetadataRevision > metadataRevision || r.PresenceRevision > presenceRevision) recover();
        } catch (error) { if (!unavailable(error)) recover(); }
      }
      scheduleProbe();
    }, 15000 * (0.8 + random() * 0.4));
  }
  connection.onreconnecting(disconnected);
  connection.onreconnected(join);
  connection.onclose(error => {
    if (terminal) return;
    if (error && /ObserverLimitReached|ObserverUnavailable|Forbidden/.test(error.message) && unavailable(error)) return;
    disconnected();
    window.clearTimeout(startTimer);
    startTimer = window.setTimeout(start, 5000 + random() * 5000);
  });
  async function start() {
    if (terminal) return;
    try { await connection.start(); await join(); scheduleProbe(); }
    catch (error) { if (!unavailable(error)) startTimer = window.setTimeout(start, 5000 + random() * 5000); }
  }
  window.addEventListener('pagehide', () => {
    disconnected();
    terminal = true;
    window.clearTimeout(probeTimer); window.clearTimeout(startTimer); window.clearTimeout(recoveryTimer);
    void connection.stop(); unbindText(); boardBinding?.destroy(); doc?.destroy(); editor?.dispose(); model?.dispose();
  });
  window.addEventListener('pageshow', event => { if (event.persisted) window.location.reload(); });
  const ready = token.length !== 43
    ? (terminal = true, status.textContent = 'This guest link is invalid.', Promise.resolve())
    : start();
  return { ready, idle: () => processing };
}
