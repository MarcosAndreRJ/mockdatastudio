using System.Text.Json.Nodes;
using MockDataStudio.Core.Naming;

namespace MockDataStudio.Core.Bindings;

// Vínculo coluna → gerador (C05 §1). Definido no Core porque Mapping, Generation e Projects o consomem (AR-02).

/// <summary>Especificação serializável de um gerador. Parameters é JSON.</summary>
public sealed record GeneratorSpec(string GeneratorId, int Version, JsonObject Parameters, IReadOnlyList<GeneratorSpec>? Children = null);

public enum BindingOrigin { Structural, Auto, Rule, User }

public enum BindingState { Active, NeedsReview, Orphaned }

public sealed record ColumnBinding(
    ColumnRef Column, GeneratorSpec Generator, double NullRate, BindingOrigin Origin,
    string ColumnFingerprint, BindingState State = BindingState.Active,
    string? ConfidenceLabel = null,     // "High", "Medium"... (texto para persistência)
    bool Unique = false,                // força unicidade mesmo sem UNIQUE no schema
    string? Note = null);
