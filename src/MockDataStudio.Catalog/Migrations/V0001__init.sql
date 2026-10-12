CREATE TABLE schema_version (
    version      INTEGER PRIMARY KEY,
    script_name  TEXT    NOT NULL,
    checksum     TEXT    NOT NULL,              -- sha256 do script aplicado
    applied_at   TEXT    NOT NULL
);

CREATE TABLE template_pack (
    id           INTEGER PRIMARY KEY,
    code         TEXT    NOT NULL UNIQUE CHECK (length(code) BETWEEN 2 AND 64 AND code = upper(code)),
    name         TEXT    NOT NULL,
    version      TEXT    NOT NULL DEFAULT '1.0.0',
    author       TEXT,
    license      TEXT,
    is_builtin   INTEGER NOT NULL DEFAULT 0 CHECK (is_builtin IN (0,1)),
    created_at   TEXT    NOT NULL,
    updated_at   TEXT    NOT NULL
);

CREATE TABLE template (
    id            INTEGER PRIMARY KEY,
    code          TEXT    NOT NULL UNIQUE CHECK (length(code) BETWEEN 2 AND 64 AND code = upper(code)
                                                 AND code NOT GLOB '*[^A-Z0-9_]*' AND substr(code,1,1) GLOB '[A-Z]'),
    name          TEXT    NOT NULL,
    kind          TEXT    NOT NULL CHECK (kind IN ('scalar','composite')),
    logical_type  TEXT    CHECK (logical_type IN ('boolean','integer','decimal','float','string','text','binary','date',
                                   'time','datetime','datetimetz','uuid','json','enum','interval','other')),
    locale        TEXT    NOT NULL DEFAULT 'pt-BR',
    description   TEXT,
    tags          TEXT    NOT NULL DEFAULT '[]' CHECK (json_valid(tags) AND json_type(tags) = 'array'),
    pack_id       INTEGER NOT NULL REFERENCES template_pack(id) ON DELETE RESTRICT,
    is_builtin    INTEGER NOT NULL DEFAULT 0 CHECK (is_builtin IN (0,1)),
    content_hash  TEXT,                              -- recalculado após alterações de valores
    created_at    TEXT    NOT NULL,
    updated_at    TEXT    NOT NULL,
    CHECK ((kind = 'scalar' AND logical_type IS NOT NULL) OR (kind = 'composite' AND logical_type IS NULL))
);

CREATE TABLE template_field (
    id           INTEGER PRIMARY KEY,
    template_id  INTEGER NOT NULL REFERENCES template(id) ON DELETE CASCADE,
    field_code   TEXT    NOT NULL CHECK (length(field_code) BETWEEN 1 AND 64 AND field_code = lower(field_code)
                                         AND field_code NOT GLOB '*[^a-z0-9_]*' AND substr(field_code,1,1) GLOB '[a-z]'),
    logical_type TEXT    NOT NULL,
    ordinal      INTEGER NOT NULL,
    max_length   INTEGER CHECK (max_length IS NULL OR max_length > 0),
    required     INTEGER NOT NULL DEFAULT 1 CHECK (required IN (0,1)),
    UNIQUE (template_id, field_code),
    UNIQUE (template_id, ordinal)
);

CREATE TABLE template_alias (
    id                     INTEGER PRIMARY KEY,
    template_id            INTEGER NOT NULL REFERENCES template(id) ON DELETE CASCADE,
    field_id               INTEGER REFERENCES template_field(id) ON DELETE CASCADE,
    alias                  TEXT    NOT NULL CHECK (length(trim(alias)) > 0),
    normalized_alias       TEXT    NOT NULL,       -- NormalizedName.Compact (14 §2)
    weight                 REAL    NOT NULL DEFAULT 1.0 CHECK (weight > 0 AND weight <= 2),
    context_table_pattern  TEXT,                   -- glob sobre nome normalizado da tabela, ex. '*ENDERECO*'
    source                 TEXT    NOT NULL DEFAULT 'user' CHECK (source IN ('builtin','user','import','learned')),
    created_at             TEXT    NOT NULL
);
CREATE UNIQUE INDEX ux_template_alias ON template_alias(template_id, ifnull(field_id, 0), normalized_alias);
CREATE INDEX ix_template_alias_norm ON template_alias(normalized_alias);

