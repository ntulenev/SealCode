-- Apply once using a schema-owner account. The application itself only reads.
BEGIN;

CREATE SCHEMA snippets;

CREATE TABLE snippets.catalog_state (
    id integer PRIMARY KEY CHECK (id = 1),
    updated_at timestamptz NOT NULL
);
INSERT INTO snippets.catalog_state VALUES (1, clock_timestamp());

CREATE TABLE snippets.categories (
    language text PRIMARY KEY CHECK (language ~ '^[a-z][a-z0-9_-]{0,31}$'),
    title text NOT NULL CHECK (length(btrim(title)) > 0),
    sort_order integer NOT NULL DEFAULT 0
);

CREATE TABLE snippets.questions (
    language text NOT NULL REFERENCES snippets.categories(language)
        ON UPDATE CASCADE ON DELETE CASCADE,
    number integer NOT NULL CHECK (number > 0),
    topic text NOT NULL CHECK (length(btrim(topic)) > 0),
    description text NOT NULL CHECK (length(btrim(description)) > 0),
    markdown text NOT NULL CHECK (length(btrim(markdown)) > 0),
    PRIMARY KEY (language, number)
);

CREATE FUNCTION snippets.touch_catalog() RETURNS trigger
LANGUAGE plpgsql AS $$
BEGIN
    -- Serialize writers and advance even when two changes share a clock tick.
    UPDATE snippets.catalog_state
    SET updated_at = GREATEST(clock_timestamp(), updated_at + interval '1 microsecond')
    WHERE id = 1;
    IF NOT FOUND THEN
        RAISE EXCEPTION 'Snippet catalog timestamp is missing';
    END IF;
    RETURN NULL;
END;
$$;

CREATE TRIGGER categories_changed
AFTER INSERT OR UPDATE OR DELETE OR TRUNCATE ON snippets.categories
FOR EACH STATEMENT EXECUTE FUNCTION snippets.touch_catalog();

CREATE TRIGGER questions_changed
AFTER INSERT OR UPDATE OR DELETE OR TRUNCATE ON snippets.questions
FOR EACH STATEMENT EXECUTE FUNCTION snippets.touch_catalog();

COMMENT ON TABLE snippets.catalog_state IS 'Automatic catalog timestamp; do not edit or delete.';
COMMENT ON COLUMN snippets.categories.language IS 'Normalized Monaco language ID, or general.';
COMMENT ON COLUMN snippets.categories.sort_order IS 'Display order, with language as a stable tie breaker.';
COMMENT ON TABLE snippets.questions IS 'Edit through a database UI; commit category and question changes together.';

COMMIT;
