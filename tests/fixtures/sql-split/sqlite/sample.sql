-- Fixture SQLite para Splitter
PRAGMA foreign_keys = ON;

-- Criação de tabela
CREATE TABLE produtos (
    id INTEGER PRIMARY KEY,
    nome TEXT NOT NULL,
    descricao TEXT DEFAULT 'Item; com ponto e vírgula'
);

/* Comentário de bloco com ; dentro */
INSERT INTO produtos (id, nome) VALUES (1, 'Teclado');
INSERT INTO produtos (id, nome) VALUES (2, 'Mouse');

SELECT id, nome FROM produtos WHERE id = 1;
