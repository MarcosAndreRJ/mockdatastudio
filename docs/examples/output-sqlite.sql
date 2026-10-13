-- ============================================================
-- Mock Data Studio 1.0.0 — script gerado
-- Projeto: Loja Demo  Perfil: mini  RunId: 5b1f0c7e-2a44-4d61-9b0e-7f3c2d1a9e80
-- Destino: sqlite 3.46   Gerado em: 2026-10-11T18:00:00Z
-- Seed: 20261011  ReferenceInstant: 2026-10-11T12:00:00-03:00
-- PlanHash: sha256:f1b377bb3ce99c8fc9586753a4bc0ac9885a09034c29f09ae5bfb7a39e4c7408
-- StructureHash: sha256:c338a413649bcae20bd9100fee716c5071d3db2cb9ee89ff4a55626f69205755
-- Reprodutibilidade: Exact
-- Segmento 001 de 001 — executar em ordem (ver manifesto)
-- Faixas de chave reservadas: categoria.id [1..3] cliente.id [1..3] pedido.id [1..3] produto.id [1..3]
-- ============================================================
PRAGMA foreign_keys = ON;
BEGIN;
-- VERIFICAÇÃO: SELECT MAX(id) FROM categoria deve ser menor que 1 (faixa [1..3] reservada).
-- VERIFICAÇÃO: SELECT MAX(id) FROM cliente deve ser menor que 1 (faixa [1..3] reservada).
-- VERIFICAÇÃO: SELECT MAX(id) FROM pedido deve ser menor que 1 (faixa [1..3] reservada).
-- VERIFICAÇÃO: SELECT MAX(id) FROM produto deve ser menor que 1 (faixa [1..3] reservada).
-- Tabela: categoria (3 linhas)
INSERT INTO "categoria" ("id","nome","pai_id") VALUES
(1,'Eletrônicos',NULL),
(2,'Informática',1),
(3,'Papelaria',NULL);
-- Tabela: cliente (3 linhas)
INSERT INTO "cliente" ("id","nome","cpf","email","zap","nro_cep","cidade","uf","nascimento","ativo","criado_em") VALUES
(1,'Ana Exemplo Souza','52998224725','ana@exemplo.com.br','(31) 99812-4410','30130010','Belo Horizonte','MG','1987-04-12',1,'2026-03-02 10:15:33.000'),
(2,'Bruno Fictício Lima','11144477735',NULL,'(11) 98765-0021','01310100','São Paulo','SP','1979-11-30',1,'2026-07-19 08:02:11.000'),
(3,'Carla D''Ávila Teste','39053344705','carla@teste.dev',NULL,'80010000','Curitiba','PR','2001-01-05',0,'2025-12-24 23:59:59.000');
-- Tabela: pedido (3 linhas)
INSERT INTO "pedido" ("id","cliente_id","data_pedido","status","total") VALUES
(1,1,'2026-09-30 14:22:05.000','PAGO',0),
(2,1,'2026-10-02 09:11:47.000','ENVIADO',0),
(3,3,'2026-08-15 19:40:00.000','ABERTO',0);
-- Tabela: produto (3 linhas)
INSERT INTO "produto" ("id","sku","descricao","categoria_id","preco","estoque","atributos") VALUES
(1,'KQZ-48213','Notebook Modelo Exemplo 14"',2,4299.90,12,'{}'),
(2,'BHA-00917','Caderno Fictício 200 folhas',3,32.50,340,'{}'),
(3,'TRM-55102','Fone de Ouvido Teste',1,149.00,0,'{}');
-- Tabela: item_pedido (4 linhas)
INSERT INTO "item_pedido" ("pedido_id","item_seq","produto_id","quantidade","preco_unit") VALUES
(1,1,1,1,4299.90),
(1,2,3,2,149.00),
(2,1,2,5,32.50),
(3,1,3,1,149.00);
-- Tabela: entrega (2 linhas)
INSERT INTO "entrega" ("id","pedido_id","item_seq","previsao","cep_entrega") VALUES
('3f2504e0-4f89-41d3-9a0c-0305e82c3301',1,1,'2026-10-14','30130010'),
('a8098c1a-f86e-41da-b7c5-2b1f3e0d7c22',2,1,NULL,'01310100');
COMMIT;
