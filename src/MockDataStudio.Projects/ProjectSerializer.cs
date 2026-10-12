using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using MockDataStudio.Core.Bindings;
using MockDataStudio.Core.Common;
using MockDataStudio.Core.Digest;
using MockDataStudio.Core.Hashing;
using MockDataStudio.Core.Naming;
using MockDataStudio.Core.Profiles;

namespace MockDataStudio.Projects;

/// <summary>
/// Serializador do formato <c>mds.project</c> 1.0 (docs/22). Gera JSON estável para diffs: UTF-8, 2 espaços, "\n",
/// chaves de mapas em ordem ordinal. Valores padrão de relações e opções de saída são omitidos.
/// </summary>
public sealed class ProjectSerializer(IDigestValidator? digestValidator = null) : IProjectSerializer
{
    public const string FormatId = "mds.project";
    public const int SupportedMajor = 1;

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        IndentSize = 2,
        NewLine = "\n",
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static readonly HashSet<string> RootKeys = new(StringComparer.Ordinal)
    {
        "format", "formatVersion", "id", "name", "createdAt", "modifiedAt", "toolVersion", "connections", "snapshots",
        "activeSnapshotId", "bindings", "projectRules", "profiles", "activeProfile", "catalogDependencies", "runs", "notes",
    };

    private readonly IDigestValidator _digestValidator = digestValidator ?? new DigestValidator(new StructureHasher());

    public string CurrentFormatVersion => "1.0";

    // ---------------------------------------------------------------- escrita

    public string Serialize(MdsProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        var root = new JsonObject
        {
            ["format"] = FormatId,
            ["formatVersion"] = project.FormatVersion,
            ["id"] = project.Id.ToString(),
            ["name"] = project.Name,
            ["createdAt"] = Instant(project.CreatedAt),
            ["modifiedAt"] = Instant(project.ModifiedAt),
        };
        Put(root, "toolVersion", project.ToolVersion);

        var connections = new JsonObject();
        PutNode(connections, "source", project.Connections.Source is { } s ? ConnectionNode(s) : null);
        PutNode(connections, "target", project.Connections.Target is { } t ? ConnectionNode(t) : null);
        PutNode(connections, "reference", project.Connections.Reference is { } r ? ConnectionNode(r) : null);
        root["connections"] = connections;

        var snapshots = new JsonArray();
        foreach (var snapshot in project.Snapshots)
        {
            var node = new JsonObject
            {
                ["id"] = snapshot.Id,
                ["label"] = snapshot.Label,
                ["capturedAt"] = Instant(snapshot.CapturedAt),
                ["structureHash"] = snapshot.StructureHash,
            };
            Put(node, "sourceFingerprint", snapshot.SourceFingerprintHash);
            node["digest"] = JsonNode.Parse(snapshot.DigestJson);
            snapshots.Add(node);
        }

        root["snapshots"] = snapshots;
        Put(root, "activeSnapshotId", project.ActiveSnapshotId);

        var bindings = new JsonObject();
        foreach (var (key, binding) in project.Bindings.OrderBy(b => b.Key, StringComparer.Ordinal))
        {
            bindings[key] = BindingNode(binding);
        }

        root["bindings"] = bindings;

        if (project.ProjectRules is { Count: > 0 } rules)
        {
            var array = new JsonArray();
            foreach (var rule in rules)
            {
                var node = new JsonObject { ["columnPattern"] = rule.ColumnPattern };
                Put(node, "tablePattern", rule.TablePattern);
                node["targetKind"] = rule.TargetKind;
                node["targetRef"] = rule.TargetRef;
                PutNode(node, "parameters", rule.Parameters?.DeepClone());
                if (rule.Priority is { } priority)
                {
                    node["priority"] = priority;
                }

                array.Add(node);
            }

            root["projectRules"] = array;
        }

        var profiles = new JsonArray();
        foreach (var profile in project.Profiles)
        {
            profiles.Add(ProfileNode(profile));
        }

        root["profiles"] = profiles;
        root["activeProfile"] = project.ActiveProfile;

        if (project.CatalogDependencies.Count > 0)
        {
            var array = new JsonArray();
            foreach (var dependency in project.CatalogDependencies)
            {
                array.Add(new JsonObject
                {
                    ["templateCode"] = dependency.TemplateCode,
                    ["contentHash"] = dependency.ContentHash,
                    ["valueCount"] = dependency.ValueCount,
                });
            }

            root["catalogDependencies"] = array;
        }

        if (project.Runs.Count > 0)
        {
            var array = new JsonArray();
            foreach (var run in project.Runs)
            {
                var node = new JsonObject
                {
                    ["runId"] = run.RunId.ToString(),
                    ["profileName"] = run.ProfileName,
                    ["startedAt"] = Instant(run.StartedAt),
                    ["status"] = run.Status,
                    ["rows"] = run.Rows,
                    ["planHash"] = run.PlanHash,
                    ["seed"] = run.Seed.ToString(CultureInfo.InvariantCulture),
                    ["outputKind"] = run.OutputKind,
                };
                Put(node, "targetFingerprint", run.TargetFingerprintHash);
                array.Add(node);
            }

            root["runs"] = array;
        }

        Put(root, "notes", project.Notes);
        return root.ToJsonString(WriteOptions);
    }

