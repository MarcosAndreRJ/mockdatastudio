-- Fixture: Composite Keys (SQLite)
PRAGMA foreign_keys = ON;

CREATE TABLE filiais (
    empresa_id INTEGER NOT NULL,
    filial_id INTEGER NOT NULL,
    nome TEXT NOT NULL,
    cidade TEXT NOT NULL,
    PRIMARY KEY (empresa_id, filial_id)
);

CREATE TABLE departamentos (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    empresa_id INTEGER NOT NULL,
    filial_id INTEGER NOT NULL,
    nome TEXT NOT NULL,
    FOREIGN KEY (empresa_id, filial_id) REFERENCES filiais(empresa_id, filial_id)
);
