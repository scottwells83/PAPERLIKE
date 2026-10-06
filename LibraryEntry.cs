using System.Text.Json.Serialization;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace PaperwhiteReader;

public sealed class LibraryEntry : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    private int _lastPage;
    private int _pageCount;
    private void Notify([CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ProgressLabel)));
    }
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = "";
    public string Author { get; set; } = "";
    public string FileName { get; set; } = "";
    public string ContentHash { get; set; } = "";
    public int ChapterCount { get; set; }
    public int LastChapter { get; set; }
    public List<double> ChapterOffsets { get; set; } = [];
    public List<ReaderBookmark> Bookmarks { get; set; } = [];
    public int LastPage { get => _lastPage; set { if (_lastPage != value) { _lastPage = value; Notify(); } } }
    public int PageCount { get => _pageCount; set { if (_pageCount != value) { _pageCount = value; Notify(); } } }
    public bool IsPdf => FileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase);
    [JsonIgnore]
    public string ProgressLabel => PageCount > 0
        ? $"Page {Math.Clamp(LastPage, 1, PageCount)} of {PageCount}"
        : IsPdf ? $"Page {Math.Clamp(LastChapter + 1, 1, Math.Max(ChapterCount, 1))} of {Math.Max(ChapterCount, 1)}" : "Open to see page progress";
}

public sealed class ReaderBookmark
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public int Chapter { get; set; }
    public double Offset { get; set; }
    public string Label { get; set; } = "";
    public bool IsAutomatic { get; set; }
    public override string ToString() => Label;
}

public sealed class LibraryIndex
{
    public string? SelectedBookId { get; set; }
    public string Theme { get; set; } = "Paper";
    public double FontSize { get; set; } = 19;
    public string FontName { get; set; } = "Georgia";
    public List<LibraryEntry> Books { get; set; } = [];
}

public sealed record EpubChapter(string Title, string Text)
{
    public override string ToString() => Title;
}
public sealed record ReaderSection(string Title, int ChapterIndex)
{
    public override string ToString() => Title;
}
public sealed record EpubBook(string Title, string Author, IReadOnlyList<EpubChapter> Chapters, bool IsPdf = false, string? Path = null, IReadOnlyList<ReaderSection>? Sections = null);
