-- Fixture: Cycles (PostgreSQL)
-- Ciclo relacional quebrável por inserção com nulo (NullThenUpdate) e suporte a deferrable
CREATE TABLE times (
    id INT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    nome VARCHAR(100) NOT NULL,
    lider_id INT
);

CREATE TABLE usuarios (
    id INT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    nome VARCHAR(150) NOT NULL,
    time_id INT,
    CONSTRAINT fk_usuarios_time FOREIGN KEY (time_id) REFERENCES times(id) DEFERRABLE INITIALLY DEFERRED
);

ALTER TABLE times
    ADD CONSTRAINT fk_times_lider FOREIGN KEY (lider_id) REFERENCES usuarios(id) DEFERRABLE INITIALLY DEFERRED;
