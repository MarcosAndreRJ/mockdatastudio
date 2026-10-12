// Gera tests/fixtures/mapping-corpus/corpus-core-ptbr.csv a partir dos aliases do pack CORE_PTBR.
// ATENÇÃO: corpus SINTÉTICO de regressão. Não substitui o corpus de nomes reais anonimizados exigido por docs/14 §11;
// as métricas medidas com ele indicam consistência do pipeline, não precisão em schemas reais.
// Uso: node tests/fixtures/mapping-corpus/generate-corpus.js   (a partir da raiz do repositório)
const fs = require('fs');
const path = require('path');

const root = path.resolve(__dirname, '..', '..', '..');
const pack = JSON.parse(fs.readFileSync(path.join(root, 'src', 'MockDataStudio.Catalog', 'Seeds', 'pack-core-ptbr.json'), 'utf8'));

// Tipo nativo típico por template/campo (o motor compara tipo lógico; o comprimento precisa caber no alvo).
const typeOf = (template, field) => {
  if (template === 'ENDERECO') return field === 'cep' ? 'char(8)' : field === 'uf' ? 'char(2)' : 'varchar(120)';
  if (template === 'TELEFONE') return 'varchar(20)';
  if (template === 'UF') return 'char(2)';
  if (template === 'EMAIL_DOMINIO') return 'varchar(120)';
  return 'varchar(60)';
};

const tables = ['cliente', 'fornecedor', 'pedido', 'endereco', 'funcionario', 'empresa', 'usuario', 'contato', 'produto', 'filial', 'transportadora', 'vendedor'];
const styles = [
  (a) => a,
  (a) => a.toLowerCase(),
  (a) => a.toLowerCase().replace(/_([a-z])/g, (_, c) => c.toUpperCase()),
  (a) => 'DS_' + a,
  (a) => 'TX_' + a,
  (a) => 'NM_' + a,
  (a) => 'VL_' + a,
];

const rows = [];
for (const template of pack.templates) {
  const aliases = template.aliases || [];
  for (const alias of aliases) {
    const text = typeof alias === 'string' ? alias : alias.alias;
    const field = typeof alias === 'string' ? null : alias.field;
    for (const table of tables) {
      for (const style of styles) {
        const column = style(text);
        const ambiguous = field && field === 'uf'; // UF e ENDERECO.uf são alvos válidos para o mesmo nome (docs/14 §7: ambiguidade).
        rows.push({ table, column, type: typeOf(template.code, field), kind: ambiguous ? 'uf_either' : (field ? 'field' : 'template'), template: template.code, field: field || '' });
      }
    }
  }
}

// Negativos: nomes sem correspondência no pack. Esperado: nenhum alvo com confiança High.
const negatives = [
  'SALDO_ANTERIOR', 'CODIGO_INTERNO', 'OBSERVACAO_LOG', 'FLAG_PROCESSO', 'VERSAO_REGISTRO', 'HASH_ARQUIVO',
  'PRIORIDADE_FILA', 'LIMITE_CREDITO', 'TAXA_JUROS', 'ORDEM_EXIBICAO', 'CONTADOR_TENTATIVAS', 'ULTIMA_SINCRONIA',
  'PESO_BRUTO', 'ALTURA_EMBALAGEM', 'NUMERO_LOTE', 'DATA_VALIDADE_LOTE', 'PONTOS_FIDELIDADE', 'ALIQUOTA_ICMS',
  'TEMPO_ESPERA', 'FATOR_MULTIPLICADOR', 'CICLO_FATURAMENTO', 'ESTADO_CIVIL_LEGADO', 'FLAG_EXCLUIDO', 'MODO_OPERACAO',
];
for (const table of tables) {
  for (const name of negatives) {
    rows.push({ table, column: name, type: 'integer', kind: 'none', template: '', field: '' });
  }
}

const header = 'table,column,native_type,expected_kind,expected_template,expected_field';
const lines = rows.map((r) => [r.table, r.column, r.type, r.kind, r.template, r.field].join(','));
fs.writeFileSync(path.join(__dirname, 'corpus-core-ptbr.csv'), [header, ...lines].join('\n') + '\n', 'utf8');
console.log(`corpus-core-ptbr.csv: ${rows.length} linhas (${rows.filter((r) => r.kind !== 'none').length} positivas, ${rows.filter((r) => r.kind === 'none').length} negativas)`);
