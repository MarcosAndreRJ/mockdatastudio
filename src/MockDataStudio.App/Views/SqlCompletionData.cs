using ICSharpCode.AvalonEdit.CodeCompletion;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Editing;
using MockDataStudio.SqlTooling.Completion;

namespace MockDataStudio.App.Views;

// Item da janela de autocomplete do editor. Substitui o prefixo digitado pelo texto escolhido.
public sealed class SqlCompletionData(CompletionItem item) : ICompletionData
{
    public CompletionItem Item { get; } = item;

    public System.Windows.Media.ImageSource? Image => null;

    public string Text => Item.InsertText;

    public object Content => Item.Label;

    public object Description => Item.Kind switch
    {
        CompletionKind.Relation => "Relação",
        CompletionKind.Column => "Coluna",
        _ => "Palavra-chave",
    };

    public double Priority => Item.Kind == CompletionKind.Keyword ? 0 : 1;

    public void Complete(TextArea textArea, ISegment completionSegment, EventArgs insertionRequestEventArgs) =>
        textArea.Document.Replace(completionSegment, Item.InsertText);
}
