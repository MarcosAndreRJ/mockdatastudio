-- Fixture: Cycles (SQLite)
-- Ciclo relacional quebrável por inserção com nulo (NullThenUpdate)
PRAGMA foreign_keys = ON;

CREATE TABLE times (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    nome TEXT NOT NULL,
    lider_id INTEGER,
    FOREIGN KEY (lider_id) REFERENCES usuarios(id)
);

CREATE TABLE usuarios (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    nome TEXT NOT NULL,
    time_id INTEGER,
    FOREIGN KEY (time_id) REFERENCES times(id)
);
