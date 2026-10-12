-- Fixture de referência: Loja (MySQL)
CREATE TABLE categorias (
    id INT AUTO_INCREMENT PRIMARY KEY,
    nome VARCHAR(100) NOT NULL,
    descricao VARCHAR(255)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE clientes (
    id INT AUTO_INCREMENT PRIMARY KEY,
    nome VARCHAR(150) NOT NULL,
    email VARCHAR(150) NOT NULL UNIQUE,
    cpf VARCHAR(14) NOT NULL UNIQUE,
    data_cadastro DATETIME NOT NULL,
    ativo TINYINT(1) NOT NULL DEFAULT 1
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE produtos (
    id INT AUTO_INCREMENT PRIMARY KEY,
    categoria_id INT NOT NULL,
    nome VARCHAR(150) NOT NULL,
    preco DECIMAL(10,2) NOT NULL,
    estoque INT NOT NULL DEFAULT 0,
    CONSTRAINT fk_produtos_categoria FOREIGN KEY (categoria_id) REFERENCES categorias(id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE pedidos (
    id INT AUTO_INCREMENT PRIMARY KEY,
    cliente_id INT NOT NULL,
    data_pedido DATETIME NOT NULL,
    valor_total DECIMAL(12,2) NOT NULL,
    status VARCHAR(50) NOT NULL,
    CONSTRAINT fk_pedidos_cliente FOREIGN KEY (cliente_id) REFERENCES clientes(id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE itens_pedido (
    id INT AUTO_INCREMENT PRIMARY KEY,
    pedido_id INT NOT NULL,
    produto_id INT NOT NULL,
    quantidade INT NOT NULL,
    preco_unitario DECIMAL(10,2) NOT NULL,
    CONSTRAINT fk_itens_pedido FOREIGN KEY (pedido_id) REFERENCES pedidos(id),
    CONSTRAINT fk_itens_produto FOREIGN KEY (produto_id) REFERENCES produtos(id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