CREATE TABLE template_value (
    id           INTEGER PRIMARY KEY,
    template_id  INTEGER NOT NULL REFERENCES template(id) ON DELETE CASCADE,
    value        TEXT    NOT NULL,
    weight       REAL    NOT NULL DEFAULT 1.0 CHECK (weight > 0),
    UNIQUE (template_id, value)
);

CREATE TABLE template_record (
    id           INTEGER PRIMARY KEY,
    template_id  INTEGER NOT NULL REFERENCES template(id) ON DELETE CASCADE,
    record_key   TEXT    NOT NULL,
    data         TEXT    NOT NULL CHECK (json_valid(data) AND json_type(data) = 'object'),
    weight       REAL    NOT NULL DEFAULT 1.0 CHECK (weight > 0),
    UNIQUE (template_id, record_key)
);

CREATE TABLE generator_preset (
    id                 INTEGER PRIMARY KEY,
    code               TEXT    NOT NULL UNIQUE CHECK (code = upper(code) AND length(code) BETWEEN 2 AND 64),
    name               TEXT    NOT NULL,
    generator_id       TEXT    NOT NULL,           -- ex. 'core.date.range'
    generator_version  INTEGER NOT NULL,
    parameters         TEXT    NOT NULL DEFAULT '{}' CHECK (json_valid(parameters) AND json_type(parameters) = 'object'),
    description        TEXT,
    is_builtin         INTEGER NOT NULL DEFAULT 0 CHECK (is_builtin IN (0,1)),
    created_at         TEXT    NOT NULL,
    updated_at         TEXT    NOT NULL
);

CREATE TABLE mapping_rule (
    id              INTEGER PRIMARY KEY,
    column_pattern  TEXT    NOT NULL,              -- glob sobre NormalizedName.Compact, ex. 'DTNASC*'
    table_pattern   TEXT,                          -- glob sobre nome normalizado da tabela
    engine          TEXT    CHECK (engine IS NULL OR engine IN ('sqlite','mysql','postgresql','firebird')),
    target_kind     TEXT    NOT NULL CHECK (target_kind IN ('template','template_field','preset','generator')),
    target_ref      TEXT    NOT NULL,              -- 'ENDERECO.cep', 'TELEFONE', 'DATA_NASC_ADULTO', 'core.uuid'
    parameters      TEXT    CHECK (parameters IS NULL OR json_valid(parameters)),
    scope           TEXT    NOT NULL DEFAULT 'global' CHECK (scope IN ('global','engine')),
    priority        INTEGER NOT NULL DEFAULT 100,
    source          TEXT    NOT NULL CHECK (source IN ('builtin','user','learned')),
    enabled         INTEGER NOT NULL DEFAULT 1 CHECK (enabled IN (0,1)),
    hit_count       INTEGER NOT NULL DEFAULT 0,
    created_at      TEXT    NOT NULL,
    updated_at      TEXT    NOT NULL
);
-- UNIQUE de tabela no SQLite não aceita expressões → índice único de expressão
CREATE UNIQUE INDEX ux_mapping_rule ON mapping_rule(column_pattern, ifnull(table_pattern,''), ifnull(engine,''), target_kind, target_ref);
-- Regras de escopo 'project' (RuleScope.Project) vivem no arquivo .mdsproj, não no catálogo.

CREATE TABLE mapping_feedback (                  -- "histórico de mapeamentos" usado como evidência (14 §5)
    column_compact  TEXT    NOT NULL,
    target_kind     TEXT    NOT NULL,
    target_ref      TEXT    NOT NULL,
    accepted_count  INTEGER NOT NULL DEFAULT 0,
    rejected_count  INTEGER NOT NULL DEFAULT 0,
    last_at         TEXT    NOT NULL,
    PRIMARY KEY (column_compact, target_kind, target_ref)
) WITHOUT ROWID;

