-- Fixture: Self-Reference (MySQL)
CREATE TABLE categorias_arvore (
    id INT AUTO_INCREMENT PRIMARY KEY,
    nome VARCHAR(100) NOT NULL,
    parent_id INT,
    CONSTRAINT fk_categoria_pai FOREIGN KEY (parent_id) REFERENCES categorias_arvore(id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
