import { createRoomApplication } from './room-application.js';
import { createWhiteboardView } from './whiteboard-view.js';
import * as signalR from '@microsoft/signalr';
import * as Y from 'yjs';
import { Awareness } from 'y-protocols/awareness';

const connectionStatusEl = document.getElementById('connectionStatus');
const roomNameEl = document.getElementById('roomName');
const createdByEl = document.getElementById('createdBy');
const participantsList = document.getElementById('participantsList');
const versionNumberEl = document.getElementById('versionNumber');
const joinOverlay = document.getElementById('joinOverlay');
const joinBtn = document.getElementById('joinBtn');
const displayNameInput = document.getElementById('displayNameInput');
const joinError = document.getElementById('joinError');
const syncError = document.getElementById('syncError');
const deleteRoomBtn = document.getElementById('deleteRoom');
const deleteRoomError = document.getElementById('deleteRoomError');

const pathSegments = new URL(window.location.href).pathname.split('/').filter(Boolean);
const roomId = pathSegments[pathSegments.length - 1] ?? '';
createRoomApplication({
  monaco: null,
  signalR,
  Y,
  MonacoBinding: null,
  Awareness,
  window,
  document,
  roomId,
  board: createWhiteboardView(document.getElementById('whiteboard')),
  elements: {
    connectionStatusEl,
    roomNameEl,
    createdByEl,
    participantsList,
    languageSelect: null,
    versionNumberEl,
    editorHost: null,
    joinOverlay,
    joinBtn,
    displayNameInput,
    joinError,
    syncError,
    deleteRoomBtn,
    deleteRoomError
  }
});
