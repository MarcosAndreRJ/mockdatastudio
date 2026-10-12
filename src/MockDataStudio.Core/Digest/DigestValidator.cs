using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using MockDataStudio.Core.Common;
using MockDataStudio.Core.Hashing;
using MockDataStudio.Core.Naming;

namespace MockDataStudio.Core.Digest;

// Validador codificado do digest 1.0 (DA-017): reproduz as regras de digest-1.0.schema.json e as regras
// semânticas de 12 §7. Acumula todos os erros, com caminho JSON, em vez de parar no primeiro.
public sealed partial class DigestValidator(IStructureHasher hasher) : IDigestValidator
{
    private const string ColumnFlagsPattern =
        "^(nn( (ai|idd|ida|ser|tsq))?( (gs|gv))?|(ai|idd|ida|ser|tsq)( (gs|gv))?|gs|gv)?$";

    private static readonly HashSet<string> RootKeys =
        ["format", "version", "engine", "server", "database", "capturedAt", "hash", "options", "stats", "meta", "namespaces"];

    private static readonly HashSet<string> OptionKeys =
        ["views", "routines", "triggers", "comments", "indexes", "include", "exclude"];

    private static readonly HashSet<string> NamespaceKeys =
        ["name", "virtual", "x", "tables", "views", "sequences", "routines", "triggers"];

    private static readonly HashSet<string> TableKeys =
        ["name", "cols", "pk", "pkn", "fk", "uq", "ix", "ck", "cmt", "x"];

    private static readonly HashSet<string> ViewKeys = ["name", "cols", "upd", "cmt"];
    private static readonly HashSet<string> ColumnExtrasKeys = ["def", "seq", "gx", "enum", "cs", "coll", "dom", "lu", "cmt", "x"];
    private static readonly HashSet<string> ForeignKeyKeys = ["n", "c", "r", "rc", "od", "ou", "df", "ne"];
    private static readonly HashSet<string> UniqueKeys = ["n", "c", "ix", "nnd"];
    private static readonly HashSet<string> IndexKeys = ["n", "c", "e", "w"];
    private static readonly HashSet<string> CheckConstraintKeys = ["n", "e"];
    private static readonly HashSet<string> SequenceKeys = ["n", "s", "i", "min", "max", "cy"];
    private static readonly HashSet<string> RoutineKeys = ["n", "k", "p", "rt"];
    private static readonly HashSet<string> TriggerKeys = ["n", "t", "tm", "ev", "a", "pos"];

    private static readonly string[] Engines = ["sqlite", "mysql", "postgresql", "firebird"];
    private static readonly string[] Actions = ["cascade", "restrict", "setnull", "setdefault"];

    private static readonly Regex ColumnFlags = new(ColumnFlagsPattern, RegexOptions.CultureInvariant);
    private static readonly Regex VersionRegex = VersionPattern();
    private static readonly Regex CapturedAtRegex = CapturedAtPattern();
    private static readonly Regex HashRegex = HashPattern();
    private static readonly Regex IdentifierRegex = new("^.{1,255}$", RegexOptions.Singleline | RegexOptions.CultureInvariant);

    private readonly IStructureHasher _hasher = hasher;

    public IReadOnlyList<DigestValidationError> Validate(string json)
    {
        var errors = new List<DigestValidationError>();

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            errors.Add(Error("$", ErrorCodes.DigJson, $"JSON inválido: {ex.Message}"));
            return errors;
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                errors.Add(Error("$", ErrorCodes.DigSchema, "A raiz do digest deve ser um objeto."));
                return errors;
            }

            ValidateRoot(root, errors);
            if (errors.Any(e => e.Severity == DigestSeverity.Error))
            {
                return errors;
            }

            ValidateNamespaces(root, errors);
            if (errors.Any(e => e.Severity == DigestSeverity.Error))
            {
                return errors;
            }

