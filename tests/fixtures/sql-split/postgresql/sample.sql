-- Fixture PostgreSQL para Splitter
CREATE TABLE itens (
    id INT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    detalhes TEXT
);

CREATE OR REPLACE FUNCTION fn_processar_item(p_id INT)
RETURNS VOID AS $$
BEGIN
    UPDATE itens SET detalhes = 'Processado;' WHERE id = p_id;
    INSERT INTO itens (detalhes) VALUES ('Log: ok;');
END;
$$ LANGUAGE plpgsql;

SELECT * FROM itens WHERE id = 1;
