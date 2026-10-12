namespace MockDataStudio.Generation.Generators;

// Geradores de texto pseudo-latim (docs/15 §6).
internal static class LoremVocabulary
{
    // Corpus latino clássico (De finibus bonorum et malorum e expansões comuns de Lorem Ipsum).
    public static readonly string[] Words =
    [
        "lorem", "ipsum", "dolor", "sit", "amet", "consectetur", "adipiscing", "elit",
        "sed", "do", "eiusmod", "tempor", "incididunt", "ut", "labore", "et", "dolore",
        "magna", "aliqua", "enim", "ad", "minim", "veniam", "quis", "nostrud",
        "exercitation", "ullamco", "laboris", "nisi", "aliquip", "ex", "ea", "commodo",
        "consequat", "duis", "aute", "irure", "in", "reprehenderit", "voluptate",
        "velit", "esse", "cillum", "fugiat", "nulla", "pariatur", "excepteur", "sint",
        "occaecat", "cupidatat", "non", "proident", "sunt", "culpa", "qui", "officia",
        "deserunt", "mollit", "anim", "id", "est", "laborum", "at", "vero", "eos",
        "accusamus", "iusto", "odio", "dignissimos", "ducimus", "blanditiis", "praesentium",
        "voluptatum", "deleniti", "atque", "corrupti", "quos", "dolores", "quas", "molestias",
        "excepturi", "occaccati", "provident", "similique", "mollitia", "fuga", "harum",
        "quidem", "rerum", "facilis", "expedita", "distinctio", "nam", "libero", "tempore",
        "cum", "soluta", "nobis", "eligendi", "optio", "cumque", "nihil", "impedit",
        "minus", "quod", "maxime", "placeat", "facere", "possimus", "omnis", "voluptas",
        "assumenda", "repellendus", "temporibus", "autem", "quibusdam", "officiis",
        "debitis", "necessitatibus", "saepe", "eveniet", "voluptates", "repudiandae",
        "recusandae", "itaque", "earum", "hic", "tenetur", "sapiente", "delectus",
        "reiciendis", "maiores", "alias", "consequatur", "perferendis", "doloribus",
        "asperiores", "repellat", "aeterno", "argumentum", "bonorum", "causa", "civibus",
        "corporis", "definitionem", "detraxit", "disputando", "docendi", "doming",
        "eirmod", "electram", "fabulas", "facilisi", "graeco", "habeo", "hendrerit",
        "inermis", "invidunt", "iudico", "laudem", "legere", "lucilius", "ludus",
        "mediocritatem", "mel", "melius", "movet", "mutat", "natum", "nemore",
        "nusquam", "omnesque", "oporteat", "partiendo", "patrioque", "pericula",
        "pertinacia", "phaedrum", "platonem", "ponderum", "posse", "pri", "pro",
        "prompta", "propriae", "quaestio", "rationibus", "recteque", "reque",
        "salutandi", "scaevola", "scripserit", "senserit", "singulis", "splendide",
        "tale", "tantas", "te", "theophrastus", "tibique", "torquatos", "tractatos",
        "ubique", "unum", "urbanitas", "usu", "utroque", "valet", "veniam", "vidisse",
        "vim", "virtute", "vis", "vituperata", "vocent", "vocibus", "voluptaria"
    ];
}

internal sealed class LoremWordsGenerator(int min, int max) : IValueGenerator
{
    private readonly int _min = min;
    private readonly int _max = max;

    public ValueDomain Domain { get; } = new(null, false);

    public IReadOnlyList<string> DependsOnColumns { get; } = [];

    public object? Generate(ref GenerationCell cell)
    {
        var count = (int)cell.Rng.NextInt64(_min, _max + 1);
        var words = LoremVocabulary.Words;
        var chosen = new string[count];
        for (var i = 0; i < count; i++)
        {
            var idx = (int)cell.Rng.NextInt64(0, words.Length);
            chosen[i] = words[idx];
        }

        return string.Join(' ', chosen);
    }
}

internal sealed class LoremParagraphGenerator(int minSentences, int maxSentences) : IValueGenerator
{
    private readonly int _minSentences = minSentences;
    private readonly int _maxSentences = maxSentences;

    public ValueDomain Domain { get; } = new(null, false);

    public IReadOnlyList<string> DependsOnColumns { get; } = [];

    public object? Generate(ref GenerationCell cell)
    {
        var sentenceCount = (int)cell.Rng.NextInt64(_minSentences, _maxSentences + 1);
        var words = LoremVocabulary.Words;
        var sentences = new string[sentenceCount];

        for (var s = 0; s < sentenceCount; s++)
        {
            var wordCount = (int)cell.Rng.NextInt64(5, 13);
            var chosen = new string[wordCount];
            for (var w = 0; w < wordCount; w++)
            {
                var idx = (int)cell.Rng.NextInt64(0, words.Length);
                var word = words[idx];
                if (w == 0)
                {
                    word = char.ToUpperInvariant(word[0]) + word[1..];
                }

                chosen[w] = word;
            }

            sentences[s] = string.Join(' ', chosen) + ".";
        }

        return string.Join(' ', sentences);
    }
}
