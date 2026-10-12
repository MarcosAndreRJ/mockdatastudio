# 35 — Matriz de Rastreabilidade

| Campo | Valor |
|---|---|
| Objetivo | Conectar Requisito → Módulo → Contrato → Tarefa → Teste → Critério de aceite. |
| Escopo | FRs MVP/V1 e NFRs principais. V2 rastreado após detalhamento. |
| Manutenção | Atualizada pela IA Executora a cada tarefa que altere testes/contratos ([33 §14](33-executor-instructions.md)). |

Módulos: CORE, PA (Providers.Abstractions), PRV (Providers.*), CAT, MAP, GEN, OUT, SQLT (SqlTooling), PRJ, APP (Application), INF (Infrastructure), UI.

| Requisito | Módulo(s) | Contrato(s) | Tarefa(s) | Teste(s) | Aceite |
|---|---|---|---|---|---|
| FR-001 | APP, CAT, UI | C02 (ConnectionProfile), C09 IConnectionService | 04.02, 09.02, 09.05, 10.01 | T-CAT-001, T-SEC-002, T-UI-FLOW-001 | AC-001.1–3 |
| FR-002 | PRV, APP, UI | IDatabaseProvider, IConnectionService.TestAsync | 02.02, 08.02, 08.03, 10.01, 09.05 | T-PRV-<X>-001, T-UI-FLOW-001 | AC-002.1–3 |
| FR-003 | INF, CAT | ISecretStore (INF), connection_profile.secret | 09.03 | T-SEC-002/004/012 | AC-003.1–3 |
| FR-004 | APP, UI | IExecutionGuard | 09.05, 11.01 | T-SEC-005, T-SQL-GUARD-001, T-UI-FLOW-004 | AC-004.1–2 |
| FR-005 | PRV, UI | ConnectionSettingDefinition | 11.03 | T-PRV-<X>-009, T-PRV-SQLITE-DATE-001 | AC-005.1–2 |
| FR-006 | APP, PRV | IErrorTranslator | 11.03 | T-SQL-EXEC-002, T-DIR-002 | AC-006.1–2 |
| FR-007 | APP, PRJ | ConnectionRef, ExecutionAuthorization | 11.04, 15.01 | T-SEC-003, T-REL-011 | AC-007.1–2 |
| FR-008 | PA, PRV | C02, IMdsModule | 02.01, 02.05 | T-ARC-007, T-PRV-<X>-* | AC-008.1–2 |
| FR-010 | PRV, UI | ISchemaExtractor.ListTablesAsync | 12.01 | T-PRV-<X>-002, T-UI-PERF-001 | AC-010.1–2 |
| FR-011 | PRV, UI | ExtractTableAsync | 12.02 | T-PRV-<X>-002/004/005 | AC-011.1 |
| FR-012 | UI | — | 12.02 | T-UI-FLOW-003 | AC-012.1 |
| FR-013 | UI, PRV | ISqlDialect | 12.03 | T-SQL-EXEC-001 | AC-013.1 |
| FR-014 | PRV, UI | ExtractTableAsync | 12.03 | T-PRV-<X>-002 | AC-014.1–2 |
| FR-015 | UI | — | 12.03 | T-UI-PERF-001 | AC-015.1 |
| FR-020 | UI, APP | QuerySession | 13.03 | T-UI-FLOW-003 | AC-020.1–2 |
| FR-021 | SQLT | ICompletionEngine | 13.02 | T-SQL-COMP-001 | AC-021.1 |
| FR-022 | SQLT | ICompletionEngine, ICompletionSchemaSource | 13.02 | T-SQL-COMP-001, B-SQL-001 | AC-022.1–2 |
| FR-023 | SQLT | ISqlScriptSplitter, ISqlLexer | 03.01, 03.02 | T-SQL-SPLIT-<X> | AC-023.1 |
| FR-024 | APP, UI | QuerySession.ExecuteScriptAsync | 13.01 | T-SQL-EXEC-001 | AC-024.1–2 |
| FR-025 | APP, UI | IQueryExecutor, QueryResultPage | 13.01, 13.04 | T-UI-PERF-003 | AC-025.1 |
| FR-026 | APP, PRV | CancellationToken | 13.01 | T-PRV-<X>-009, T-SQL-EXEC-002 | AC-026.1 |
| FR-027 | APP, UI | TransactionMode | 13.01 | T-SQL-EXEC-001, T-UI-FLOW-003 | AC-027.1–2 |
| FR-028 | SQLT, APP | SqlStatementInfo, IExecutionGuard | 03.02, 11.01, 11.02 | T-SQL-CLASS-001, T-SQL-GUARD-001, T-SEC-005/006 | AC-028.1–3 |
| FR-029 | CAT, UI | query_history | 13.04 | T-SQL-HIST-001 | AC-029.1 |
| FR-030 | OUT, UI | ISqlLiteralFormatter | 13.04 | T-OUT-ROUNDTRIP-<X>, T-OUT-005 | AC-030.1 |
| FR-031 | PRV, UI | IErrorTranslator | 13.04 | T-PRV-<X>-008 | AC-031.1 |
| FR-040 | PRV, CORE | ISchemaExtractor, SchemaModel | 01.02, 02.03, 08.02, 08.03, 10.01 | T-PRV-<X>-002..005/011, B-INT-001 | AC-040.1–2 |
| FR-041 | CORE | IDigestSerializer | 01.03 | T-DIG-001, B-DIG-SIZE | AC-041.1–2 |
| FR-042 | CORE, APP | IDigestSerializer.Deserialize, IDigestValidator | 01.04 | T-DIG-007, T-DIG-010 | AC-042.1–2 |
| FR-043 | CORE | IStructureHasher | 01.05 | T-DIG-003/004 | AC-043.1–2 |
| FR-044 | PRJ, UI | SnapshotEntry | 09.01, 14.01 | T-PRJ-001 | AC-044.1 |
| FR-045 | CORE, UI | ISchemaDiffer | 01.06, 14.01 | T-DIG-005/006 | AC-045.1 |
| FR-046 | APP, MAP | IMappingService.MigrateBindingsAsync | 14.02 | T-DIG-030, T-E2E-004 | AC-046.1–2 |
| FR-047 | PRV, CORE | CaptureOptions | 02.03, 14.01 | T-PRV-<X>-002 | AC-047.1 |
| FR-050 | CAT, UI | ITemplateRepository | 04.02, 17.01 | T-CAT-004, T-UI-FLOW-002 | AC-050.1–2 |
| FR-051 | CAT, MAP | TemplateAliasInfo, MappingKnowledge | 04.02, 05.03 | T-MAP-003/005 | AC-051.1 |
| FR-052 | GEN | TemplateDataSet, WeightedIndex | 04.07, 06.04 | T-GEN-LIB-catalog.template | AC-052.1 |
| FR-053 | CAT, GEN, UI | TemplateRecordRow, RowContext | 04.02, 06.04, 17.01 | T-GEN-COMP-001, T-MAP-006 | AC-053.1–2 |
| FR-054 | CAT, SQLT | IStagingImporter | 04.03, 04.04, 17.02 | T-CAT-003/004/005, T-SEC-009 | AC-054.1–3 |
| FR-055 | CAT | ITemplatePackService | 17.03 | T-CAT-008 | AC-055.1 |
| FR-056 | CAT | BuiltInPackInstaller | 04.06, 17.06 | T-CAT-010 | AC-056.1–2 |
| FR-057 | CAT | ICatalogMaintenance | 04.05 | T-CAT-006/007 | AC-057.1 |
| FR-058 | CAT | Triggers/repositório | 04.02 | T-CAT-002 | AC-058.1 |
| FR-059 | CAT, MAP | IGeneratorPresetRepository | 17.04 | T-MAP-003 | AC-059.1 |
| FR-060 | MAP | IStructuralBinder | 05.02 | T-MAP-002 | AC-060.1 |
| FR-061 | MAP | IMappingEngine | 05.03, 05.04 | T-MAP-CORPUS, T-MAP-004 | AC-061.1–2 |
| FR-062 | MAP | Confidence, MappingSuggestion.IsAmbiguous | 05.03 | T-MAP-005 | AC-062.1 |
| FR-063 | APP, UI | IMappingService.SetBinding | 09.06 | T-MAP-007, T-UI-FLOW-001 | AC-063.1 |
| FR-064 | CAT, MAP | IMappingRuleRepository, mapping_feedback | 17.04 | T-MAP-007 | AC-064.1 |
| FR-065 | APP, UI | ValidationIssue (MAP-*) | 17.04 | T-VAL-001..040 | AC-065.1 |
| FR-066 | APP | IMappingService.AutoBindAll | 09.02 | T-MAP-CORPUS, T-E2E-<X>-001 | AC-066.1 |
| FR-070 | GEN | IGeneratorFactory, IValueGenerator | 06.03, 06.04, 17.05 | T-GEN-LIB-<id>, T-GEN-DET-001 | AC-070.1 |
| FR-071 | GEN, UI | GeneratorDescriptor | 06.02, 09.06 | T-GEN-LIB-<id> | AC-071.1 |
| FR-072 | GEN | ColumnBinding.NullRate | 06.03 | T-GEN-NULL-001, T-VAL-* | AC-072.1–2 |
| FR-073 | GEN | GeneratorSpec.Children | 17.05 | T-GEN-LIB-core.switch | AC-073.1 |
| FR-074 | GEN | IExprCompiler | 06.05 | T-GEN-EXPR-001..003 | AC-074.1–3 |
| FR-075 | GEN | IIndexableGenerator, ValueDomain | 06.06 | T-GEN-UQ-001..004 | AC-075.1–3 |
| FR-076 | GEN | br.* | 17.05 | T-GEN-BR-001 | AC-076.1 |
| FR-077 | GEN | catalog.template.field | 06.04 | T-GEN-COMP-001 | AC-053.1–2 |
| FR-078 | GEN, APP | IReferenceDataSource | 16.03 | T-REL-010 | AC-078.1 |
| FR-079 | GEN | GeneratorSpec.Version | 06.02 | T-GEN-LIB-<id>, T-GEN-DET-002 | AC-079.1 |
| FR-080 | MAP, GEN | IStructuralBinder, rel.fk | 05.02, 07.04 | T-MAP-002, T-REL-001 | AC-080.1 |
| FR-081 | GEN, APP | RelationSettings, IReferenceDataSource | 07.04, 16.03 | T-REL-010, T-E2E-<X>-001 | AC-081.1 |
| FR-082 | GEN | CardinalitySpec | 07.04, 16.02 | T-REL-008/009 | AC-082.1–2 |
| FR-083 | GEN | IKeyOracle | 07.04 | T-REL-007 | AC-083.1 |
| FR-084 | GEN | CycleBreakStrategy | 07.02, 16.01 | T-REL-002..006, T-REL-SELF-NN-<X> | AC-084.1–2 |
| FR-085 | GEN, PRV | KeyColumnPlan, ITargetInspector | 07.03, 15.04 | T-REL-012/013, T-PRV-<X>-010 | AC-085.1–2 |
| FR-086 | GEN | IGenerationPlanner, PlanDiagnostic | 07.05 | T-REL-PLAN-001, T-REL-005 | AC-086.1–2 |
| FR-087 | APP, GEN | ValidationSettings.VerifyReferencesInTarget | 16.03 | T-REL-011 | AC-087.1 |
| FR-090 | GEN, UI | GenerationPlan.Estimated* | 07.05, 09.07 | B-OUT-001 | AC-090.1 |
| FR-091 | GEN, UI | IGenerationEngine.PreviewAsync | 06.07, 09.07 | T-GEN-DET-002 | AC-091.1 |
| FR-092 | GEN, APP | IPreflightValidator, ITrialInsertRunner | 07.06, 16.05 | T-VAL-*, T-VAL-TRIAL-001/002 | AC-092.1–2 |
| FR-093 | OUT, PRV | IOutputWriter, ISqlDialect | 08.01–08.03, 10.02 | T-E2E-<X>-001, T-OUT-001, T-OUT-ROUNDTRIP-<X> | AC-093.1 |
| FR-094 | OUT | ISegmentedTextSink, RunManifest | 08.01, 18.01 | T-OUT-002/003 | AC-094.1 |
| FR-095 | OUT, APP, PRV | IBulkInserter, DirectInsertSession, ExecutionAuthorization | 15.01–15.03 | T-DIR-<X>-001, T-SEC-001, T-E2E-<X>-002 | AC-095.1–2 |
| FR-096 | GEN, UI | RunProgress | 06.07, 15.03 | T-DIR-003, T-UI-PERF-002 | AC-096.1 |
| FR-097 | GEN | StreamKey, DeterministicRandom, PlanHash | 06.01, 06.07 | T-GEN-DET-001..003, T-E2E-003 | AC-097.1–2 |
| FR-098 | APP, CAT | RunOutcome, generation_run | 15.05 | T-DIR-002 | AC-098.1 |
| FR-099 | OUT | ICleanupScriptBuilder | 15.05 | T-OUT-004, T-E2E-<X>-002 | AC-099.1 |
| FR-100 | OUT | IOutputWriterFactory (Csv/JsonLines) | 18.02 | T-OUT-005 | AC-100.1 |
| FR-101 | OUT, PRV | ISqlDialect.ResyncSequenceStatements | 08.01 | T-OUT-006, T-PRV-<X>-010 | AC-085.1 |
| FR-110 | PRJ, APP | IProjectStore, IProjectSerializer | 09.01 | T-PRJ-001/006 | AC-110.1–2 |
| FR-111 | PRJ | GenerationProfile | 09.01, 14.03 | T-PRJ-001 | AC-111.1 |
| FR-112 | PRJ | SanitizeOptions | 09.01, 14.03 | T-PRJ-003, T-SEC-002 | AC-112.1–2 |
| FR-113 | PRJ | ProjectMigrator | 14.03 | T-PRJ-004 | AC-113.1 |
| FR-114 | APP, CAT | RunSummary, generation_run | 15.05 | T-DIR-002 | AC-114.1–2 |
| FR-115 | APP, UI | — | 14.03 | T-PRJ-006 | AC-115.1 |
| FR-116 | APP, PRJ | CatalogDependency | 14.04 | T-PRJ-007 | AC-116.1 |
| FR-120 | UI, INF | IAppSettings | 19.02 | T-UI-FLOW-001 | AC-120.1 |
| FR-121 | INF | Logging | 19.03 | T-SEC-004 | AC-121.1 |
| FR-122 | UI | — | 19.02 | T-UI-A11Y-003 | AC-122.1 |
| NFR-PERF-001/002 | GEN, OUT | IRowSink, Channel | 06.07, 18.03 | B-GEN-001, B-MEM-001 | G-MVP-7, G-V1-6 |
| NFR-DET-001 | GEN | ADR-013 | 06.01, 06.07 | T-GEN-DET-002 | G-MVP-3 |
| NFR-SEC-001..003 | INF, APP | — | 09.03, 15.01, 19.04 | T-SEC-*, T-SEC-NET-001 | G-MVP-9, G-V1-8 |
| NFR-MNT-001 | todos | 05 §3 | 00.02 | T-ARC-* | G-MVP-9 |
| NFR-A11Y-001 | UI | — | 19.01 | T-UI-A11Y-*, T-UI-DPI-001 | G-V1-7 |
| NFR-COMP-002 | CORE, PRJ | versionamento | 01.04, 14.03 | T-DIG-020, T-PRJ-004 | G-V1-1 |
