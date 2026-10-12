-- Schema de referência "loja" (SQLite) — corresponde a examples/digest-sqlite.json
-- Usado como fixture tests/fixtures/schemas/loja.sqlite.sql (TASK-00.05).
PRAGMA foreign_keys = ON;

CREATE TABLE categoria (
    id      INTEGER PRIMARY KEY,
    nome    VARCHAR(60) NOT NULL UNIQUE,
    pai_id  INTEGER REFERENCES categoria(id) ON DELETE SET NULL
);

CREATE TABLE cliente (
    id          INTEGER PRIMARY KEY,
    nome        VARCHAR(120) NOT NULL,
    cpf         CHAR(11) NOT NULL UNIQUE,
    email       VARCHAR(200),
    zap         VARCHAR(20),
    nro_cep     CHAR(8),
    cidade      VARCHAR(80),
    uf          CHAR(2),
    nascimento  DATE,
    ativo       BOOLEAN NOT NULL DEFAULT 1,
    criado_em   DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    CHECK (length(uf) = 2)
);
CREATE UNIQUE INDEX ux_cliente_email ON cliente(email);
CREATE INDEX ix_cliente_nome ON cliente(nome);

CREATE TABLE produto (
    id            INTEGER PRIMARY KEY,
    sku           TEXT NOT NULL UNIQUE,
    descricao     TEXT NOT NULL,
    categoria_id  INTEGER NOT NULL REFERENCES categoria(id),
    preco         NUMERIC(12,2) NOT NULL CHECK (preco >= 0),
    estoque       INTEGER NOT NULL DEFAULT 0,
    atributos     JSON
);

CREATE TABLE pedido (
    id               INTEGER PRIMARY KEY,
    cliente_id       INTEGER NOT NULL REFERENCES cliente(id),
    data_pedido      DATETIME NOT NULL,
    status           TEXT NOT NULL DEFAULT 'ABERTO' CHECK (status IN ('ABERTO','PAGO','ENVIADO','CANCELADO')),
    total            NUMERIC(14,2) NOT NULL DEFAULT 0,
    total_com_frete  NUMERIC(14,2) GENERATED ALWAYS AS (total + 15.00) VIRTUAL
);

CREATE TABLE item_pedido (
    pedido_id   INTEGER NOT NULL REFERENCES pedido(id) ON DELETE CASCADE,
    item_seq    INTEGER NOT NULL,
    produto_id  INTEGER NOT NULL REFERENCES produto(id),
    quantidade  INTEGER NOT NULL CHECK (quantidade > 0),
    preco_unit  NUMERIC(12,2) NOT NULL,
    PRIMARY KEY (pedido_id, item_seq)
) WITHOUT ROWID;

CREATE TABLE entrega (
    id           TEXT PRIMARY KEY NOT NULL,
    pedido_id    INTEGER NOT NULL,
    item_seq     INTEGER NOT NULL,
    previsao     DATE,
    cep_entrega  CHAR(8) NOT NULL,
    UNIQUE (pedido_id, item_seq),
    FOREIGN KEY (pedido_id, item_seq) REFERENCES item_pedido(pedido_id, item_seq)
);

CREATE VIEW vw_pedidos_cliente AS
SELECT cliente_id, count(*) AS qtd_pedidos, sum(total) AS valor_total FROM pedido GROUP BY cliente_id;

CREATE TRIGGER trg_item_atualiza_total AFTER INSERT ON item_pedido
BEGIN
    UPDATE pedido SET total = total + NEW.quantidade * NEW.preco_unit WHERE id = NEW.pedido_id;
END;
