import * as signalR from '@microsoft/signalr';
import * as Y from 'yjs';
import { createWhiteboardView } from './whiteboard-view.js';
import { createObserverApplication, observerRetryDelay } from './observer-application.js';

const connection = new signalR.HubConnectionBuilder().withUrl('/observerHub')
  .withAutomaticReconnect({ nextRetryDelayInMilliseconds: observerRetryDelay })
  .configureLogging(signalR.LogLevel.None).build();
connection.keepAliveIntervalInMilliseconds = 5000;
connection.serverTimeoutInMilliseconds = 15000;

/** Loads Monaco only when the guest is watching a code room. */
async function loadMonaco() {
  /** @type {typeof globalThis & {MonacoEnvironment: import('monaco-editor').Environment}} */ (globalThis).MonacoEnvironment = { getWorker(_module, label) {
    const name = ({ json: 'json', css: 'css', html: 'html', typescript: 'ts', javascript: 'ts' })[label] || 'editor';
    return new Worker(new URL(`${name}.worker.js${new URL(import.meta.url).search}`, import.meta.url), { type: 'module' });
  } };
  return import('monaco-editor');
}

createObserverApplication({ window, document, connection, Y, createWhiteboardView,
  loadMonaco, connectedState: signalR.HubConnectionState.Connected });
