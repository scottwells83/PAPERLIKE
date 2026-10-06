using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using SkiaSharp;

namespace PaperwhiteReader;

public partial class MainWindow : Window
{
    private readonly EpubLibrary _library;
    private LibraryIndex _index;
    private EpubBook? _book;
    private int _chapter;
    private double _fontSize;
    private int[] _pageCounts = [];
    private bool _ready, _selecting, _scrolling;
    private SKColor _pageColor = SKColor.Parse("#F0E9D7"), _inkColor = SKColor.Parse("#332F29");
    private Bitmap? _pdfBitmap;
    private readonly Dictionary<(string Path, int Page, string Theme), byte[]> _pdfCache = [];
    private readonly Queue<(string Path, int Page, string Theme)> _pdfCacheOrder = [];
    private readonly Dictionary<(string Path, int Page, string Theme), Task<byte[]>> _pdfRenders = [];
    private readonly SemaphoreSlim _pdfRenderGate = new(1, 1);
    private int _pdfRenderVersion;

    public MainWindow()
    {
        InitializeComponent();
        _library = new EpubLibrary();
        _index = _library.Load();
        _fontSize = Math.Clamp(_index.FontSize, 14, 32);
        ThemePicker.SelectedIndex = _index.Theme switch { "Ivory" => 1, "Night" => 2, _ => 0 };
        FontPicker.SelectedIndex = _index.FontName switch { "Inter" => 1, "Verdana" => 2, "Atkinson" => 3, _ => 0 };
        _ready = true;
        ApplyTheme();
        ApplyFont();
        RefreshLibrary();
        var entry = _index.Books.FirstOrDefault(x => x.Id == _index.SelectedBookId);
        if (entry is not null) OpenBook(entry);
        else ShowWelcome();
        Closing += (_, _) => SavePosition(true);
    }

    private LibraryEntry? CurrentEntry => _index.Books.FirstOrDefault(x => x.Id == _index.SelectedBookId);
    private int TotalPages => _book?.IsPdf == true ? _book.Chapters.Count : _pageCounts.Sum();
    private int CurrentPage => _book?.IsPdf == true ? _chapter + 1 : EpubPageMath.GlobalPage(_pageCounts, _chapter, CurrentLocalPage());
    private int CurrentLocalPage() => EpubPageMath.PageForOffset(ChapterScroll.Offset.Y, ChapterScroll.Extent.Height, ChapterScroll.Viewport.Height);

    private void RefreshLibrary()
    {
        _selecting = true;
        LibraryList.ItemsSource = null;
        LibraryList.ItemsSource = _index.Books.ToList();
        LibraryList.SelectedItem = CurrentEntry;
        _selecting = false;
        RemoveBookButton.IsEnabled = CurrentEntry is not null;
    }

    private void OpenBook(LibraryEntry entry)
    {
        try
        {
            _book = _library.Open(entry);
            _chapter = Math.Clamp(entry.LastChapter, 0, _book.Chapters.Count - 1);
            _index.SelectedBookId = entry.Id;
            _library.Save(_index);
            BookTitle.Text = _book.Title;
            BookAuthor.Text = _book.Author;
            var sections = _book.IsPdf
                ? (_book.Sections?.Count > 0 ? _book.Sections : [new ReaderSection("Pages", 0)])
                : _book.Chapters.Select((x, i) => new ReaderSection(x.Title, i)).ToList();
            _selecting = true;
            ChapterList.ItemsSource = sections;
            _selecting = false;
            RefreshLibrary();
            RefreshBookmarks();
            SearchResultsPanel.IsVisible = false;
            UpdateReadingView(entry.ChapterOffsets.ElementAtOrDefault(_chapter));
        }
        catch (Exception ex) { ShowError(ex.Message); }
    }

    private void ShowWelcome()
    {
        _pdfRenderVersion++;
        _book = null;
        ReaderPanel.IsVisible = false;
        WelcomePanel.IsVisible = true;
        BookTitle.Text = "Your reading room";
        BookAuthor.Text = "";
        ProgressText.Text = "A quiet place for your next page";
        ChapterList.ItemsSource = null;
        RefreshLibrary();
    }

