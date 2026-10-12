CREATE TABLE template (
    code          TEXT NOT NULL,
    name          TEXT,
    kind          TEXT DEFAULT 'scalar',        -- 'scalar' | 'composite'
    logical_type  TEXT DEFAULT 'string',        -- obrigatório p/ scalar; ignorado p/ composite
    locale        TEXT DEFAULT 'pt-BR',
    description   TEXT,
    tags          TEXT                          -- JSON array opcional, ex. '["pessoa","br"]'
);
CREATE TABLE template_field (
    template_code TEXT NOT NULL,
    field_code    TEXT NOT NULL,
    logical_type  TEXT DEFAULT 'string',
    ordinal       INTEGER,                      -- nulo = ordem de inserção
    max_length    INTEGER,
    required      INTEGER DEFAULT 1
);
CREATE TABLE template_alias (
    template_code          TEXT NOT NULL,
    field_code             TEXT,                -- nulo = alias do template inteiro
    alias                  TEXT NOT NULL,
    weight                 REAL DEFAULT 1.0,
    context_table_pattern  TEXT
);
CREATE TABLE template_value (
    template_code TEXT NOT NULL,
    value         TEXT NOT NULL,
    weight        REAL DEFAULT 1.0
);
CREATE TABLE template_record (
    template_code TEXT NOT NULL,
    record_key    TEXT,                         -- nulo = gerado (sha256 de data, 16 hex)
    data          TEXT NOT NULL,                -- objeto JSON {campo: valor}
    weight        REAL DEFAULT 1.0
);
CREATE TABLE mapping_rule (
    column_pattern TEXT NOT NULL,
    table_pattern  TEXT,
    engine         TEXT,
    target_kind    TEXT NOT NULL,
    target_ref     TEXT NOT NULL,
    parameters     TEXT,
    priority       INTEGER DEFAULT 100
);
CREATE TABLE generator_preset (
    code              TEXT NOT NULL,
    name              TEXT,
    generator_id      TEXT NOT NULL,
    generator_version INTEGER,
    parameters        TEXT DEFAULT '{}',
    description       TEXT
);