    private static JsonObject ConnectionNode(ConnectionRef reference)
    {
        var node = new JsonObject();
        if (reference.ProfileId is { } id)
        {
            node["profileId"] = id.ToString();
        }

        node["displayName"] = reference.DisplayName;
        node["engine"] = reference.Engine.ToString().ToLowerInvariant();
        Put(node, "fingerprint", reference.FingerprintHash);
        return node;
    }

    private static JsonObject BindingNode(ColumnBinding binding)
    {
        var node = new JsonObject { ["generator"] = GeneratorNode(binding.Generator) };
        node["origin"] = Camel(binding.Origin);
        Put(node, "confidence", binding.ConfidenceLabel?.ToLowerInvariant());
        node["columnFingerprint"] = binding.ColumnFingerprint;
        if (binding.NullRate != 0)
        {
            node["nullRate"] = binding.NullRate;
        }

        if (binding.State != BindingState.Active)
        {
            node["state"] = Camel(binding.State);
        }

        if (binding.Unique)
        {
            node["unique"] = true;
        }

        Put(node, "note", binding.Note);
        return node;
    }

    private static JsonObject GeneratorNode(GeneratorSpec spec)
    {
        var node = new JsonObject
        {
            ["generatorId"] = spec.GeneratorId,
            ["version"] = spec.Version,
            ["parameters"] = spec.Parameters.DeepClone(),
        };
        if (spec.Children is { Count: > 0 } children)
        {
            var array = new JsonArray();
            foreach (var child in children)
            {
                array.Add(GeneratorNode(child));
            }

            node["children"] = array;
        }

        return node;
    }

    private static JsonObject ProfileNode(GenerationProfile profile)
    {
        var node = new JsonObject
        {
            ["name"] = profile.Name,
            ["seed"] = profile.Seed.ToString(CultureInfo.InvariantCulture),
            ["referenceInstant"] = Instant(profile.ReferenceInstant),
            ["referenceInstantMode"] = Camel(profile.ReferenceInstantMode),
            ["timeZoneId"] = profile.TimeZoneId,
            ["defaultKeyStrategy"] = Camel(profile.DefaultKeyStrategy),
            ["keyOffset"] = profile.KeyOffset,
        };

        var tables = new JsonObject();
        foreach (var (key, table) in profile.Tables.OrderBy(t => t.Key, StringComparer.Ordinal))
        {
            var tableNode = new JsonObject { ["include"] = table.Include, ["rowCount"] = table.RowCount };
            if (table.KeyStrategy is { } strategy)
            {
                tableNode["keyStrategy"] = Camel(strategy);
            }

            if (table.KeyStartOverride is { } start)
            {
                tableNode["keyStartOverride"] = start;
            }

            if (table.Relations is { Count: > 0 } relations)
            {
                var relationsNode = new JsonObject();
                foreach (var (name, relation) in relations.OrderBy(r => r.Key, StringComparer.Ordinal))
                {
                    relationsNode[name] = RelationNode(relation);
                }

                tableNode["relations"] = relationsNode;
            }

            tables[key] = tableNode;
        }

        node["tables"] = tables;
        node["validation"] = new JsonObject
        {
            ["trialInsert"] = profile.Validation.TrialInsert,
            ["trialRows"] = profile.Validation.TrialRows,
            ["verifyReferencesInTarget"] = profile.Validation.VerifyReferencesInTarget,
        };
        node["output"] = OutputNode(profile.Output);
        return node;
    }

