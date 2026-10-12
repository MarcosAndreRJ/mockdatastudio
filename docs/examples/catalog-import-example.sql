-- =====================================================================
-- Mock Data Studio — exemplo de script de importação para o catálogo
-- Executado no SCHEMA DE STAGING (docs/09-internal-database.md §4),
-- nunca diretamente no catalog.db. Somente INSERT (UPDATE/DELETE em
-- tabelas de staging são tolerados; DDL/ATTACH/PRAGMA são rejeitados).
-- Todos os dados abaixo são FICTÍCIOS.
-- =====================================================================

-- 1) Template escalar TELEFONE com aliases
INSERT INTO template (code, name, kind, logical_type, tags, description)
VALUES ('TELEFONE', 'Telefone', 'scalar', 'string', '["contato","br"]', 'Telefones fictícios no formato (DD) NNNNN-NNNN');

INSERT INTO template_alias (template_code, alias) VALUES
  ('TELEFONE', 'TELEFONE'), ('TELEFONE', 'FONE'), ('TELEFONE', 'TEL'), ('TELEFONE', 'NRO_TEL'),
  ('TELEFONE', 'CELULAR'), ('TELEFONE', 'ZAP'), ('TELEFONE', 'WHATSAPP'), ('TELEFONE', 'FAX');

INSERT INTO template_value (template_code, value, weight) VALUES
  ('TELEFONE', '(11) 98765-4321', 1), ('TELEFONE', '(21) 99876-1234', 1),
  ('TELEFONE', '(31) 3222-1100', 0.5), ('TELEFONE', '(41) 99123-4567', 1),
  ('TELEFONE', '(51) 98111-2233', 1), ('TELEFONE', '(61) 3344-5566', 0.5);

-- 2) Template escalar com lista ponderada
INSERT INTO template (code, name, logical_type) VALUES ('STATUS_PEDIDO', 'Status de pedido', 'string');
INSERT INTO template_alias (template_code, alias) VALUES ('STATUS_PEDIDO', 'STATUS'), ('STATUS_PEDIDO', 'SITUACAO');
INSERT INTO template_value (template_code, value, weight) VALUES
  ('STATUS_PEDIDO', 'ABERTO', 3), ('STATUS_PEDIDO', 'PAGO', 5),
  ('STATUS_PEDIDO', 'ENVIADO', 4), ('STATUS_PEDIDO', 'CANCELADO', 1);

-- 3) Template composto ENDERECO (campos + aliases de campo + registros coerentes)
INSERT INTO template (code, name, kind, tags) VALUES ('ENDERECO', 'Endereço completo', 'composite', '["endereco","br"]');

INSERT INTO template_field (template_code, field_code, logical_type, ordinal, max_length) VALUES
  ('ENDERECO', 'cep',        'string', 1, 8),
  ('ENDERECO', 'logradouro', 'string', 2, 120),
  ('ENDERECO', 'bairro',     'string', 3, 60),
  ('ENDERECO', 'cidade',     'string', 4, 60),
  ('ENDERECO', 'uf',         'string', 5, 2);

INSERT INTO template_alias (template_code, field_code, alias, weight, context_table_pattern) VALUES
  ('ENDERECO', 'cep',        'CEP',         1.0, NULL),
  ('ENDERECO', 'cep',        'NRO_CEP',     1.0, NULL),
  ('ENDERECO', 'cep',        'NCEP',        1.0, NULL),
  ('ENDERECO', 'cep',        'NROCEP',      1.0, NULL),
  ('ENDERECO', 'cep',        'COD_POSTAL',  1.0, NULL),
  ('ENDERECO', 'logradouro', 'LOGRADOURO',  1.0, NULL),
  ('ENDERECO', 'logradouro', 'RUA',         1.0, NULL),
  ('ENDERECO', 'logradouro', 'ENDERECO',    0.8, NULL),
  ('ENDERECO', 'bairro',     'BAIRRO',      1.0, NULL),
  ('ENDERECO', 'cidade',     'CIDADE',      1.0, NULL),
  ('ENDERECO', 'cidade',     'MUNICIPIO',   1.0, NULL),
  ('ENDERECO', 'uf',         'UF',          1.0, NULL),
  ('ENDERECO', 'uf',         'ESTADO',      0.9, '*ENDERECO*|*CLIENTE*|*FORNECEDOR*');

INSERT INTO template_record (template_code, data) VALUES
  ('ENDERECO', '{"cep":"01310100","logradouro":"Avenida Exemplo Paulista","bairro":"Bela Vista","cidade":"São Paulo","uf":"SP"}'),
  ('ENDERECO', '{"cep":"30130010","logradouro":"Rua dos Exemplos","bairro":"Centro","cidade":"Belo Horizonte","uf":"MG"}'),
  ('ENDERECO', '{"cep":"80010000","logradouro":"Rua Fictícia das Flores","bairro":"Centro","cidade":"Curitiba","uf":"PR"}'),
  ('ENDERECO', '{"cep":"40010000","logradouro":"Travessa Imaginária","bairro":"Comércio","cidade":"Salvador","uf":"BA"}'),
  ('ENDERECO', '{"cep":"90010000","logradouro":"Avenida Modelo","bairro":"Centro Histórico","cidade":"Porto Alegre","uf":"RS"}');

-- 4) Preset de gerador e regra de mapeamento
INSERT INTO generator_preset (code, name, generator_id, generator_version, parameters, description) VALUES
  ('DATA_NASC_ADULTO', 'Data de nascimento (18–80 anos)', 'core.date.relative', 1,
   '{"from":"-P80Y","to":"-P18Y","granularity":"day"}', 'Relativa ao ReferenceInstant do perfil');

INSERT INTO mapping_rule (column_pattern, table_pattern, engine, target_kind, target_ref, parameters, priority) VALUES
  ('DTNASC*', NULL, NULL, 'preset', 'DATA_NASC_ADULTO', NULL, 110),
  ('*NASCIMENTO*', NULL, NULL, 'preset', 'DATA_NASC_ADULTO', NULL, 100);