    private void UpdateReadingView(double? targetOffset = null)
    {
        if (_book is null) return;
        var pdf = _book.IsPdf;
        if (!pdf) _pdfRenderVersion++;
        WelcomePanel.IsVisible = false;
        ReaderPanel.IsVisible = true;
        PdfPageJump.IsVisible = pdf;
        ChapterText.IsVisible = !pdf;
        PdfPageImage.IsVisible = pdf;
        _scrolling = true;
        ChapterScroll.Offset = new Vector(0, 0);
        if (pdf)
        {
            ChapterText.Text = "";
            RenderPdf();
            PageJumpBox.Text = (_chapter + 1).ToString();
        }
        else
        {
            ChapterText.Text = _book.Chapters[_chapter].Title + "\n\n" + _book.Chapters[_chapter].Text;
        }
        Dispatcher.UIThread.Post(() =>
        {
            if (_book is null) return;
            if (!pdf)
            {
                RecalculatePages();
                var height = ChapterScroll.Extent.Height;
                var viewport = ChapterScroll.Viewport.Height;
                var requested = Math.Clamp(targetOffset ?? 0, 0, Math.Max(0, height - viewport));
                ChapterScroll.Offset = new Vector(0, requested);
            }
            _scrolling = false;
            RefreshProgress();
        }, DispatcherPriority.Loaded);
        RefreshSectionSelection();
        RefreshProgress();
    }

    private void RecalculatePages()
    {
        if (_book is null || _book.IsPdf) return;
        var width = Math.Max(120, Math.Min(660, ChapterScroll.Viewport.Width - 18));
        var height = Math.Max(100, ChapterScroll.Viewport.Height);
        var result = new int[_book.Chapters.Count];
        for (var i = 0; i < result.Length; i++)
        {
            var block = new TextBlock
            {
                Text = _book.Chapters[i].Title + "\n\n" + _book.Chapters[i].Text,
                FontFamily = ChapterText.FontFamily,
                FontSize = _fontSize,
                LineHeight = _fontSize * 1.65,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = width
            };
            block.Measure(new Size(width, double.PositiveInfinity));
            result[i] = EpubPageMath.CountPages(block.DesiredSize.Height, height);
        }
        _pageCounts = result;
    }

    private void RefreshProgress()
    {
        if (_book is null) return;
        var entry = CurrentEntry;
        if (entry is null) return;
        var total = Math.Max(1, TotalPages);
        var page = Math.Clamp(CurrentPage, 1, total);
        ProgressText.Text = $"Page {page} of {total}";
        entry.LastChapter = _chapter;
        entry.LastPage = page;
        entry.PageCount = total;
        ChapterHeading.Text = _book.IsPdf ? SectionForPage(_chapter) : _book.Chapters[_chapter].Title;
        PreviousButton.IsEnabled = page > 1;
        NextButton.IsEnabled = page < total;
        RefreshSectionSelection();
    }

    private string SectionForPage(int page)
    {
        var sections = _book?.Sections;
        return sections?.LastOrDefault(s => s.ChapterIndex <= page)?.Title ?? $"Page {page + 1}";
    }

    private void RefreshSectionSelection()
    {
        if (_book is null) return;
        _selecting = true;
        ChapterList.SelectedItem = ChapterList.ItemsSource?.Cast<ReaderSection>().LastOrDefault(x => x.ChapterIndex <= _chapter);
        _selecting = false;
    }

    private void SavePosition(bool continueBookmark = false)
    {
        if (_book is null || CurrentEntry is not { } entry) return;
        while (entry.ChapterOffsets.Count < _book.Chapters.Count) entry.ChapterOffsets.Add(0);
        if (!_book.IsPdf) entry.ChapterOffsets[_chapter] = ChapterScroll.Offset.Y;
        entry.LastChapter = _chapter;
        entry.LastPage = Math.Clamp(CurrentPage, 1, Math.Max(1, TotalPages));
        entry.PageCount = Math.Max(1, TotalPages);
        if (continueBookmark)
        {
            entry.Bookmarks.RemoveAll(x => x.IsAutomatic);
            entry.Bookmarks.Insert(0, new ReaderBookmark
            {
                IsAutomatic = true,
                Label = $"Continue reading · Page {entry.LastPage}",
                Chapter = _chapter,
                Offset = _book.IsPdf ? 0 : ChapterScroll.Offset.Y
            });
        }
        _library.Save(_index);
    }

