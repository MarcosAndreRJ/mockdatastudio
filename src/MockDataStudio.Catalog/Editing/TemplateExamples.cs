using MockDataStudio.Core.Schema;

namespace MockDataStudio.Catalog.Editing;

// Exemplos prontos para a tela Templates (ajuda para criar templates). Cada exemplo passa pelas mesmas validações do catálogo
// e tem teste que o salva/analisa de verdade. Os códigos começam com EXEMPLO_ para não colidir com templates do usuário.
public static class TemplateExamples
{
    // Escalar: uma lista de valores. Preenche o formulário de template escalar.
    public const string ScalarCode = "EXEMPLO_STATUS_PEDIDO";

    public static ScalarTemplateForm Scalar() => new(
        ScalarCode, "Exemplo: status do pedido", "Situações possíveis de um pedido. Um valor por linha.",
        LogicalType.String,
        ["Aberto", "Pago", "Enviado", "Entregue", "Cancelado"],
        ["status", "situacao"]);

    // Composto: campos e registros. Preenche o formulário de template composto no formato de CompositeTemplateEditor.
    public const string CompositeCode = "EXEMPLO_CLIENTE";
    public const string CompositeName = "Exemplo: cliente";
    public const string CompositeDescription = "Clientes com código, nome e UF. A última coluna (UF) é opcional.";
    public const string CompositeFields = "codigo | string | 10 | sim\nnome | string | 120 | sim\nuf | string | 2 | não";
    public const string CompositeRecords = "CLI001 | Ana Souza | SP\nCLI002 | Bruno Lima | RJ\nCLI003 | Carla Dias |";
    public const string CompositeAliases = "cliente";

    // Script SQL para a aba de importação (formato de TemplateInstructions.ForAi).
    public const string ScriptCode = "EXEMPLO_CANAL_VENDA";
    public const string Script = """
        INSERT INTO template (code, name, kind, logical_type, locale, description, tags) VALUES ('EXEMPLO_CANAL_VENDA', 'Exemplo: canal de venda', 'scalar', 'string', 'pt-BR', 'Canais de venda de um pedido.', '["exemplo"]');
        INSERT INTO template_value (template_code, value, weight) VALUES ('EXEMPLO_CANAL_VENDA', 'Loja física', 1.0), ('EXEMPLO_CANAL_VENDA', 'Site', 1.0), ('EXEMPLO_CANAL_VENDA', 'Marketplace', 1.0);
        INSERT INTO template_alias (template_code, field_code, alias, weight) VALUES ('EXEMPLO_CANAL_VENDA', NULL, 'canal', 1.0);
        """;

    // Pack JSON (formato de pack-core-ptbr.json) com um composto e um escalar.
    public const string PackJson = """
        {
          "code": "EXEMPLO_PACK",
          "name": "Exemplo de pack",
          "templates": [
            {
              "code": "EXEMPLO_PRODUTO",
              "name": "Exemplo: produto",
              "kind": "composite",
              "description": "Produtos com SKU, nome e preço.",
              "tags": ["exemplo"],
              "fields": [
                { "code": "sku", "logicalType": "string", "ordinal": 1, "maxLength": 12 },
                { "code": "nome", "logicalType": "string", "ordinal": 2, "maxLength": 80 },
                { "code": "preco", "logicalType": "decimal", "ordinal": 3 }
              ],
              "aliases": ["produto", { "alias": "descricao", "field": "nome" }],
              "records": [
                { "key": "1", "data": { "sku": "PRD-001", "nome": "Caneta azul", "preco": "3.50" } },
                { "key": "2", "data": { "sku": "PRD-002", "nome": "Caderno 96 folhas", "preco": "19.90" } }
              ]
            },
            {
              "code": "EXEMPLO_PRIORIDADE",
              "name": "Exemplo: prioridade",
              "kind": "scalar",
              "logicalType": "string",
              "description": "Prioridades de atendimento.",
              "tags": ["exemplo"],
              "values": [ { "value": "Baixa" }, { "value": "Média" }, { "value": "Alta" } ],
              "aliases": ["prioridade"]
            }
          ]
        }
        """;
}
