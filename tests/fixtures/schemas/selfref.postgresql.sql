-- Fixture: Self-Reference (PostgreSQL)
CREATE TABLE categorias_arvore (
    id INT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    nome VARCHAR(100) NOT NULL,
    parent_id INT,
    CONSTRAINT fk_categoria_pai FOREIGN KEY (parent_id) REFERENCES categorias_arvore(id)
);
