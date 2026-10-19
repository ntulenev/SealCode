# ADR 0001: Selective collaborative undo

## Status

Accepted.

## Context

Monaco's native undo stack contains both user edits and edits applied by the
Yjs binding. Invoking native undo can therefore turn a remote edit into a new
local deletion. The SignalR server also treats Yjs updates as opaque bytes and
cannot merge client snapshots itself.

## Decision

- Text has one mutation path: Yjs updates.
- Each browser tab owns a `Y.UndoManager` scoped to the room `Y.Text`.
- The manager tracks only the active `MonacoBinding` transaction origin.
- Undo and redo are ordinary Yjs transactions and are broadcast like any other
  local change.
- Remote and bootstrap origins are never tracked.
- The visible local history is limited to 100 grouped operations. Continuous
  edits within 500 ms may form one operation. A complete memory guard clears
  the history after its estimated retained updates exceed 8 MiB.
  The estimate conservatively accumulates encoded updates, UTF-16 deleted text,
  and item overhead, including undo/redo and discarded entries. Clearing releases
  retained deleted content through Yjs garbage collection when GC is enabled.
- A reconnect merges the server sync payload into the existing `Y.Doc`, keeping
  local history. Leaving the room or reloading the page starts a new history.
- The server stores a checkpoint plus every incremental update after it. A
  client checkpoint is accepted only when its based-on document revision is
  still current.

## Consequences

Undo never targets another user's operation, concurrent undo operations remain
commutative, and a new joiner cannot lose an update because of a stale
last-writer-wins client snapshot. The server keeps a short opaque update tail
until a caught-up client submits a checkpoint.
