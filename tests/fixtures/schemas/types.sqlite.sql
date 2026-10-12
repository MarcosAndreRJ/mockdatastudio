-- Fixture de tipos para T-PRV-SQLITE-003 (mapeamento tipo nativo -> LogicalType).
CREATE TABLE tipos (
    c_bool       BOOLEAN NOT NULL,
    c_int        INTEGER,
    c_smallint   SMALLINT,
    c_bigint     BIGINT,
    c_dec        NUMERIC(12,2),
    c_real       REAL,
    c_varchar    VARCHAR(40),
    c_text       TEXT,
    c_blob       BLOB,
    c_date       DATE,
    c_time       TIME,
    c_datetime   DATETIME,
    c_timestamp  TIMESTAMP,
    c_uuid       UUID,
    c_json       JSON
);
