using System.Text.Json.Serialization;

namespace PaperwhiteReader;

public sealed class LibraryEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = "";
    public string Author { get; set; } = "";
    public string FileName { get; set; } = "";
    public string ContentHash { get; set; } = "";
    public int ChapterCount { get; set; }
    public int LastChapter { get; set; }
    public List<double> ChapterOffsets { get; set; } = [];
    [JsonIgnore]
    public string ProgressLabel => $"Chapter {Math.Clamp(LastChapter + 1, 1, Math.Max(ChapterCount, 1))} of {Math.Max(ChapterCount, 1)}";
}

public sealed class LibraryIndex
{
    public string? SelectedBookId { get; set; }
    public string Theme { get; set; } = "Paper";
    public double FontSize { get; set; } = 19;
    public List<LibraryEntry> Books { get; set; } = [];
}

public sealed record EpubChapter(string Title, string Text);
public sealed record EpubBook(string Title, string Author, IReadOnlyList<EpubChapter> Chapters);
