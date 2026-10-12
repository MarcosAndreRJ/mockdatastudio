-- Fixture de referência: Loja (PostgreSQL)
CREATE TABLE categorias (
    id INT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    nome VARCHAR(100) NOT NULL,
    descricao VARCHAR(255)
);

CREATE TABLE clientes (
    id INT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    nome VARCHAR(150) NOT NULL,
    email VARCHAR(150) NOT NULL UNIQUE,
    cpf VARCHAR(14) NOT NULL UNIQUE,
    data_cadastro TIMESTAMP NOT NULL,
    ativo BOOLEAN NOT NULL DEFAULT TRUE
);

CREATE TABLE produtos (
    id INT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    categoria_id INT NOT NULL,
    nome VARCHAR(150) NOT NULL,
    preco NUMERIC(10,2) NOT NULL,
    estoque INT NOT NULL DEFAULT 0,
    CONSTRAINT fk_produtos_categoria FOREIGN KEY (categoria_id) REFERENCES categorias(id)
);

CREATE TABLE pedidos (
    id INT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    cliente_id INT NOT NULL,
    data_pedido TIMESTAMP NOT NULL,
    valor_total NUMERIC(12,2) NOT NULL,
    status VARCHAR(50) NOT NULL,
    CONSTRAINT fk_pedidos_cliente FOREIGN KEY (cliente_id) REFERENCES clientes(id)
);

CREATE TABLE itens_pedido (
    id INT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    pedido_id INT NOT NULL,
    produto_id INT NOT NULL,
    quantidade INT NOT NULL,
    preco_unitario NUMERIC(10,2) NOT NULL,
    CONSTRAINT fk_itens_pedido FOREIGN KEY (pedido_id) REFERENCES pedidos(id),
    CONSTRAINT fk_itens_produto FOREIGN KEY (produto_id) REFERENCES produtos(id)
);
