# CRDT recovery and room closure

Accepted updates are broadcast independently of the author's connection lifetime.
A disconnected author must not cancel delivery of an already journaled update.

Clients request a participant-only snapshot when document revisions have gaps.
A revision probe every five seconds also detects a missing final update. The
snapshot is merged into the existing Y.Doc with the bootstrap origin, preserving
local undo history. Live updates arriving during the snapshot request are buffered
and replayed afterwards. Synchronization does not re-register the participant.

Closing a room clears pending edits and timers, destroys the CRDT binding, and
makes the editor read-only while retaining the visible text for copying. Async
send/checkpoint/recovery completions are scoped to their document session. A
terminal `Room not found` response closes the room even if `RoomKilled` was missed.
Temporary transport failures continue to retry normally.

Checkpoint contents remain client supplied. By the project owner's decision,
participants are trusted to submit correct CRDT state; verifying the semantic
contents on the server is outside this change. There is no server JavaScript
runtime or additional CRDT decoder dependency.
