-- Fixture: Cycles (MySQL)
-- Ciclo relacional quebrável por inserção com nulo (NullThenUpdate)
CREATE TABLE times (
    id INT AUTO_INCREMENT PRIMARY KEY,
    nome VARCHAR(100) NOT NULL,
    lider_id INT
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE usuarios (
    id INT AUTO_INCREMENT PRIMARY KEY,
    nome VARCHAR(150) NOT NULL,
    time_id INT,
    CONSTRAINT fk_usuarios_time FOREIGN KEY (time_id) REFERENCES times(id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

ALTER TABLE times
    ADD CONSTRAINT fk_times_lider FOREIGN KEY (lider_id) REFERENCES usuarios(id);
