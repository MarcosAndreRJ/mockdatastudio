-- ============================================================
-- Mock Data Studio 1.0.0 — script gerado (EXEMPLO ILUSTRATIVO, volume reduzido)
-- Projeto: Loja Demo  Perfil: mini  RunId: 5b1f0c7e-2a44-4d61-9b0e-7f3c2d1a9e81
-- Destino: postgresql 17.2   Gerado em: 2026-10-11T18:00:00Z
-- Seed: 20261011  ReferenceInstant: 2026-10-11T12:00:00-03:00
-- PlanHash: sha256:8888888888888888888888888888888888888888888888888888888888888888
-- StructureHash: sha256:4f1c0a7e2b9d3c5a6e8f0a1b2c3d4e5f60718293a4b5c6d7e8f9012345678abc
-- Reprodutibilidade: Exact
-- Segmento 1 de 1 — executar em ordem (ver manifesto)
-- Faixas de chave reservadas: categoria.id [1..3] cliente.id [101..103] produto.id [1..3] pedido.id [1..3]
-- ============================================================
SET client_encoding = 'UTF8';
SET standard_conforming_strings = on;
BEGIN;
-- Guarda (EmitGuards): falha se a faixa reservada já estiver ocupada
DO $$ BEGIN
  IF EXISTS (SELECT 1 FROM "public"."cliente" WHERE "id" BETWEEN 101 AND 103) THEN
    RAISE EXCEPTION 'MDS: faixa de chaves de public.cliente ocupada; replaneje a geração';
  END IF;
END $$;
-- Tabela: categoria (3 linhas)
INSERT INTO "public"."categoria" ("id","nome","pai_id") VALUES
(1,'Eletrônicos',NULL),
(2,'Informática',1),
(3,'Papelaria',NULL);
-- Tabela: cliente (3 linhas) — identity ALWAYS
INSERT INTO "public"."cliente" ("id","nome","cpf","email","zap","nro_cep","cidade","uf","nascimento","ativo","criado_em") OVERRIDING SYSTEM VALUE VALUES
(101,'Ana Exemplo Souza','52998224725','ana.exemplo.souza.0@exemplo.com.br','(31) 99812-4410','30130010','Belo Horizonte','MG',DATE '1987-04-12',TRUE,TIMESTAMPTZ '2026-03-02 10:15:33-03:00'),
(102,'Bruno Fictício Lima','11144477735',NULL,'(11) 98765-0021','01310100','São Paulo','SP',DATE '1979-11-30',TRUE,TIMESTAMPTZ '2026-07-19 08:02:11-03:00'),
(103,'Carla D''Ávila Teste','39053344705','carla.d.avila.teste.2@teste.dev',NULL,'80010000','Curitiba','PR',DATE '2001-01-05',FALSE,TIMESTAMPTZ '2025-12-24 23:59:59-03:00');
-- Tabela: produto (3 linhas)
INSERT INTO "public"."produto" ("id","sku","descricao","categoria_id","preco","estoque","atributos") VALUES
(1,'KQZ-48213','Notebook Modelo Exemplo 14"',2,4299.90,12,'{}'::jsonb),
(2,'BHA-00917','Caderno Fictício 200 folhas',3,32.50,340,'{}'::jsonb),
(3,'TRM-55102','Fone de Ouvido Teste',1,149.00,0,'{}'::jsonb);
-- Tabela: pedido (3 linhas)
INSERT INTO "public"."pedido" ("id","cliente_id","data_pedido","status","total") VALUES
(1,101,TIMESTAMP '2026-09-30 14:22:05','PAGO',0.00),
(2,101,TIMESTAMP '2026-10-02 09:11:47','ENVIADO',0.00),
(3,103,TIMESTAMP '2026-08-15 19:40:00','ABERTO',0.00);
-- Tabela: item_pedido (4 linhas)
INSERT INTO "public"."item_pedido" ("pedido_id","item_seq","produto_id","quantidade","preco_unit") VALUES
(1,1,1,1,4299.90),
(1,2,3,2,149.00),
(2,1,2,5,32.50),
(3,1,3,1,149.00);
-- Tabela: entrega (2 linhas)
INSERT INTO "public"."entrega" ("id","pedido_id","item_seq","previsao","cep_entrega") VALUES
('3f2504e0-4f89-41d3-9a0c-0305e82c3301'::uuid,1,1,DATE '2026-10-14','30130010'),
('a8098c1a-f86e-41da-b7c5-2b1f3e0d7c22'::uuid,2,1,NULL,'01310100');
-- Ressincronização de sequences
SELECT setval('"public"."categoria_id_seq"', 3, true);
SELECT setval('"public"."cliente_id_seq"', 103, true);
SELECT setval('"public"."produto_id_seq"', 3, true);
SELECT setval('"public"."pedido_id_seq"', 3, true);
COMMIT;
