BEGIN;
CREATE SCHEMA IF NOT EXISTS statistics;
CREATE TABLE IF NOT EXISTS statistics.admin_accounts (
    id text PRIMARY KEY,
    display_name text NOT NULL
);
CREATE TABLE IF NOT EXISTS statistics.events (
    id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    user_id text NOT NULL REFERENCES statistics.admin_accounts(id),
    occurred_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    event_type text NOT NULL CHECK (event_type IN ('login', 'room')),
    provider text,
    room_id text,
    room_name text,
    room_kind text CHECK (room_kind IN ('code', 'whiteboard')),
    language text
);
CREATE INDEX IF NOT EXISTS ix_events_time ON statistics.events(occurred_at);
CREATE INDEX IF NOT EXISTS ix_events_user_time ON statistics.events(user_id, occurred_at DESC, id DESC);
COMMIT;