    private static JsonObject RelationNode(RelationSettings relation)
    {
        var node = new JsonObject { ["source"] = Camel(relation.Source) };

        var cardinality = new JsonObject { ["mode"] = Camel(relation.Cardinality.Mode) };
        var distribution = relation.Cardinality.Distribution;
        var distributionNode = new JsonObject { ["kind"] = Camel(distribution.Kind) };
        if (distribution.ZipfExponent is { } zipf)
        {
            distributionNode["zipfExponent"] = zipf;
        }

        if (distribution.Min is { } min)
        {
            distributionNode["min"] = min;
        }

        if (distribution.Max is { } max)
        {
            distributionNode["max"] = max;
        }

        if (distribution.Lambda is { } lambda)
        {
            distributionNode["lambda"] = lambda;
        }

        if (distribution.Weights is { Count: > 0 } weights)
        {
            var array = new JsonArray();
            foreach (var weight in weights)
            {
                array.Add(weight);
            }

            distributionNode["weights"] = array;
        }

        cardinality["distribution"] = distributionNode;
        if (relation.Cardinality.OneToOne)
        {
            cardinality["oneToOne"] = true;
        }

        node["cardinality"] = cardinality;

        if (relation.FixedValues is { Count: > 0 } fixedValues)
        {
            var array = new JsonArray();
            foreach (var tuple in fixedValues)
            {
                array.Add(tuple.DeepClone());
            }

            node["fixedValues"] = array;
        }

        Put(node, "datasetPath", relation.DatasetPath);
        Put(node, "query", relation.Query);
        if (relation.ExistingLimit != 100_000)
        {
            node["existingLimit"] = relation.ExistingLimit;
        }

        if (relation.NullRate != 0)
        {
            node["nullRate"] = relation.NullRate;
        }

        if (relation.CycleBreak is { } cycle)
        {
            node["cycleBreak"] = Camel(cycle);
        }

        if (relation.RootRatio != 0.1)
        {
            node["rootRatio"] = relation.RootRatio;
        }

        if (relation.MaxDepth != 6)
        {
            node["maxDepth"] = relation.MaxDepth;
        }

        return node;
    }

    private static JsonObject OutputNode(OutputSettings output)
    {
        var node = new JsonObject { ["kind"] = Camel(output.Kind) };
        Put(node, "directory", output.Directory);
        Put(node, "fileNamePattern", output.FileNamePattern);
        node["segmentMaxBytes"] = output.SegmentMaxBytes;
        node["rowsPerStatement"] = output.RowsPerStatement;
        node["batchSize"] = output.BatchSize;
        node["transactionMode"] = Camel(output.TransactionMode);
        node["includeCleanupScript"] = output.IncludeCleanupScript;
        node["resyncSequences"] = output.ResyncSequences;
        node["encoding"] = output.Encoding;
        node["emitGuards"] = output.EmitGuards;
        if (output.AllowDisableFkChecks)
        {
            node["allowDisableFkChecks"] = true;
        }

        if (output.QualifyNamespace)
        {
            node["qualifyNamespace"] = true;
        }

        if (!output.QuoteAllIdentifiers)
        {
            node["quoteAllIdentifiers"] = false;
        }

        if (!output.IncludeTransaction)
        {
            node["includeTransaction"] = false;
        }

        if (output.WriteBom)
        {
            node["writeBom"] = true;
        }

        if (output.LineEnding != "\n")
        {
            node["lineEnding"] = output.LineEnding;
        }

        if (output.CsvSeparator != ",")
        {
            node["csvSeparator"] = output.CsvSeparator;
        }

        return node;
    }

    // ---------------------------------------------------------------- leitura

