import { createRoomApplication } from './room-application.js';
import * as monaco from 'monaco-editor';
import * as signalR from '@microsoft/signalr';
import * as Y from 'yjs';
import { MonacoBinding } from 'y-monaco';
import { Awareness } from 'y-protocols/awareness';

/** @type {typeof globalThis & { MonacoEnvironment: import('monaco-editor').Environment }} */ (
  globalThis
).MonacoEnvironment = {
  getWorker(_module, label) {
    const name =
      {
        json: 'json',
        css: 'css',
        scss: 'css',
        less: 'css',
        html: 'html',
        handlebars: 'html',
        razor: 'html',
        typescript: 'ts',
        javascript: 'ts'
      }[label] ?? 'editor';
    return new Worker(`/assets/${name}.worker.js`, { type: 'module' });
  }
};

// E2E tests use window.monaco to inspect the editor and exercise language workers.
/** @type {Window & { monaco?: typeof import('monaco-editor') }} */ (window).monaco =
  monaco;
// E2E synchronization tests create independent probe connections through this export.
/** @type {Window & { signalR?: typeof import('@microsoft/signalr') }} */ (
  window
).signalR = signalR;
const connectionStatusEl = document.getElementById('connectionStatus');
const roomNameEl = document.getElementById('roomName');
const createdByEl = document.getElementById('createdBy');
const participantsList = document.getElementById('participantsList');
const languageSelect = document.getElementById('languageSelect');
const versionNumberEl = document.getElementById('versionNumber');
const editorHost = document.getElementById('monacoEditor');
const joinOverlay = document.getElementById('joinOverlay');
const joinBtn = document.getElementById('joinBtn');
const displayNameInput = document.getElementById('displayNameInput');
const joinError = document.getElementById('joinError');
const syncError = document.getElementById('syncError');

const pathSegments = new URL(window.location.href).pathname.split('/').filter(Boolean);
const roomId = pathSegments[pathSegments.length - 1] ?? '';
createRoomApplication({
  monaco,
  signalR,
  Y,
  MonacoBinding,
  Awareness,
  window,
  document,
  roomId,
  elements: {
    connectionStatusEl,
    roomNameEl,
    createdByEl,
    participantsList,
    languageSelect,
    versionNumberEl,
    editorHost,
    joinOverlay,
    joinBtn,
    displayNameInput,
    joinError,
    syncError
  }
});
