-- Fixture de referência: Loja (SQLite)
PRAGMA foreign_keys = ON;

CREATE TABLE categorias (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    nome TEXT NOT NULL,
    descricao TEXT
);

CREATE TABLE clientes (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    nome TEXT NOT NULL,
    email TEXT NOT NULL UNIQUE,
    cpf TEXT NOT NULL UNIQUE,
    data_cadastro TEXT NOT NULL,
    ativo INTEGER NOT NULL DEFAULT 1
);

CREATE TABLE produtos (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    categoria_id INTEGER NOT NULL,
    nome TEXT NOT NULL,
    preco REAL NOT NULL,
    estoque INTEGER NOT NULL DEFAULT 0,
    FOREIGN KEY (categoria_id) REFERENCES categorias(id)
);

CREATE TABLE pedidos (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    cliente_id INTEGER NOT NULL,
    data_pedido TEXT NOT NULL,
    valor_total REAL NOT NULL,
    status TEXT NOT NULL,
    FOREIGN KEY (cliente_id) REFERENCES clientes(id)
);

CREATE TABLE itens_pedido (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    pedido_id INTEGER NOT NULL,
    produto_id INTEGER NOT NULL,
    quantidade INTEGER NOT NULL,
    preco_unitario REAL NOT NULL,
    FOREIGN KEY (pedido_id) REFERENCES pedidos(id),
    FOREIGN KEY (produto_id) REFERENCES produtos(id)
);