    public Result<MdsProject> Deserialize(string json)
    {
        JsonNode? parsed;
        try
        {
            parsed = JsonNode.Parse(json);
        }
        catch (JsonException ex)
        {
            return Result<MdsProject>.Fail(ErrorCodes.PrjJson, "O arquivo não é um JSON válido: " + ex.Message);
        }

        if (parsed is not JsonObject root)
        {
            return Result<MdsProject>.Fail(ErrorCodes.PrjJson, "O arquivo de projeto deve conter um objeto JSON.");
        }

        try
        {
            return Map(root);
        }
        catch (ProjectFormatException ex)
        {
            return Result<MdsProject>.Fail(ex.Code, ex.Message);
        }
    }

    private Result<MdsProject> Map(JsonObject root)
    {
        foreach (var key in root.Select(p => p.Key).Where(k => !RootKeys.Contains(k)))
        {
            throw new ProjectFormatException("$." + key, "propriedade desconhecida.");
        }

        if (Str(root, "$", "format") != FormatId)
        {
            throw new ProjectFormatException("$.format", $"deve ser \"{FormatId}\".");
        }

        var version = Str(root, "$", "formatVersion");
        var parts = version.Split('.');
        if (parts.Length != 2 || !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var major)
            || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out _))
        {
            throw new ProjectFormatException("$.formatVersion", "deve ter o formato MAJOR.MINOR.");
        }

        if (major > SupportedMajor)
        {
            return Result<MdsProject>.Fail(ErrorCodes.PrjMajorUnsupported,
                $"O projeto usa o formato {version}, mais novo que o suportado ({CurrentFormatVersion}).");
        }

        var name = Str(root, "$", "name");
        if (name.Length is < 1 or > 200)
        {
            throw new ProjectFormatException("$.name", "deve ter de 1 a 200 caracteres.");
        }

        var connectionsNode = Obj(root, "$", "connections", required: false);
        var connections = connectionsNode is null
            ? ProjectConnections.Empty
            : new ProjectConnections(
                ConnectionOf(connectionsNode, "$.connections", "source"),
                ConnectionOf(connectionsNode, "$.connections", "target"),
                ConnectionOf(connectionsNode, "$.connections", "reference"));

        var snapshots = new List<SnapshotEntry>();
        var snapshotsNode = Arr(root, "$", "snapshots", required: true)!;
        for (var i = 0; i < snapshotsNode.Count; i++)
        {
            var path = $"$.snapshots[{i}]";
            var item = snapshotsNode[i] as JsonObject ?? throw new ProjectFormatException(path, "deve ser um objeto.");
            var digest = item["digest"] as JsonObject ?? throw new ProjectFormatException(path + ".digest", "é obrigatório e deve ser um objeto.");
            var digestJson = digest.ToJsonString();

            foreach (var error in _digestValidator.Validate(digestJson).Where(e => e.Severity == DigestSeverity.Error))
            {
                throw new ProjectFormatException(path + ".digest" + error.JsonPath.TrimStart('$'), error.Message, ErrorCodes.PrjDigest);
            }

            snapshots.Add(new SnapshotEntry(
                Str(item, path, "id"), Str(item, path, "label"), Time(item, path, "capturedAt"),
                Str(item, path, "structureHash"), StrOpt(item, "sourceFingerprint"), digestJson));
        }

        var bindings = new Dictionary<string, ColumnBinding>(StringComparer.Ordinal);
        var bindingsNode = Obj(root, "$", "bindings", required: true)!;
        foreach (var (key, value) in bindingsNode)
        {
            var path = $"$.bindings.{key}";
            var item = value as JsonObject ?? throw new ProjectFormatException(path, "deve ser um objeto.");
            bindings[key] = MapBinding(key, item, path);
        }

        var rules = new List<ProjectRule>();
        var rulesNode = Arr(root, "$", "projectRules", required: false);
        for (var i = 0; rulesNode is not null && i < rulesNode.Count; i++)
        {
            var path = $"$.projectRules[{i}]";
            var item = rulesNode[i] as JsonObject ?? throw new ProjectFormatException(path, "deve ser um objeto.");
            rules.Add(new ProjectRule(
                Str(item, path, "columnPattern"), StrOpt(item, "tablePattern"), Str(item, path, "targetKind"),
                Str(item, path, "targetRef"), item["parameters"] as JsonObject is { } p ? (JsonObject)p.DeepClone() : null,
                item["priority"] is JsonValue pv && pv.TryGetValue<int>(out var priority) ? priority : null));
        }

        var profiles = new List<GenerationProfile>();
        var profilesNode = Arr(root, "$", "profiles", required: true)!;
        if (profilesNode.Count == 0)
        {
            throw new ProjectFormatException("$.profiles", "deve ter ao menos um perfil.");
        }

        for (var i = 0; i < profilesNode.Count; i++)
        {
            var path = $"$.profiles[{i}]";
            profiles.Add(MapProfile(profilesNode[i] as JsonObject ?? throw new ProjectFormatException(path, "deve ser um objeto."), path));
        }

        var activeProfile = Str(root, "$", "activeProfile");
        if (profiles.All(p => p.Name != activeProfile))
        {
            throw new ProjectFormatException("$.activeProfile", $"o perfil \"{activeProfile}\" não existe em profiles.");
        }

        var activeSnapshot = StrOpt(root, "activeSnapshotId");
        if (activeSnapshot is not null && snapshots.All(s => s.Id != activeSnapshot))
        {
            throw new ProjectFormatException("$.activeSnapshotId", $"o snapshot \"{activeSnapshot}\" não existe em snapshots.");
        }

        var dependencies = new List<CatalogDependency>();
        var dependenciesNode = Arr(root, "$", "catalogDependencies", required: false);
        for (var i = 0; dependenciesNode is not null && i < dependenciesNode.Count; i++)
        {
            var path = $"$.catalogDependencies[{i}]";
            var item = dependenciesNode[i] as JsonObject ?? throw new ProjectFormatException(path, "deve ser um objeto.");
            dependencies.Add(new CatalogDependency(Str(item, path, "templateCode"), Str(item, path, "contentHash"), Long(item, path, "valueCount")));
        }

        var runs = new List<RunSummary>();
        var runsNode = Arr(root, "$", "runs", required: false);
        for (var i = 0; runsNode is not null && i < runsNode.Count; i++)
        {
            var path = $"$.runs[{i}]";
            var item = runsNode[i] as JsonObject ?? throw new ProjectFormatException(path, "deve ser um objeto.");
            runs.Add(new RunSummary(
                GuidOf(item, path, "runId"), Str(item, path, "profileName"), Time(item, path, "startedAt"), Str(item, path, "status"),
                Long(item, path, "rows"), Str(item, path, "planHash"), Seed(item, path, "seed"), Str(item, path, "outputKind"),
                StrOpt(item, "targetFingerprint")));
        }

        var project = new MdsProject(
            GuidOf(root, "$", "id"), name, version, Time(root, "$", "createdAt"), Time(root, "$", "modifiedAt"),
            connections, snapshots, activeSnapshot, bindings, profiles, activeProfile, dependencies, runs,
            StrOpt(root, "notes"), rules, StrOpt(root, "toolVersion"));
        return Result<MdsProject>.Ok(project);
    }

    private static ConnectionRef? ConnectionOf(JsonObject parent, string parentPath, string key)
    {
        if (parent[key] is null)
        {
            return null;
        }

        var path = $"{parentPath}.{key}";
        var node = parent[key] as JsonObject ?? throw new ProjectFormatException(path, "deve ser um objeto.");
        Guid? profileId = node["profileId"] is null ? null : GuidOf(node, path, "profileId");
        var engineText = Str(node, path, "engine");
        if (!Enum.TryParse<DatabaseEngine>(engineText, ignoreCase: true, out var engine))
        {
            throw new ProjectFormatException(path + ".engine", $"valor desconhecido: \"{engineText}\".");
        }

        return new ConnectionRef(profileId, Str(node, path, "displayName"), engine, StrOpt(node, "fingerprint"));
    }

    private static ColumnBinding MapBinding(string key, JsonObject node, string path)
    {
        ColumnRef column;
        try
        {
            column = ColumnRef.ParseCanonical(key);
        }
        catch (FormatException ex)
        {
            throw new ProjectFormatException(path, ex.Message);
        }

        var generator = MapGenerator(node["generator"] as JsonObject ?? throw new ProjectFormatException(path + ".generator", "é obrigatório."), path + ".generator");
        var nullRate = node["nullRate"] is JsonValue nv && nv.TryGetValue<double>(out var rate) ? rate : 0;
        if (nullRate is < 0 or > 1)
        {
            throw new ProjectFormatException(path + ".nullRate", "deve estar entre 0 e 1.");
        }

        var state = node["state"] is null ? BindingState.Active : EnumOf<BindingState>(node, path, "state");
        return new ColumnBinding(
            column, generator, nullRate, EnumOf<BindingOrigin>(node, path, "origin"), Str(node, path, "columnFingerprint"), state,
            StrOpt(node, "confidence"), node["unique"] is JsonValue uv && uv.TryGetValue<bool>(out var unique) && unique, StrOpt(node, "note"));
    }

    private static GeneratorSpec MapGenerator(JsonObject node, string path)
    {
        var parameters = node["parameters"] as JsonObject is { } p ? (JsonObject)p.DeepClone() : new JsonObject();
        List<GeneratorSpec>? children = null;
        if (node["children"] is JsonArray array)
        {
            children = [];
            for (var i = 0; i < array.Count; i++)
            {
                children.Add(MapGenerator(array[i] as JsonObject ?? throw new ProjectFormatException($"{path}.children[{i}]", "deve ser um objeto."), $"{path}.children[{i}]"));
            }
        }

        return new GeneratorSpec(Str(node, path, "generatorId"), (int)Long(node, path, "version"), parameters, children);
    }

    private static GenerationProfile MapProfile(JsonObject node, string path)
    {
        var tables = new Dictionary<string, TableSettings>(StringComparer.Ordinal);
        foreach (var (key, value) in Obj(node, path, "tables", required: true)!)
        {
            var tablePath = $"{path}.tables.{key}";
            var tableNode = value as JsonObject ?? throw new ProjectFormatException(tablePath, "deve ser um objeto.");
            Dictionary<string, RelationSettings>? relations = null;
            if (tableNode["relations"] is JsonObject relationsNode)
            {
                relations = new Dictionary<string, RelationSettings>(StringComparer.Ordinal);
                foreach (var (name, relationValue) in relationsNode)
                {
                    var relationPath = $"{tablePath}.relations.{name}";
                    relations[name] = MapRelation(name, relationValue as JsonObject ?? throw new ProjectFormatException(relationPath, "deve ser um objeto."), relationPath);
                }
            }

            tables[key] = new TableSettings(
                Bool(tableNode, tablePath, "include"), Long(tableNode, tablePath, "rowCount"),
                tableNode["keyStrategy"] is null ? null : EnumOf<KeyStrategy>(tableNode, tablePath, "keyStrategy"),
                tableNode["keyStartOverride"] is JsonValue ko && ko.TryGetValue<long>(out var start) ? start : null, relations);
        }

        var validationNode = node["validation"] as JsonObject;
        var validation = validationNode is null
            ? new ValidationSettings()
            : new ValidationSettings(
                BoolOr(validationNode, "trialInsert", false), (int)LongOr(validationNode, "trialRows", 50),
                BoolOr(validationNode, "verifyReferencesInTarget", true));

        var outputNode = node["output"] as JsonObject ?? throw new ProjectFormatException(path + ".output", "é obrigatório.");
        var outputPath = path + ".output";
        var defaults = new OutputSettings(EnumOf<OutputKind>(outputNode, outputPath, "kind"));
        var output = defaults with
        {
            Directory = StrOpt(outputNode, "directory"),
            FileNamePattern = outputNode.ContainsKey("fileNamePattern") ? StrOpt(outputNode, "fileNamePattern") : defaults.FileNamePattern,
            SegmentMaxBytes = LongOr(outputNode, "segmentMaxBytes", defaults.SegmentMaxBytes),
            RowsPerStatement = (int)LongOr(outputNode, "rowsPerStatement", defaults.RowsPerStatement),
            BatchSize = (int)LongOr(outputNode, "batchSize", defaults.BatchSize),
            TransactionMode = outputNode["transactionMode"] is null ? defaults.TransactionMode : EnumOf<DirectTransactionMode>(outputNode, outputPath, "transactionMode"),
            IncludeCleanupScript = BoolOr(outputNode, "includeCleanupScript", defaults.IncludeCleanupScript),
            ResyncSequences = BoolOr(outputNode, "resyncSequences", defaults.ResyncSequences),
            Encoding = StrOpt(outputNode, "encoding") ?? defaults.Encoding,
            EmitGuards = BoolOr(outputNode, "emitGuards", defaults.EmitGuards),
            AllowDisableFkChecks = BoolOr(outputNode, "allowDisableFkChecks", defaults.AllowDisableFkChecks),
            QualifyNamespace = BoolOr(outputNode, "qualifyNamespace", defaults.QualifyNamespace),
            QuoteAllIdentifiers = BoolOr(outputNode, "quoteAllIdentifiers", defaults.QuoteAllIdentifiers),
            IncludeTransaction = BoolOr(outputNode, "includeTransaction", defaults.IncludeTransaction),
            WriteBom = BoolOr(outputNode, "writeBom", defaults.WriteBom),
            LineEnding = StrOpt(outputNode, "lineEnding") ?? defaults.LineEnding,
            CsvSeparator = StrOpt(outputNode, "csvSeparator") ?? defaults.CsvSeparator,
        };

        return new GenerationProfile(
            Str(node, path, "name"), Seed(node, path, "seed"), Time(node, path, "referenceInstant"),
            node["referenceInstantMode"] is null ? ReferenceInstantMode.Fixed : EnumOf<ReferenceInstantMode>(node, path, "referenceInstantMode"),
            StrOpt(node, "timeZoneId") ?? "UTC", tables,
            node["defaultKeyStrategy"] is null ? KeyStrategy.Explicit : EnumOf<KeyStrategy>(node, path, "defaultKeyStrategy"),
            output, validation, LongOr(node, "keyOffset", 0));
    }

    private static RelationSettings MapRelation(string name, JsonObject node, string path)
    {
        var cardinalityNode = node["cardinality"] as JsonObject ?? throw new ProjectFormatException(path + ".cardinality", "é obrigatório.");
        var cardinalityPath = path + ".cardinality";
        var distributionNode = cardinalityNode["distribution"] as JsonObject ?? throw new ProjectFormatException(cardinalityPath + ".distribution", "é obrigatório.");
        var distributionPath = cardinalityPath + ".distribution";

        List<double>? weights = null;
        if (distributionNode["weights"] is JsonArray weightsNode)
        {
            weights = weightsNode.Select(w => w?.GetValue<double>() ?? 0).ToList();
        }

        var distributionResult = DistributionSpec.Create(
            EnumOf<DistributionKind>(distributionNode, distributionPath, "kind"),
            DoubleOpt(distributionNode, "zipfExponent"),
            distributionNode["min"] is JsonValue mn && mn.TryGetValue<int>(out var min) ? min : null,
            distributionNode["max"] is JsonValue mx && mx.TryGetValue<int>(out var max) ? max : null,
            DoubleOpt(distributionNode, "lambda"), weights);
        if (!distributionResult.IsSuccess)
        {
            throw new ProjectFormatException(distributionPath, distributionResult.Error.Message);
        }

        var cardinality = new CardinalitySpec(
            EnumOf<CardinalityMode>(cardinalityNode, cardinalityPath, "mode"), distributionResult.Value, BoolOr(cardinalityNode, "oneToOne", false));

        List<JsonArray>? fixedValues = null;
        if (node["fixedValues"] is JsonArray fixedNode)
        {
            fixedValues = fixedNode.Select(t => t as JsonArray is { } a ? (JsonArray)a.DeepClone() : []).ToList();
        }

        return new RelationSettings(
            name, EnumOf<ReferenceSourceKind>(node, path, "source"), cardinality, fixedValues,
            StrOpt(node, "datasetPath"), StrOpt(node, "query"), LongOr(node, "existingLimit", 100_000),
            DoubleOpt(node, "nullRate") ?? 0,
            node["cycleBreak"] is null ? null : EnumOf<CycleBreakStrategy>(node, path, "cycleBreak"),
            DoubleOpt(node, "rootRatio") ?? 0.1, (int)LongOr(node, "maxDepth", 6));
    }

    // ---------------------------------------------------------------- utilitários

    private static string Instant(DateTimeOffset value)
    {
        var text = value.ToString("yyyy-MM-dd'T'HH:mm:ss.FFFFFFF", CultureInfo.InvariantCulture);
        return value.Offset == TimeSpan.Zero ? text + "Z" : text + value.ToString("zzz", CultureInfo.InvariantCulture);
    }

    private static string Camel<T>(T value) where T : struct, Enum
    {
        var text = value.ToString();
        return char.ToLowerInvariant(text[0]) + text[1..];
    }

    private static void Put(JsonObject node, string key, string? value)
    {
        if (value is not null)
        {
            node[key] = value;
        }
    }

    private static void PutNode(JsonObject node, string key, JsonNode? value)
    {
        if (value is not null)
        {
            node[key] = value;
        }
    }

    private static string Str(JsonObject node, string path, string key) =>
        StrOpt(node, key) ?? throw new ProjectFormatException($"{path}.{key}", "é obrigatório e deve ser texto.");

    private static string? StrOpt(JsonObject node, string key) =>
        node[key] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    private static long Long(JsonObject node, string path, string key) =>
        node[key] is JsonValue value && value.TryGetValue<long>(out var number)
            ? number
            : throw new ProjectFormatException($"{path}.{key}", "é obrigatório e deve ser inteiro.");

    private static long LongOr(JsonObject node, string key, long fallback) =>
        node[key] is JsonValue value && value.TryGetValue<long>(out var number) ? number : fallback;

    private static double? DoubleOpt(JsonObject node, string key) =>
        node[key] is JsonValue value && value.TryGetValue<double>(out var number) ? number : null;

    private static bool Bool(JsonObject node, string path, string key) =>
        node[key] is JsonValue value && value.TryGetValue<bool>(out var flag)
            ? flag
            : throw new ProjectFormatException($"{path}.{key}", "é obrigatório e deve ser booleano.");

    private static bool BoolOr(JsonObject node, string key, bool fallback) =>
        node[key] is JsonValue value && value.TryGetValue<bool>(out var flag) ? flag : fallback;

    private static Guid GuidOf(JsonObject node, string path, string key) =>
        Guid.TryParse(StrOpt(node, key), out var guid) ? guid : throw new ProjectFormatException($"{path}.{key}", "deve ser um GUID.");

    private static DateTimeOffset Time(JsonObject node, string path, string key) =>
        DateTimeOffset.TryParse(StrOpt(node, key), CultureInfo.InvariantCulture, DateTimeStyles.None, out var instant)
            ? instant
            : throw new ProjectFormatException($"{path}.{key}", "deve ser um instante ISO 8601.");

    private static ulong Seed(JsonObject node, string path, string key) =>
        ulong.TryParse(StrOpt(node, key), NumberStyles.None, CultureInfo.InvariantCulture, out var seed)
            ? seed
            : throw new ProjectFormatException($"{path}.{key}", "deve ser um inteiro decimal sem sinal, em texto.");

    private static T EnumOf<T>(JsonObject node, string path, string key) where T : struct, Enum
    {
        var text = Str(node, path, key);
        return Enum.TryParse<T>(text, ignoreCase: true, out var value) && Enum.IsDefined(value)
            ? value
            : throw new ProjectFormatException($"{path}.{key}", $"valor desconhecido: \"{text}\".");
    }

    private static JsonObject? Obj(JsonObject node, string path, string key, bool required) =>
        node[key] switch
        {
            JsonObject obj => obj,
            null when !required => null,
            _ => throw new ProjectFormatException($"{path}.{key}", "é obrigatório e deve ser um objeto."),
        };

    private static JsonArray? Arr(JsonObject node, string path, string key, bool required) =>
        node[key] switch
        {
            JsonArray array => array,
            null when !required => null,
            _ => throw new ProjectFormatException($"{path}.{key}", "é obrigatório e deve ser uma lista."),
        };

    private sealed class ProjectFormatException(string jsonPath, string message, string code = ErrorCodes.PrjSchema)
        : Exception($"{jsonPath}: {message}")
    {
        public string Code { get; } = code;
    }
}
