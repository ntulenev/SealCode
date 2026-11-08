-- Apply once after 001_create_snippets.sql. Existing Markdown remains the first example.
BEGIN;

ALTER TABLE snippets.questions
    ADD COLUMN snippet_title text NOT NULL DEFAULT 'Original example'
    CHECK (length(btrim(snippet_title)) > 0);

CREATE TABLE snippets.question_steps (
    language text NOT NULL,
    question_number integer NOT NULL,
    number integer NOT NULL CHECK (number >= 2),
    title text NOT NULL CHECK (length(btrim(title)) > 0),
    markdown text NOT NULL CHECK (length(btrim(markdown)) > 0),
    PRIMARY KEY (language, question_number, number),
    FOREIGN KEY (language, question_number) REFERENCES snippets.questions(language, number)
        ON UPDATE CASCADE ON DELETE CASCADE
);

CREATE TRIGGER question_steps_changed
AFTER INSERT OR UPDATE OR DELETE OR TRUNCATE ON snippets.question_steps
FOR EACH STATEMENT EXECUTE FUNCTION snippets.touch_catalog();

COMMENT ON COLUMN snippets.questions.snippet_title IS 'Title of example 1; its text remains in questions.markdown.';
COMMENT ON TABLE snippets.question_steps IS 'Follow-up examples 2, 3, ... for a question. Edit the progression in one transaction.';

UPDATE snippets.catalog_state
SET updated_at = GREATEST(clock_timestamp(), updated_at + interval '1 microsecond')
WHERE id = 1;

COMMIT;