CREATE TABLE connection_profile (
    id               TEXT    PRIMARY KEY,          -- GUID
    name             TEXT    NOT NULL UNIQUE,
    engine           TEXT    NOT NULL CHECK (engine IN ('sqlite','mysql','postgresql','firebird')),
    settings         TEXT    NOT NULL CHECK (json_valid(settings)),   -- ConnectionSettings SEM senha
    environment      TEXT    NOT NULL CHECK (environment IN ('development','test','staging','production','unknown')),
    color_hex        TEXT    CHECK (color_hex IS NULL OR color_hex GLOB '#[0-9A-Fa-f][0-9A-Fa-f][0-9A-Fa-f][0-9A-Fa-f][0-9A-Fa-f][0-9A-Fa-f]'),
    is_readonly      INTEGER NOT NULL DEFAULT 0 CHECK (is_readonly IN (0,1)),
    prompt_password  INTEGER NOT NULL DEFAULT 0 CHECK (prompt_password IN (0,1)),
    secret           BLOB,                         -- DPAPI CurrentUser (23 §3); NULL se prompt_password
    notes            TEXT,
    created_at       TEXT    NOT NULL,
    updated_at       TEXT    NOT NULL,
    last_used_at     TEXT
);

CREATE TABLE query_history (
    id              INTEGER PRIMARY KEY,
    profile_id      TEXT    REFERENCES connection_profile(id) ON DELETE SET NULL,
    executed_at     TEXT    NOT NULL,
    sql_text        TEXT    NOT NULL,              -- truncado em 64 KB
    statement_kind  TEXT    NOT NULL,
    duration_ms     INTEGER NOT NULL,
    rows_affected   INTEGER,
    success         INTEGER NOT NULL CHECK (success IN (0,1)),
    error_code      TEXT
);
CREATE INDEX ix_query_history_profile ON query_history(profile_id, executed_at DESC);

CREATE TABLE generation_run (
    id                  TEXT    PRIMARY KEY,       -- RunId GUID
    project_id          TEXT    NOT NULL,
    project_path        TEXT,
    profile_name        TEXT    NOT NULL,
    started_at          TEXT    NOT NULL,
    finished_at         TEXT,
    status              TEXT    NOT NULL CHECK (status IN ('running','completed','failed','cancelled','interrupted')),
    seed                TEXT    NOT NULL,          -- ulong em texto decimal
    plan_hash           TEXT    NOT NULL,
    structure_hash      TEXT    NOT NULL,
    output_kind         TEXT    NOT NULL CHECK (output_kind IN ('sqlScript','directInsert','csv','jsonLines')),
    target_profile_id   TEXT    REFERENCES connection_profile(id) ON DELETE SET NULL,
    target_fingerprint  TEXT,
    rows_total          INTEGER NOT NULL DEFAULT 0,
    report              TEXT    CHECK (report IS NULL OR json_valid(report)),  -- RunOutcome serializado
    cleanup_path        TEXT
);
CREATE INDEX ix_generation_run_project ON generation_run(project_id, started_at DESC);

CREATE TABLE app_setting (
    key    TEXT PRIMARY KEY,
    value  TEXT NOT NULL
) WITHOUT ROWID;

CREATE TABLE recent_project (
    path            TEXT PRIMARY KEY,
    name            TEXT NOT NULL,
    last_opened_at  TEXT NOT NULL
) WITHOUT ROWID;

-- Gatilhos de proteção de itens embutidos
CREATE TRIGGER trg_template_builtin_protect_upd BEFORE UPDATE OF code, kind, logical_type ON template
WHEN OLD.is_builtin = 1
BEGIN SELECT RAISE(ABORT, 'CAT-BUILTIN-READONLY'); END;

CREATE TRIGGER trg_template_builtin_protect_del BEFORE DELETE ON template
WHEN OLD.is_builtin = 1
BEGIN SELECT RAISE(ABORT, 'CAT-BUILTIN-READONLY'); END;
