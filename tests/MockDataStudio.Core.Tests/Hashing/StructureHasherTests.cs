using System.Text.Json.Nodes;
using MockDataStudio.Core.Hashing;
using MockDataStudio.Core.Naming;
using MockDataStudio.Core.Schema;
using MockDataStudio.Core.Tests.TestSupport;
using Shouldly;
using Xunit;

namespace MockDataStudio.Core.Tests.Hashing;

public sealed class StructureHasherTests
{
    private static readonly StructureHasher Hasher = new();

    [Theory]
    [Trait("Category", "Unit")]
    [Trait("Id", "T-DIG-003")]
    [InlineData("sqlite")]
    [InlineData("postgresql")]
    public void T_DIG_003_Hash_InvariantePorBancoServidorHorarioOpcoesEComentarios(string engine)
    {
        var model = DigestFixtures.LoadExample(engine);
        var baseline = Hasher.Compute(model);

        var changed = model with
        {
            DatabaseName = "outro_banco",
            ServerVersion = "99.9",
            CapturedAt = DateTimeOffset.UnixEpoch.AddYears(40),
            Options = model.Options with { IncludeComments = true },
        };

        Hasher.Compute(changed).ShouldBe(baseline);
    }

    [Fact]
    [Trait("Category", "Unit")]
    [Trait("Id", "T-DIG-003")]
    public void T_DIG_003_Hash_InvarianteAOrdemDeColunasNomesDeConstraintsEComentarios()
    {
        var model = DigestFixtures.LoadExample("sqlite");
        var baseline = Hasher.Compute(model);

        var changed = DigestFixtures.MapTables(model, table => table with
        {
            Columns = table.Columns.Select((c, i) => c with { Ordinal = 100 - i, Comment = "comentario qualquer" }).ToList(),
            ForeignKeys = table.ForeignKeys.Select(fk => fk with { Name = "nome_gerado_" + fk.Name }).ToList(),
            UniqueConstraints = table.UniqueConstraints.Select(u => u with { Name = "uq_" + (u.Name ?? "x") }).ToList(),
            Indexes = table.Indexes.Select(i => i with { Name = "ix_novo_" + i.Name }).ToList(),
            Comment = "tabela comentada",
        });

        Hasher.Compute(changed).ShouldBe(baseline);
    }

    [Fact]
    [Trait("Category", "Unit")]
    [Trait("Id", "T-DIG-003")]
    public void T_DIG_003_Hash_InvarianteAFormatacaoDeExpressoes()
    {
        var model = DigestFixtures.LoadExample("sqlite");

        var spaced = DigestFixtures.ReplaceTable(model, "pedido", table => table with
        {
            Checks = [new CheckConstraintModel(null, "STATUS   IN ( 'ABERTO' , 'PAGO' ,'ENVIADO','CANCELADO')", ["status"])],
        });

        var compact = DigestFixtures.ReplaceTable(model, "pedido", table => table with
        {
            Checks = [new CheckConstraintModel(null, "status IN ('ABERTO','PAGO','ENVIADO','CANCELADO')", ["status"])],
        });

        Hasher.Compute(spaced).ShouldBe(Hasher.Compute(compact));
    }

    public static TheoryData<string, Func<SchemaModel, SchemaModel>> MudancasEstruturais()
    {
        var data = new TheoryData<string, Func<SchemaModel, SchemaModel>>();

        data.Add("tipo da coluna", m => DigestFixtures.ReplaceTable(m, "cliente", t => t with
        {
            Columns = Replace(t.Columns, "nome", c => c with { Type = c.Type with { Length = 121 } }),
        }));

        data.Add("nulabilidade", m => DigestFixtures.ReplaceTable(m, "cliente", t => t with
        {
            Columns = Replace(t.Columns, "nome", c => c with { IsNullable = true }),
        }));

        data.Add("identidade", m => DigestFixtures.ReplaceTable(m, "cliente", t => t with
        {
            Columns = Replace(t.Columns, "nome", c => c with { Identity = new IdentityInfo(IdentityKind.AutoIncrement) }),
        }));

        data.Add("coluna gerada", m => DigestFixtures.ReplaceTable(m, "pedido", t => t with
        {
            Columns = Replace(t.Columns, "status", c => c with { Generated = new GeneratedInfo("upper(id)", Stored: true) }),
        }));

        data.Add("default", m => DigestFixtures.ReplaceTable(m, "cliente", t => t with
        {
            Columns = Replace(t.Columns, "ativo", c => c with { Default = new DefaultValue("0", DefaultKind.Literal, "0") }),
        }));

        data.Add("coluna da PK", m => DigestFixtures.ReplaceTable(m, "cliente", t => t with
        {
            PrimaryKey = new PrimaryKeyModel(null, ["cpf"]),
        }));

        data.Add("acao ON DELETE da FK", m => DigestFixtures.ReplaceTable(m, "pedido", t => t with
        {
            ForeignKeys = t.ForeignKeys.Select(fk => fk with { OnDelete = ReferentialAction.Cascade }).ToList(),
        }));

        data.Add("FK deferrable", m => DigestFixtures.ReplaceTable(m, "pedido", t => t with
        {
            ForeignKeys = t.ForeignKeys.Select(fk => fk with { IsDeferrable = true, InitiallyDeferred = true }).ToList(),
        }));

        data.Add("UNIQUE adicionada", m => DigestFixtures.ReplaceTable(m, "pedido", t => t with
        {
            UniqueConstraints = t.UniqueConstraints.Append(new UniqueConstraintModel(null, ["status"], UniqueSource.Constraint)).ToList(),
        }));

        data.Add("UNIQUE NULLS NOT DISTINCT", m => DigestFixtures.ReplaceTable(m, "cliente", t => t with
        {
            UniqueConstraints = t.UniqueConstraints.Select(u => u with { NullsDistinct = false }).ToList(),
        }));

        data.Add("expressao de CHECK", m => DigestFixtures.ReplaceTable(m, "produto", t => t with
        {
            Checks = t.Checks.Select(c => c with { Expression = "preco > 0" }).ToList(),
        }));

        data.Add("coluna do indice", m => DigestFixtures.ReplaceTable(m, "cliente", t => t with
        {
            Indexes = t.Indexes.Select(i => i with { Columns = [new IndexColumn("email")] }).ToList(),
        }));

        data.Add("predicado de indice", m => DigestFixtures.ReplaceTable(m, "cliente", t => t with
        {
            Indexes = t.Indexes.Select(i => i with { Predicate = "ativo = 1" }).ToList(),
        }));

        data.Add("sequence", m => AddSequence(m, new SequenceModel("seq_pedido", Start: 1, Increment: 1)));
        data.Add("sequence com incremento diferente", m => AddSequence(m, new SequenceModel("seq_pedido", Start: 1, Increment: 5)));

        data.Add("trigger: timing", m => AddTrigger(m, t => t with { Timing = TriggerTiming.Before }));
        data.Add("trigger: eventos", m => AddTrigger(m, t => t with { Events = TriggerEvents.Delete }));
        data.Add("trigger: inativa", m => AddTrigger(m, t => t with { IsActive = false }));

        data.Add("rotina adicionada", m => AddRoutine(m, new RoutineModel("calcula_total", RoutineKind.Function, 2, "numeric")));
        data.Add("tabela adicionada", m => AddTable(m, "tabela_nova"));
        data.Add("coluna removida", m => DigestFixtures.ReplaceTable(m, "produto", t => t with
        {
            Columns = t.Columns.Where(c => c.Name != "atributos").ToList(),
        }));

        return data;
    }

