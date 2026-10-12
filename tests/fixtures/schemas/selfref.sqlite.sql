-- Fixture: Self-Reference (SQLite)
PRAGMA foreign_keys = ON;

CREATE TABLE categorias_arvore (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    nome TEXT NOT NULL,
    parent_id INTEGER,
    FOREIGN KEY (parent_id) REFERENCES categorias_arvore(id)
);