    private void TurnPage(int direction)
    {
        if (_book is null) return;
        if (_book.IsPdf)
        {
            var page = _chapter + direction;
            if (page < 0 || page >= _book.Chapters.Count) return;
            _chapter = page;
            UpdateReadingView();
        }
        else
        {
            var local = CurrentLocalPage() + direction;
            if (local >= 0 && local < _pageCounts[_chapter])
            {
                _scrolling = true;
                ChapterScroll.Offset = new Vector(0, EpubPageMath.OffsetForPage(local, ChapterScroll.Extent.Height, ChapterScroll.Viewport.Height));
                _scrolling = false;
                RefreshProgress();
            }
            else
            {
                var next = _chapter + direction;
                if (next < 0 || next >= _book.Chapters.Count) return;
                SavePosition();
                _chapter = next;
                UpdateReadingView(direction < 0 ? double.MaxValue : 0);
            }
        }
        SavePosition();
    }

    private void Previous_OnClick(object? s, RoutedEventArgs e) => TurnPage(-1);
    private void Next_OnClick(object? s, RoutedEventArgs e) => TurnPage(1);
    private void ChapterScroll_OnScrollChanged(object? s, ScrollChangedEventArgs e)
    {
        if (_scrolling || _book is null || _book.IsPdf) return;
        RefreshProgress();
    }
    private void ChapterScroll_OnSizeChanged(object? s, SizeChangedEventArgs e)
    {
        if (!_ready || _book is null || _book.IsPdf) return;
        Dispatcher.UIThread.Post(() => { RecalculatePages(); RefreshProgress(); }, DispatcherPriority.Loaded);
    }
    private void LibraryList_OnSelectionChanged(object? s, SelectionChangedEventArgs e)
    {
        if (_selecting || LibraryList.SelectedItem is not LibraryEntry entry || entry.Id == _index.SelectedBookId) return;
        SavePosition(true);
        OpenBook(entry);
    }
    private void ChapterList_OnSelectionChanged(object? s, SelectionChangedEventArgs e)
    {
        if (_selecting || ChapterList.SelectedItem is not ReaderSection section || _book is null) return;
        SavePosition();
        _chapter = section.ChapterIndex;
        UpdateReadingView();
        SavePosition();
    }
    private async void AddBook_OnClick(object? s, RoutedEventArgs e)
    {
        try
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Choose an EPUB or PDF book", AllowMultiple = false,
                FileTypeFilter = [new FilePickerFileType("Books") { Patterns = ["*.epub", "*.pdf"] }]
            });
            var file = files.FirstOrDefault();
            if (file is null || !file.Path.IsFile) return;
            SavePosition(true);
            var added = _library.Add(file.Path.LocalPath);
            _index = added.Index;
            OpenBook(added.Entry);
        }
        catch (Exception ex) { ShowError(ex.Message); }
    }
    private void RemoveBook_OnClick(object? s, RoutedEventArgs e)
    {
        if (CurrentEntry is not { } entry) return;
        try
        {
            var next = _index.Books.FirstOrDefault(x => x.Id != entry.Id);
            _library.Remove(_index, entry);
            if (next is null) ShowWelcome(); else OpenBook(next);
        }
        catch (Exception ex) { ShowError(ex.Message); }
    }
    private void ShowBooks_OnClick(object? s, RoutedEventArgs e)
    {
        try { _library.RevealBooksFolder(); }
        catch (Exception ex) { ShowError(ex.Message); }
    }
    private void PageJump_OnClick(object? s, RoutedEventArgs e) => GoToPage();
    private void PageJumpBox_OnKeyDown(object? s, KeyEventArgs e) { if (e.Key == Key.Enter) GoToPage(); }
    private void GoToPage()
    {
        if (_book?.IsPdf != true || !int.TryParse(PageJumpBox.Text, out var page)) return;
        _chapter = Math.Clamp(page - 1, 0, _book.Chapters.Count - 1);
        UpdateReadingView();
        SavePosition();
    }

    private void RefreshBookmarks()
    {
        _selecting = true;
        BookmarkPicker.ItemsSource = null;
        BookmarkPicker.ItemsSource = CurrentEntry?.Bookmarks.ToList();
        BookmarkPicker.SelectedIndex = -1;
        _selecting = false;
        RemoveBookmarkButton.IsEnabled = false;
    }
    private void AddBookmark_OnClick(object? s, RoutedEventArgs e)
    {
        if (_book is null || CurrentEntry is not { } entry) return;
        entry.Bookmarks.Add(new ReaderBookmark { Chapter = _chapter, Offset = _book.IsPdf ? 0 : ChapterScroll.Offset.Y,
            Label = $"Page {CurrentPage} · {ChapterHeading.Text}" });
        _library.Save(_index);
        RefreshBookmarks();
    }
    private void BookmarkPicker_OnSelectionChanged(object? s, SelectionChangedEventArgs e)
    {
        if (_selecting || BookmarkPicker.SelectedItem is not ReaderBookmark mark || _book is null) return;
        RemoveBookmarkButton.IsEnabled = true;
        _chapter = Math.Clamp(mark.Chapter, 0, _book.Chapters.Count - 1);
        UpdateReadingView(mark.Offset);
    }
    private void RemoveBookmark_OnClick(object? s, RoutedEventArgs e)
    {
        if (CurrentEntry is not { } entry || BookmarkPicker.SelectedItem is not ReaderBookmark mark) return;
        entry.Bookmarks.Remove(mark);
        _library.Save(_index);
        RefreshBookmarks();
    }

    private sealed record SearchHit(string Label, int Chapter)
    {
        public override string ToString() => Label;
    }
    private void Search_OnClick(object? s, RoutedEventArgs e) => Search();
    private void SearchBox_OnKeyDown(object? s, KeyEventArgs e) { if (e.Key == Key.Enter) Search(); }
    private void Search()
    {
        if (_book is null || string.IsNullOrWhiteSpace(SearchBox.Text)) return;
        var query = SearchBox.Text.Trim();
        var hits = new List<SearchHit>();
        for (var i = 0; i < _book.Chapters.Count && hits.Count < 100; i++)
        {
            var text = _book.Chapters[i].Text;
            var found = text.IndexOf(query, StringComparison.OrdinalIgnoreCase);
            if (found < 0) continue;
            var start = Math.Max(0, found - 38);
            var preview = text.Substring(start, Math.Min(90, text.Length - start)).Replace('\n', ' ');
            var place = _book.IsPdf ? $"Page {i + 1} · {SectionForPage(i)}" : _book.Chapters[i].Title;
            hits.Add(new SearchHit($"{place}: {preview}", i));
        }
        SearchResultsList.ItemsSource = hits.Count > 0 ? hits : [new SearchHit("No matches", -1)];
        SearchResultsPanel.IsVisible = true;
    }
    private void SearchResultsList_OnSelectionChanged(object? s, SelectionChangedEventArgs e)
    {
        if (SearchResultsList.SelectedItem is not SearchHit hit || hit.Chapter < 0) return;
        _chapter = hit.Chapter;
        UpdateReadingView();
    }
    private void CloseSearchResults_OnClick(object? s, RoutedEventArgs e) => SearchResultsPanel.IsVisible = false;

    private void ThemePicker_OnSelectionChanged(object? s, SelectionChangedEventArgs e)
    {
        if (!_ready) return;
        _index.Theme = ThemePicker.SelectedIndex switch { 1 => "Ivory", 2 => "Night", _ => "Paper" };
        ApplyTheme();
        _library.Save(_index);
        if (_book?.IsPdf == true) RenderPdf();
    }
    private void ApplyTheme()
    {
        var colors = _index.Theme switch
        {
            "Night" => new[] { "#191B1D", "#222426", "#252729", "#2C2F31", "#383B3D", "#46494B", "#EBE8E1", "#AAA9A5", "#C1AB83", "#191B1D", "#343739", "#EBE8E1" },
            "Ivory" => new[] { "#F8F2E5", "#EEE4D2", "#FFF9EB", "#FFF9ED", "#EDE1C9", "#D9CCB6", "#3B3126", "#796D5F", "#886A42", "#FFFFFF", "#EFE3D0", "#3B3126" },
            _ => new[] { "#F6F4EE", "#E3E0D9", "#F0E9D7", "#FFFFFF", "#EEE8D8", "#DEDAD0", "#332F29", "#777269", "#74634E", "#FFFFFF", "#EAE6DE", "#403B33" }
        };
        var keys = new[] { "WindowBrush", "SurfaceBrush", "PageBrush", "CardBrush", "SelectedBrush", "BorderBrush", "TextBrush", "MutedBrush", "AccentBrush", "AccentTextBrush", "ButtonBrush", "ButtonTextBrush" };
        for (var i = 0; i < keys.Length; i++) Application.Current!.Resources[keys[i]] = new SolidColorBrush(Color.Parse(colors[i]));
        _pageColor = SKColor.Parse(colors[2]);
        _inkColor = SKColor.Parse(colors[6]);
        Application.Current!.RequestedThemeVariant = _index.Theme == "Night" ? Avalonia.Styling.ThemeVariant.Dark : Avalonia.Styling.ThemeVariant.Light;
    }
    private void FontPicker_OnSelectionChanged(object? s, SelectionChangedEventArgs e)
    {
        if (!_ready) return;
        _index.FontName = FontPicker.SelectedIndex switch { 1 => "Inter", 2 => "Verdana", 3 => "Atkinson", _ => "Georgia" };
        ApplyFont();
        _library.Save(_index);
        RecalculatePages(); RefreshProgress();
    }
    private void ApplyFont()
    {
        ChapterText.FontFamily = new FontFamily(_index.FontName switch { "Atkinson" => "avares://PaperLike/assets/Fonts#Atkinson Hyperlegible Next", _ => _index.FontName });
        ChapterText.FontSize = _fontSize;
        ChapterText.LineHeight = _fontSize * 1.65;
    }
    private void SmallerText_OnClick(object? s, RoutedEventArgs e) => ChangeSize(-1);
    private void LargerText_OnClick(object? s, RoutedEventArgs e) => ChangeSize(1);
    private void ChangeSize(int delta)
    {
        SavePosition();
        _fontSize = Math.Clamp(_fontSize + delta, 14, 32);
        _index.FontSize = _fontSize;
        ApplyFont();
        _library.Save(_index);
        RecalculatePages(); RefreshProgress();
    }

    private async void RenderPdf()
    {
        if (_book?.IsPdf != true || _book.Path is null) return;
        var version = ++_pdfRenderVersion;
        var key = (Path: _book.Path, Page: _chapter, Theme: _index.Theme);
        var paper = _pageColor;
        var ink = _inkColor;
        PdfLoading.IsVisible = true;
        PdfPageImage.Source = null;
        _pdfBitmap?.Dispose();
        _pdfBitmap = null;
        try
        {
            var bytes = await GetPdfPageAsync(key, paper, ink);
            if (version != _pdfRenderVersion || _book?.Path != key.Path || _chapter != key.Page || _index.Theme != key.Theme) return;
            using var memory = new MemoryStream(bytes);
            _pdfBitmap = new Bitmap(memory);
            PdfPageImage.Source = _pdfBitmap;
            PdfLoading.IsVisible = false;
            if (key.Page + 1 < _book.Chapters.Count)
                _ = PrefetchPdfPageAsync((key.Path, key.Page + 1, key.Theme), paper, ink);
        }
        catch (Exception ex)
        {
            if (version != _pdfRenderVersion) return;
            PdfLoading.IsVisible = false;
            ShowError(ex.Message);
        }
    }

    private Task<byte[]> GetPdfPageAsync((string Path, int Page, string Theme) key, SKColor paper, SKColor ink)
    {
        if (_pdfCache.TryGetValue(key, out var cached)) return Task.FromResult(cached);
        if (_pdfRenders.TryGetValue(key, out var running)) return running;
        var task = RenderAndCachePdfPageAsync(key, paper, ink);
        _pdfRenders[key] = task;
        return task;
    }

    private async Task<byte[]> RenderAndCachePdfPageAsync((string Path, int Page, string Theme) key, SKColor paper, SKColor ink)
    {
        try
        {
            await _pdfRenderGate.WaitAsync();
            byte[] bytes;
            try { bytes = await Task.Run(() => PdfPageRenderer.Render(key.Path, key.Page, paper, ink)); }
            finally { _pdfRenderGate.Release(); }
            _pdfCache[key] = bytes;
            _pdfCacheOrder.Enqueue(key);
            while (_pdfCacheOrder.Count > 6) _pdfCache.Remove(_pdfCacheOrder.Dequeue());
            return bytes;
        }
        finally { _pdfRenders.Remove(key); }
    }

    private async Task PrefetchPdfPageAsync((string Path, int Page, string Theme) key, SKColor paper, SKColor ink)
    {
        try { await GetPdfPageAsync(key, paper, ink); }
        catch { /* Foreground render reports an error if this page is requested. */ }
    }
    private async void ShowError(string message)
    {
        var dialog = new Window { Title = "PaperLike", Width = 440, Height = 170, Content = new TextBlock { Text = message, Margin = new Thickness(24), TextWrapping = TextWrapping.Wrap } };
        await dialog.ShowDialog(this);
    }
}