    [Theory]
    [Trait("Category", "Unit")]
    [Trait("Id", "T-DIG-004")]
    [MemberData(nameof(MudancasEstruturais))]
    public void T_DIG_004_Hash_MudaComCadaMudancaEstrutural(string description, Func<SchemaModel, SchemaModel> mutate)
    {
        var model = DigestFixtures.LoadExample("sqlite");
        var baseline = Hasher.Compute(model);

        Hasher.Compute(mutate(model)).ShouldNotBe(baseline, $"Mudanca nao detectada: {description}");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ComputeTable_E_ColumnFingerprint_SaoDeterministicos()
    {
        var model = DigestFixtures.LoadExample("sqlite");
        var table = DigestFixtures.FindTable(model, "pedido");
        var name = new ObjectName("main", "pedido");

        Hasher.ComputeTable(name, table).ShouldBe(Hasher.ComputeTable(name, table));
        Hasher.ComputeTable(name, table).ShouldStartWith("sha256:");

        var column = table.FindColumn("cliente_id")!;
        var fingerprint = Hasher.ComputeColumnFingerprint(table, column);
        fingerprint.Length.ShouldBe(16);
        fingerprint.ShouldMatch("^[0-9a-f]{16}$");
        Hasher.ComputeColumnFingerprint(table, column).ShouldBe(fingerprint);

        var nullable = column with { IsNullable = true };
        Hasher.ComputeColumnFingerprint(table, nullable).ShouldNotBe(fingerprint);
    }

    [Theory]
    [Trait("Category", "Unit")]
    [Trait("Id", "T-DIG-003")]
    [InlineData("sqlite")]
    [InlineData("mysql")]
    [InlineData("postgresql")]
    [InlineData("firebird")]
    public void Hashes_DosExemplos_BatemComOCampoHashDoArquivo(string engine)
    {
        var json = RepoPaths.ReadExample(engine);
        var model = DigestFixtures.LoadExample(engine);

        Hasher.Compute(model).ShouldBe(JsonNode.Parse(json)!["hash"]!.GetValue<string>(),
            "O hash registrado no exemplo deve ser o recalculado (DIG-HASH-MISMATCH ausente).");
    }

    private static List<ColumnModel> Replace(IReadOnlyList<ColumnModel> columns, string name, Func<ColumnModel, ColumnModel> map) =>
        columns.Select(c => c.Name == name ? map(c) : c).ToList();

    private static SchemaModel AddSequence(SchemaModel model, SequenceModel sequence) =>
        model with
        {
            Namespaces = model.Namespaces.Select(ns => ns with { Sequences = ns.Sequences.Append(sequence).ToList() }).ToList(),
        };

    private static SchemaModel AddRoutine(SchemaModel model, RoutineModel routine) =>
        model with
        {
            Namespaces = model.Namespaces.Select(ns => ns with { Routines = ns.Routines.Append(routine).ToList() }).ToList(),
        };

    private static SchemaModel AddTrigger(SchemaModel model, Func<TriggerModel, TriggerModel> map)
    {
        var trigger = new TriggerModel("trg_teste", new ObjectName("main", "pedido"), TriggerTiming.After, TriggerEvents.Insert);
        return model with
        {
            Namespaces = model.Namespaces.Select(ns => ns with { Triggers = ns.Triggers.Append(map(trigger)).ToList() }).ToList(),
        };
    }

    private static SchemaModel AddTable(SchemaModel model, string name)
    {
        var table = TableModel.Create(name, [new ColumnModel("id", 1, new DataTypeInfo("integer", LogicalType.Integer), false)]).Value;
        return model with
        {
            Namespaces = model.Namespaces.Select(ns => ns with { Tables = ns.Tables.Append(table).ToList() }).ToList(),
        };
    }
}
