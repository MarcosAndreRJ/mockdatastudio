using System.Windows.Media;

namespace MockDataStudio.App.Samples;

// Itens de apresentação compartilhados pelas telas (texto, glifo e cores já resolvidos). Os dados vêm dos serviços reais.

public sealed record TreeNode(string Text, string Glyph, IReadOnlyList<TreeNode> Children, bool Expanded = false);

public sealed record KeyValue(string Label, string Value);

public sealed record ConnectionItem(string Name, bool Online, string Glyph, Brush Tint, Brush TintBg, IReadOnlyList<KeyValue> Details);

public sealed record ActivityItem(string Text, string Time, Brush Dot);

public sealed record ChangeItem(string Type, string ObjectName, string Change, Brush ChangeFg, string Details);

public sealed record StatTile(string Value, string Label, string Glyph, Brush Fg, Brush Bg);

public sealed record ScoreReason(string Text, string Points);

public sealed record ConnectionTest(string When, string Status, Brush StatusFg, Brush StatusBg, string Latency, string Message);

public sealed record ConnectionActivity(string Title, string Detail, string Time, Brush Dot);
