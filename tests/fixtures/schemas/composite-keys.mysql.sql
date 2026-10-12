-- Fixture: Composite Keys (MySQL)
CREATE TABLE filiais (
    empresa_id INT NOT NULL,
    filial_id INT NOT NULL,
    nome VARCHAR(100) NOT NULL,
    cidade VARCHAR(100) NOT NULL,
    PRIMARY KEY (empresa_id, filial_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE departamentos (
    id INT AUTO_INCREMENT PRIMARY KEY,
    empresa_id INT NOT NULL,
    filial_id INT NOT NULL,
    nome VARCHAR(100) NOT NULL,
    CONSTRAINT fk_departamento_filial FOREIGN KEY (empresa_id, filial_id) REFERENCES filiais(empresa_id, filial_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