            ValidateHash(json, root, errors);
            return errors;
        }
    }

    // ---------- Raiz ----------

    private static void ValidateRoot(JsonElement root, List<DigestValidationError> errors)
    {
        CheckKeys(root, "$", RootKeys, ["format", "version", "engine", "server", "database", "capturedAt", "hash", "namespaces"], errors);

        if (Str(root, "$", "format", errors) is { } format && format != "mds.digest")
        {
            errors.Add(Error("$.format", ErrorCodes.DigFormat, "O campo 'format' deve ser 'mds.digest'."));
        }

        if (Str(root, "$", "version", errors) is { } version)
        {
            ValidateVersion(version, errors);
        }

        if (Str(root, "$", "engine", errors) is { } engine && Array.IndexOf(Engines, engine) < 0)
        {
            errors.Add(Error("$.engine", ErrorCodes.DigSchema, $"Engine desconhecida: '{engine}'."));
        }

        if (Str(root, "$", "server", errors) is { } server && server.Length == 0)
        {
            errors.Add(Error("$.server", ErrorCodes.DigSchema, "'server' não pode ser vazio."));
        }

        if (Str(root, "$", "database", errors) is { } database && database.Length == 0)
        {
            errors.Add(Error("$.database", ErrorCodes.DigSchema, "'database' não pode ser vazio."));
        }

        if (Str(root, "$", "capturedAt", errors) is { } capturedAt && !CapturedAtRegex.IsMatch(capturedAt))
        {
            errors.Add(Error("$.capturedAt", ErrorCodes.DigSchema, "'capturedAt' deve estar no formato AAAA-MM-DDTHH:MM:SSZ."));
        }

        if (Str(root, "$", "hash", errors) is { } hash && !HashRegex.IsMatch(hash))
        {
            errors.Add(Error("$.hash", ErrorCodes.DigSchema, "'hash' deve ser 'sha256:' seguido de 64 caracteres hexadecimais minúsculos."));
        }

        if (root.TryGetProperty("options", out var options))
        {
            ValidateOptions(options, errors);
        }

        if (root.TryGetProperty("namespaces", out var namespaces) && namespaces.ValueKind != JsonValueKind.Array)
        {
            errors.Add(Error("$.namespaces", ErrorCodes.DigSchema, "'namespaces' deve ser um array."));
        }
    }

    private static void ValidateVersion(string version, List<DigestValidationError> errors)
    {
        var match = VersionRegex.Match(version);
        if (!match.Success)
        {
            errors.Add(Error("$.version", ErrorCodes.DigVersion, "'version' deve ter o formato MAJOR.MINOR."));
            return;
        }

        var major = int.Parse(match.Groups["major"].Value, System.Globalization.CultureInfo.InvariantCulture);
        var minor = int.Parse(match.Groups["minor"].Value, System.Globalization.CultureInfo.InvariantCulture);

        if (major != 1)
        {
            errors.Add(Error("$.version", ErrorCodes.DigMajorUnsupported, $"Versão maior {major} não suportada por esta versão do aplicativo."));
        }
        else if (minor > 0)
        {
            errors.Add(Error("$.version", ErrorCodes.DigMinorNewer,
                $"Digest versão 1.{minor} é mais novo que 1.0: chaves desconhecidas serão ignoradas.", DigestSeverity.Warning));
        }
    }

    private static void ValidateOptions(JsonElement options, List<DigestValidationError> errors)
    {
        const string path = "$.options";
        if (!IsObject(options, path, errors))
        {
            return;
        }

        CheckKeys(options, path, OptionKeys, [], errors);
        foreach (var flag in new[] { "views", "routines", "triggers", "comments", "indexes" })
        {
            Bool(options, path, flag, errors);
        }

        foreach (var list in new[] { "include", "exclude" })
        {
            StringArray(options, path, list, errors, minItems: 0);
        }
    }

    // ---------- Namespaces, tabelas e objetos ----------

    private static void ValidateNamespaces(JsonElement root, List<DigestValidationError> errors)
    {
        if (!root.TryGetProperty("namespaces", out var namespaces) || namespaces.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        var seenNamespaces = new HashSet<string>(StringComparer.Ordinal);
        var index = 0;
        foreach (var ns in namespaces.EnumerateArray())
        {
            var path = $"$.namespaces[{index++}]";
            if (!IsObject(ns, path, errors))
            {
                continue;
            }

            CheckKeys(ns, path, NamespaceKeys, ["name"], errors);
            var name = Str(ns, path, "name", errors);
            if (name is not null && !seenNamespaces.Add(name))
            {
                errors.Add(Error(path, ErrorCodes.DigDup, $"Namespace '{name}' duplicado."));
            }

            Bool(ns, path, "virtual", errors);
            ValidateExtrasMap(ns, path, "x", errors);

            var tables = ArrayOf(ns, path, "tables", errors);
            var views = ArrayOf(ns, path, "views", errors);
            var names = new HashSet<string>(StringComparer.Ordinal);

            foreach (var (listName, list) in new[] { ("tables", tables), ("views", views) })
            {
                if (list is null)
                {
                    continue;
                }

                var i = 0;
                foreach (var item in list.Value.EnumerateArray())
                {
                    var itemPath = $"{path}.{listName}[{i++}]";
                    var itemName = listName == "tables"
                        ? ValidateTable(item, itemPath, errors)
                        : ValidateView(item, itemPath, errors);

                    if (itemName is not null && !names.Add(itemName))
                    {
                        errors.Add(Error(itemPath, ErrorCodes.DigDup, $"Nome '{itemName}' duplicado no namespace '{name}'."));
                    }
                }
            }

            ValidateSequencesRoutinesTriggers(ns, path, errors);
        }

        ValidateReferences(root, errors);
    }

    // Retorna o nome da tabela, quando identificável.
    private static string? ValidateTable(JsonElement table, string path, List<DigestValidationError> errors)
    {
        if (!IsObject(table, path, errors))
        {
            return null;
        }

        CheckKeys(table, path, TableKeys, ["name", "cols"], errors);
        var name = Identifier(table, path, "name", errors);

        var columnNames = new HashSet<string>(StringComparer.Ordinal);
        var cols = ArrayOf(table, path, "cols", errors, minItems: 1);
        if (cols is not null)
        {
            var i = 0;
            foreach (var col in cols.Value.EnumerateArray())
            {
                var colPath = $"{path}.cols[{i++}]";
                var colName = ValidateColumn(col, colPath, errors);
                if (colName is not null && !columnNames.Add(colName))
                {
                    errors.Add(Error(colPath, ErrorCodes.DigDup, $"Coluna '{colName}' duplicada na tabela '{name}'."));
                }
            }
        }

        IdentifierList(table, path, "pk", errors);
        Identifier(table, path, "pkn", errors);
        Str(table, path, "cmt", errors);
        ValidateExtrasMap(table, path, "x", errors);

        ValidateConstraints(table, path, columnNames, errors);
        return name;
    }

    private static string? ValidateView(JsonElement view, string path, List<DigestValidationError> errors)
    {
        if (!IsObject(view, path, errors))
        {
            return null;
        }

        CheckKeys(view, path, ViewKeys, ["name", "cols"], errors);
        var name = Identifier(view, path, "name", errors);
        var cols = ArrayOf(view, path, "cols", errors, minItems: 1);
        if (cols is not null)
        {
            var i = 0;
            foreach (var col in cols.Value.EnumerateArray())
            {
                ValidateColumn(col, $"{path}.cols[{i++}]", errors);
            }
        }

        Bool(view, path, "upd", errors);
        Str(view, path, "cmt", errors);
        return name;
    }

    private static string? ValidateColumn(JsonElement column, string path, List<DigestValidationError> errors)
    {
        if (column.ValueKind != JsonValueKind.Array)
        {
            errors.Add(Error(path, ErrorCodes.DigSchema, "A coluna deve ser uma tupla [nome, tipo, flags?, extras?]."));
            return null;
        }

        var items = column.EnumerateArray().ToArray();
        if (items.Length is < 2 or > 4)
        {
            errors.Add(Error(path, ErrorCodes.DigSchema, "A tupla de coluna deve ter de 2 a 4 elementos."));
            return null;
        }

        var name = items[0].ValueKind == JsonValueKind.String ? items[0].GetString() : null;
        if (name is null || !IdentifierRegex.IsMatch(name))
        {
            errors.Add(Error($"{path}[0]", ErrorCodes.DigSchema, "Nome de coluna inválido (1 a 255 caracteres)."));
        }

        if (items[1].ValueKind != JsonValueKind.String)
        {
            errors.Add(Error($"{path}[1]", ErrorCodes.DigSchema, "O tipo da coluna deve ser texto."));
        }

        var flags = string.Empty;
        if (items.Length > 2)
        {
            if (items[2].ValueKind != JsonValueKind.String || !ColumnFlags.IsMatch(items[2].GetString()!))
            {
                errors.Add(Error($"{path}[2]", ErrorCodes.DigSchema, "Flags de coluna fora do padrão permitido."));
            }
            else
            {
                flags = items[2].GetString()!;
            }
        }

        if (items.Length > 3)
        {
            ValidateColumnExtras(items[3], $"{path}[3]", errors);
        }

        var tokens = flags.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var hasIdentity = tokens.Any(t => t is "ai" or "idd" or "ida" or "ser" or "tsq");
        var hasGenerated = tokens.Any(t => t is "gs" or "gv");
        if (hasIdentity && hasGenerated)
        {
            errors.Add(Error(path, ErrorCodes.DigFlags, "Coluna identidade não pode ser também gerada (gs/gv)."));
        }

        return name;
    }

    private static void ValidateColumnExtras(JsonElement extras, string path, List<DigestValidationError> errors)
    {
        if (!IsObject(extras, path, errors))
        {
            return;
        }

        CheckKeys(extras, path, ColumnExtrasKeys, [], errors);
        Str(extras, path, "def", errors);
        Str(extras, path, "seq", errors);
        Str(extras, path, "gx", errors);
        StringArray(extras, path, "enum", errors, minItems: 0);
        Str(extras, path, "cs", errors);
        Str(extras, path, "coll", errors);
        Str(extras, path, "dom", errors);
        if (extras.TryGetProperty("lu", out var lu) && !(lu.ValueKind == JsonValueKind.String && lu.GetString() == "b"))
        {
            errors.Add(Error($"{path}.lu", ErrorCodes.DigSchema, "'lu' deve ser \"b\"."));
        }

        Str(extras, path, "cmt", errors);
        ValidateExtrasMap(extras, path, "x", errors);
    }

    private static void ValidateConstraints(JsonElement table, string path, HashSet<string> columns, List<DigestValidationError> errors)
    {
        if (table.TryGetProperty("fk", out var fks) && fks.ValueKind == JsonValueKind.Array)
        {
            var i = 0;
            foreach (var fk in fks.EnumerateArray())
            {
                var fkPath = $"{path}.fk[{i++}]";
                if (!IsObject(fk, fkPath, errors))
                {
                    continue;
                }

                CheckKeys(fk, fkPath, ForeignKeyKeys, ["n", "c", "r", "rc"], errors);
                Identifier(fk, fkPath, "n", errors);
                var local = IdentifierList(fk, fkPath, "c", errors);
                var referenced = IdentifierList(fk, fkPath, "rc", errors);
                if (local is not null && referenced is not null && local.Count != referenced.Count)
                {
                    errors.Add(Error(fkPath, ErrorCodes.DigFkArity, "A FK tem quantidade de colunas diferente da referenciada."));
                }

                if (local is not null)
                {
                    ReportMissing(local, columns, fkPath, errors);
                }

                EnumStr(fk, fkPath, "od", Actions, errors);
                EnumStr(fk, fkPath, "ou", Actions, errors);
                EnumStr(fk, fkPath, "df", ["i", "d"], errors);
                ConstTrue(fk, fkPath, "ne", errors);

                if (fk.TryGetProperty("r", out var r) && r.ValueKind == JsonValueKind.String)
                {
                    if (r.GetString()!.Length < 3)
                    {
                        errors.Add(Error($"{fkPath}.r", ErrorCodes.DigSchema, "Referência de FK muito curta."));
                    }
                }
            }
        }

        if (table.TryGetProperty("uq", out var uqs) && uqs.ValueKind == JsonValueKind.Array)
        {
            var i = 0;
            foreach (var uq in uqs.EnumerateArray())
            {
                var uqPath = $"{path}.uq[{i++}]";
                if (!IsObject(uq, uqPath, errors))
                {
                    continue;
                }

                CheckKeys(uq, uqPath, UniqueKeys, ["c"], errors);
                Identifier(uq, uqPath, "n", errors);
                var cols = IdentifierList(uq, uqPath, "c", errors);
                if (cols is not null)
                {
                    ReportMissing(cols, columns, uqPath, errors);
                }

                ConstTrue(uq, uqPath, "ix", errors);
                ConstTrue(uq, uqPath, "nnd", errors);
            }
        }

        if (table.TryGetProperty("ix", out var ixs) && ixs.ValueKind == JsonValueKind.Array)
        {
            var i = 0;
            foreach (var ix in ixs.EnumerateArray())
            {
                var ixPath = $"{path}.ix[{i++}]";
                if (!IsObject(ix, ixPath, errors))
                {
                    continue;
                }

                CheckKeys(ix, ixPath, IndexKeys, ["n"], errors);
                Identifier(ix, ixPath, "n", errors);
                if (!ix.TryGetProperty("c", out _) && !ix.TryGetProperty("e", out _))
                {
                    errors.Add(Error(ixPath, ErrorCodes.DigSchema, "Índice deve ter 'c' (colunas) ou 'e' (expressões)."));
                }

                if (ix.TryGetProperty("c", out var cols) && cols.ValueKind == JsonValueKind.Array)
                {
                    var names = cols.EnumerateArray()
                        .Where(c => c.ValueKind == JsonValueKind.String)
                        .Select(c => StripDesc(c.GetString()!))
                        .ToList();
                    ReportMissing(names, columns, ixPath, errors);
                }

                StringArray(ix, ixPath, "e", errors, minItems: 1);
                Str(ix, ixPath, "w", errors);
            }
        }

        if (table.TryGetProperty("ck", out var cks) && cks.ValueKind == JsonValueKind.Array)
        {
            var i = 0;
            foreach (var ck in cks.EnumerateArray())
            {
                var ckPath = $"{path}.ck[{i++}]";
                if (!IsObject(ck, ckPath, errors))
                {
                    continue;
                }

                CheckKeys(ck, ckPath, CheckConstraintKeys, ["e"], errors);
                Identifier(ck, ckPath, "n", errors);
                Str(ck, ckPath, "e", errors);
            }
        }

        if (table.TryGetProperty("pk", out var pk))
        {
            IEnumerable<string> pkCols = pk.ValueKind == JsonValueKind.Array
                ? pk.EnumerateArray().Where(c => c.ValueKind == JsonValueKind.String).Select(c => c.GetString()!).ToList()
                : Array.Empty<string>();
            ReportMissing(pkCols, columns, $"{path}.pk", errors);
        }
    }

    private static void ValidateSequencesRoutinesTriggers(JsonElement ns, string path, List<DigestValidationError> errors)
    {
        var sequences = ArrayOf(ns, path, "sequences", errors);
        if (sequences is not null)
        {
            var i = 0;
            foreach (var seq in sequences.Value.EnumerateArray())
            {
                var seqPath = $"{path}.sequences[{i++}]";
                if (!IsObject(seq, seqPath, errors))
                {
                    continue;
                }

                CheckKeys(seq, seqPath, SequenceKeys, ["n"], errors);
                Identifier(seq, seqPath, "n", errors);
                foreach (var key in new[] { "s", "i", "min", "max" })
                {
                    if (seq.TryGetProperty(key, out var value) && value.ValueKind != JsonValueKind.Number && value.ValueKind != JsonValueKind.String)
                    {
                        errors.Add(Error($"{seqPath}.{key}", ErrorCodes.DigSchema, "Valor de sequence deve ser inteiro ou texto."));
                    }
                }

                ConstTrue(seq, seqPath, "cy", errors);
            }
        }

        var routines = ArrayOf(ns, path, "routines", errors);
        if (routines is not null)
        {
            var i = 0;
            foreach (var routine in routines.Value.EnumerateArray())
            {
                var rPath = $"{path}.routines[{i++}]";
                if (!IsObject(routine, rPath, errors))
                {
                    continue;
                }

                CheckKeys(routine, rPath, RoutineKeys, ["n", "k"], errors);
                Identifier(routine, rPath, "n", errors);
                EnumStr(routine, rPath, "k", ["p", "f"], errors);
                Int(routine, rPath, "p", errors, minimum: 0);
                Str(routine, rPath, "rt", errors);
            }
        }

        var triggers = ArrayOf(ns, path, "triggers", errors);
        if (triggers is not null)
        {
            var i = 0;
            foreach (var trigger in triggers.Value.EnumerateArray())
            {
                var tPath = $"{path}.triggers[{i++}]";
                if (!IsObject(trigger, tPath, errors))
                {
                    continue;
                }

                CheckKeys(trigger, tPath, TriggerKeys, ["n", "tm", "ev"], errors);
                Identifier(trigger, tPath, "n", errors);
                Identifier(trigger, tPath, "t", errors);
                EnumStr(trigger, tPath, "tm", ["b", "a", "io"], errors);
                if (trigger.TryGetProperty("ev", out var ev) && (ev.ValueKind != JsonValueKind.String || ev.GetString()!.Length == 0 || !EventsPattern().IsMatch(ev.GetString()!)))
                {
                    errors.Add(Error($"{tPath}.ev", ErrorCodes.DigSchema, "'ev' deve combinar as letras i, u, d."));
                }

                ConstFalse(trigger, tPath, "a", errors);
                Int(trigger, tPath, "pos", errors);
            }
        }
    }

    private static void ValidateExtrasMap(JsonElement node, string path, string key, List<DigestValidationError> errors)
    {
        if (!node.TryGetProperty(key, out var map))
        {
            return;
        }

        if (map.ValueKind != JsonValueKind.Object)
        {
            errors.Add(Error($"{path}.{key}", ErrorCodes.DigSchema, $"'{key}' deve ser um objeto texto→texto."));
            return;
        }

        foreach (var prop in map.EnumerateObject())
        {
            if (prop.Value.ValueKind != JsonValueKind.String)
            {
                errors.Add(Error($"{path}.{key}.{prop.Name}", ErrorCodes.DigSchema, "Valores de extras devem ser texto."));
            }
        }
    }

    // ---------- Referências entre objetos (FK → tabela) ----------

    private static void ValidateReferences(JsonElement root, List<DigestValidationError> errors)
    {
        if (!root.TryGetProperty("namespaces", out var namespaces) || namespaces.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        // Tabelas existentes por namespace, para resolver FKs.
        var existing = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var ns in namespaces.EnumerateArray())
        {
            var nsName = PropertyText(ns, "name");
            var tableNames = new HashSet<string>(StringComparer.Ordinal);
            if (ns.TryGetProperty("tables", out var tables) && tables.ValueKind == JsonValueKind.Array)
            {
                foreach (var table in tables.EnumerateArray())
                {
                    var tableName = PropertyText(table, "name");
                    if (tableName is not null)
                    {
                        tableNames.Add(tableName);
                    }
                }
            }

            if (nsName is not null)
            {
                existing[nsName] = tableNames;
            }
        }

        var filtered = root.TryGetProperty("options", out var options)
            && (options.TryGetProperty("include", out _) || options.TryGetProperty("exclude", out _));

        var nsIndex = 0;
        foreach (var ns in namespaces.EnumerateArray())
        {
            if (ns.TryGetProperty("tables", out var tables) && tables.ValueKind == JsonValueKind.Array)
            {
                var tableIndex = 0;
                foreach (var table in tables.EnumerateArray())
                {
                    var tablePath = $"$.namespaces[{nsIndex}].tables[{tableIndex++}]";
                    CheckForeignKeyTargets(table, tablePath, existing, filtered, errors);
                }
            }

            nsIndex++;
        }
    }

    private static void CheckForeignKeyTargets(
        JsonElement table, string tablePath, Dictionary<string, HashSet<string>> existing, bool filtered,
        List<DigestValidationError> errors)
    {
        if (!table.TryGetProperty("fk", out var fks) || fks.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        var fkIndex = 0;
        foreach (var fk in fks.EnumerateArray())
        {
            var reference = PropertyText(fk, "r");
            if (reference is null)
            {
                continue;
            }

            var fkPath = $"{tablePath}.fk[{fkIndex++}].r";
            ObjectName target;
            try
            {
                target = ObjectName.ParseCanonical(reference);
            }
            catch (FormatException)
            {
                errors.Add(Error(fkPath, ErrorCodes.DigSchema, "Referência de FK com formato inválido."));
                continue;
            }

            if (existing.TryGetValue(target.Namespace, out var known) && known.Contains(target.Name))
            {
                continue;
            }

            if (filtered)
            {
                errors.Add(Error(fkPath, ErrorCodes.DigFkExternal,
                    $"Tabela referenciada '{target.CanonicalId}' fora do filtro de captura.", DigestSeverity.Warning));
            }
            else
            {
                errors.Add(Error(fkPath, ErrorCodes.DigFkTarget, $"Tabela referenciada '{target.CanonicalId}' não existe no digest."));
            }
        }
    }

    private static string? PropertyText(JsonElement obj, string key) =>
        obj.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    // ---------- Hash ----------

    private void ValidateHash(string json, JsonElement root, List<DigestValidationError> errors)
    {
        if (!root.TryGetProperty("hash", out var hashElement) || hashElement.ValueKind != JsonValueKind.String)
        {
            return;
        }

        var model = DigestSerializer.ReadModel(json);
        if (!model.IsSuccess)
        {
            return;
        }

        var expected = _hasher.Compute(model.Value);
        if (!string.Equals(hashElement.GetString(), expected, StringComparison.Ordinal))
        {
            errors.Add(Error("$.hash", ErrorCodes.DigHashMismatch,
                $"O hash do arquivo difere do recalculado ({expected}). O arquivo pode ter sido editado manualmente.",
                DigestSeverity.Warning));
        }
    }

    // ---------- Auxiliares de verificação ----------

    private static DigestValidationError Error(string path, string code, string message, DigestSeverity severity = DigestSeverity.Error) =>
        new(path, code, message, severity);

    private static bool IsObject(JsonElement element, string path, List<DigestValidationError> errors)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            return true;
        }

        errors.Add(Error(path, ErrorCodes.DigSchema, "Esperado um objeto."));
        return false;
    }

    private static void CheckKeys(JsonElement obj, string path, HashSet<string> allowed, string[] required, List<DigestValidationError> errors)
    {
        if (obj.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        foreach (var prop in obj.EnumerateObject())
        {
            if (!allowed.Contains(prop.Name))
            {
                errors.Add(Error($"{path}.{prop.Name}", ErrorCodes.DigSchema, $"Chave desconhecida '{prop.Name}'."));
            }
        }

        foreach (var key in required)
        {
            if (!obj.TryGetProperty(key, out _))
            {
                errors.Add(Error($"{path}.{key}", ErrorCodes.DigSchema, $"Campo obrigatório ausente: '{key}'."));
            }
        }
    }

    private static string? Str(JsonElement obj, string path, string key, List<DigestValidationError> errors)
    {
        if (!obj.TryGetProperty(key, out var value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.String)
        {
            return value.GetString();
        }

        errors.Add(Error($"{path}.{key}", ErrorCodes.DigSchema, $"'{key}' deve ser texto."));
        return null;
    }

    private static string? Identifier(JsonElement obj, string path, string key, List<DigestValidationError> errors)
    {
        var value = Str(obj, path, key, errors);
        if (value is not null && !IdentifierRegex.IsMatch(value))
        {
            errors.Add(Error($"{path}.{key}", ErrorCodes.DigSchema, $"'{key}' deve ter de 1 a 255 caracteres."));
        }

        return value;
    }

    private static List<string>? IdentifierList(JsonElement obj, string path, string key, List<DigestValidationError> errors)
    {
        if (!obj.TryGetProperty(key, out var value))
        {
            return null;
        }

        if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() == 0)
        {
            errors.Add(Error($"{path}.{key}", ErrorCodes.DigSchema, $"'{key}' deve ser uma lista não vazia de identificadores."));
            return null;
        }

        var list = new List<string>();
        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String)
            {
                list.Add(item.GetString()!);
            }
            else
            {
                errors.Add(Error($"{path}.{key}", ErrorCodes.DigSchema, $"Itens de '{key}' devem ser texto."));
            }
        }

        return list;
    }

    private static JsonElement? ArrayOf(JsonElement obj, string path, string key, List<DigestValidationError> errors, int minItems = 0)
    {
        if (!obj.TryGetProperty(key, out var value))
        {
            return null;
        }

        if (value.ValueKind != JsonValueKind.Array)
        {
            errors.Add(Error($"{path}.{key}", ErrorCodes.DigSchema, $"'{key}' deve ser um array."));
            return null;
        }

        if (value.GetArrayLength() < minItems)
        {
            errors.Add(Error($"{path}.{key}", ErrorCodes.DigSchema, $"'{key}' deve ter ao menos {minItems} item(ns)."));
        }

        return value;
    }

    private static void StringArray(JsonElement obj, string path, string key, List<DigestValidationError> errors, int minItems)
    {
        var list = ArrayOf(obj, path, key, errors, minItems);
        if (list is null)
        {
            return;
        }

        foreach (var item in list.Value.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
            {
                errors.Add(Error($"{path}.{key}", ErrorCodes.DigSchema, $"Itens de '{key}' devem ser texto."));
                return;
            }
        }
    }

    private static void Bool(JsonElement obj, string path, string key, List<DigestValidationError> errors)
    {
        if (obj.TryGetProperty(key, out var value) && value.ValueKind != JsonValueKind.True && value.ValueKind != JsonValueKind.False)
        {
            errors.Add(Error($"{path}.{key}", ErrorCodes.DigSchema, $"'{key}' deve ser booleano."));
        }
    }

    private static void Int(JsonElement obj, string path, string key, List<DigestValidationError> errors, int? minimum = null)
    {
        if (!obj.TryGetProperty(key, out var value))
        {
            return;
        }

        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var number))
        {
            errors.Add(Error($"{path}.{key}", ErrorCodes.DigSchema, $"'{key}' deve ser inteiro."));
        }
        else if (minimum is { } min && number < min)
        {
            errors.Add(Error($"{path}.{key}", ErrorCodes.DigSchema, $"'{key}' deve ser ≥ {min}."));
        }
    }

    private static void EnumStr(JsonElement obj, string path, string key, string[] allowed, List<DigestValidationError> errors)
    {
        if (!obj.TryGetProperty(key, out var value))
        {
            return;
        }

        if (value.ValueKind != JsonValueKind.String || Array.IndexOf(allowed, value.GetString()) < 0)
        {
            errors.Add(Error($"{path}.{key}", ErrorCodes.DigSchema, $"'{key}' fora dos valores permitidos."));
        }
    }

    private static void ConstTrue(JsonElement obj, string path, string key, List<DigestValidationError> errors)
    {
        if (obj.TryGetProperty(key, out var value) && value.ValueKind != JsonValueKind.True)
        {
            errors.Add(Error($"{path}.{key}", ErrorCodes.DigSchema, $"'{key}' deve ser true quando presente."));
        }
    }

    private static void ConstFalse(JsonElement obj, string path, string key, List<DigestValidationError> errors)
    {
        if (obj.TryGetProperty(key, out var value) && value.ValueKind != JsonValueKind.False)
        {
            errors.Add(Error($"{path}.{key}", ErrorCodes.DigSchema, $"'{key}' deve ser false quando presente."));
        }
    }

    private static void ReportMissing(IEnumerable<string> names, HashSet<string> columns, string path, List<DigestValidationError> errors)
    {
        foreach (var name in names)
        {
            if (!columns.Contains(name))
            {
                errors.Add(Error(path, ErrorCodes.DigPkCol, $"Coluna '{name}' não existe na tabela."));
            }
        }
    }

    private static string StripDesc(string entry) =>
        entry.EndsWith(" desc", StringComparison.Ordinal) ? entry[..^" desc".Length] : entry;

    [GeneratedRegex(@"^(?<major>\d+)\.(?<minor>\d+)$", RegexOptions.CultureInvariant)]
    private static partial Regex VersionPattern();

    [GeneratedRegex(@"^[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}Z$", RegexOptions.CultureInvariant)]
    private static partial Regex CapturedAtPattern();

    [GeneratedRegex("^sha256(v[0-9]+)?:[0-9a-f]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex HashPattern();

    [GeneratedRegex("^i?u?d?$", RegexOptions.CultureInvariant)]
    private static partial Regex EventsPattern();
}
