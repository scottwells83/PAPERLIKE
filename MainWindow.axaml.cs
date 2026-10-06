using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Platform.Storage;
using Avalonia.Media;

namespace PaperwhiteReader;

public partial class MainWindow : Window
{
    private readonly EpubLibrary _library;
    private LibraryIndex _index;
    private EpubBook? _activeBook;
    private int _chapterIndex;
    private double _fontSize = 19;
    private bool _suppressSelection;
    private bool _suppressChapterSelection;
    private bool _suppressScrollTracking;

    public MainWindow()
    {
        InitializeComponent();
        _library = new EpubLibrary();
        _index = _library.Load();
        _fontSize = Math.Clamp(_index.FontSize, 15, 29);
        ThemePicker.SelectedIndex = _index.Theme switch { "Ivory" => 1, "Night" => 2, _ => 0 };
        Closing += (_, _) => SaveReadingPosition();
        RefreshLibraryList();
        var selected = _index.Books.FirstOrDefault(book => book.Id == _index.SelectedBookId);
        if (selected is not null) OpenBook(selected);
        else ShowWelcome();
    }

    private async void AddBook_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        try
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Choose an EPUB book",
                AllowMultiple = false,
                FileTypeFilter = [new FilePickerFileType("EPUB books") { Patterns = ["*.epub"] }]
            });
            var file = files.FirstOrDefault();
            if (file is null || !file.Path.IsFile) return;

            var added = _library.Add(file.Path.LocalPath);
            _index = added.Index;
            RefreshLibraryList();
            SelectEntryInList(added.Entry);
            _activeBook = added.Book;
            _chapterIndex = Math.Clamp(added.Entry.LastChapter, 0, added.Book.Chapters.Count - 1);
            UpdateReadingView();
        }
        catch (Exception error)
        {
            ShowError(error.Message);
        }
    }

    private void LibraryList_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_suppressSelection || LibraryList.SelectedItem is not LibraryEntry entry) return;
        if (_activeBook is not null && entry.Id == _index.SelectedBookId) return;
        SaveReadingPosition();
        OpenBook(entry);
    }

    private void OpenBook(LibraryEntry entry)
    {
        try
        {
            _activeBook = _library.Open(entry);
            EnsureChapterOffsets(entry, _activeBook.Chapters.Count);
            _chapterIndex = Math.Clamp(entry.LastChapter, 0, _activeBook.Chapters.Count - 1);
            _index.SelectedBookId = entry.Id;
            _library.Save(_index);
            SelectEntryInList(entry);
            UpdateReadingView();
        }
        catch (Exception error)
        {
            ShowError(error.Message);
        }
    }

    private void Previous_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_activeBook is null || _chapterIndex <= 0) return;
        CaptureCurrentOffset();
        _chapterIndex--;
        UpdateReadingView();
        SaveReadingPosition();
    }

    private void Next_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_activeBook is null || _chapterIndex >= _activeBook.Chapters.Count - 1) return;
        CaptureCurrentOffset();
        _chapterIndex++;
        UpdateReadingView();
        SaveReadingPosition();
    }

    private void ChapterPicker_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_suppressChapterSelection || _activeBook is null || ChapterPicker.SelectedIndex < 0 || ChapterPicker.SelectedIndex == _chapterIndex) return;
        CaptureCurrentOffset();
        _chapterIndex = ChapterPicker.SelectedIndex;
        UpdateReadingView();
        SaveReadingPosition();
    }

    private void ChapterScroll_OnScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (_suppressScrollTracking || _activeBook is null) return;
        CaptureCurrentOffset();
    }

    private void SmallerText_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _fontSize = Math.Max(15, _fontSize - 1);
        ChapterText.FontSize = _fontSize;
        ChapterText.LineHeight = _fontSize * 1.8;
        _index.FontSize = _fontSize;
        SaveReadingPosition();
    }

    private void LargerText_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _fontSize = Math.Min(29, _fontSize + 1);
        ChapterText.FontSize = _fontSize;
        ChapterText.LineHeight = _fontSize * 1.8;
        _index.FontSize = _fontSize;
        SaveReadingPosition();
    }

    private void ThemePicker_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (ThemePicker is null || PageSurface is null || ChapterText is null) return;
        var tone = (ThemePicker.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Paper";
        var (page, ink) = tone switch
        {
            "Ivory" => ("#F7F5E9", "#332F29"),
            "Night" => ("#222326", "#E7E2D8"),
            _ => ("#F0E9D7", "#332F29")
        };
        PageSurface.Background = Brush.Parse(page);
        ChapterText.Foreground = Brush.Parse(ink);
        _index.Theme = tone;
        _library.Save(_index);
    }

    private void ShowBooks_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        try { _library.RevealBooksFolder(); }
        catch (Exception error) { ShowError(error.Message); }
    }

    private void SaveReadingPosition()
    {
        var selected = _index.Books.FirstOrDefault(book => book.Id == _index.SelectedBookId);
        _index.FontSize = _fontSize;
        if (selected is not null)
        {
            selected.LastChapter = _chapterIndex;
            if (_activeBook is not null)
            {
                EnsureChapterOffsets(selected, _activeBook.Chapters.Count);
                selected.ChapterOffsets[_chapterIndex] = ChapterScroll.Offset.Y;
            }
        }
        _library.Save(_index);
        if (selected is not null)
        {
            RefreshLibraryList();
            SelectEntryInList(selected);
        }
    }

    private void CaptureCurrentOffset()
    {
        var selected = _index.Books.FirstOrDefault(book => book.Id == _index.SelectedBookId);
        if (selected is null || _activeBook is null || _chapterIndex < 0 || _chapterIndex >= _activeBook.Chapters.Count) return;
        EnsureChapterOffsets(selected, _activeBook.Chapters.Count);
        selected.ChapterOffsets[_chapterIndex] = ChapterScroll.Offset.Y;
    }

    private static void EnsureChapterOffsets(LibraryEntry entry, int count)
    {
        while (entry.ChapterOffsets.Count < count) entry.ChapterOffsets.Add(0);
        if (entry.ChapterOffsets.Count > count) entry.ChapterOffsets.RemoveRange(count, entry.ChapterOffsets.Count - count);
    }

    private void UpdateReadingView()
    {
        if (_activeBook is null) return;
        var chapter = _activeBook.Chapters[_chapterIndex];
        BookTitle.Text = _activeBook.Title;
        BookAuthor.Text = _activeBook.Author;
        ChapterHeading.Text = chapter.Title.ToUpperInvariant();
        ChapterText.Text = chapter.Text;
        ChapterText.FontSize = _fontSize;
        ChapterText.LineHeight = _fontSize * 1.8;
        _suppressChapterSelection = true;
        ChapterPicker.ItemsSource = _activeBook.Chapters;
        ChapterPicker.SelectedIndex = _chapterIndex;
        _suppressChapterSelection = false;
        ProgressText.Text = $"{_chapterIndex + 1} of {_activeBook.Chapters.Count}";
        PreviousButton.IsEnabled = _chapterIndex > 0;
        NextButton.IsEnabled = _chapterIndex < _activeBook.Chapters.Count - 1;
        WelcomePanel.IsVisible = false;
        ReaderPanel.IsVisible = true;
        var entry = _index.Books.FirstOrDefault(book => book.Id == _index.SelectedBookId);
        EnsureChapterOffsets(entry!, _activeBook.Chapters.Count);
        _suppressScrollTracking = true;
        ChapterScroll.Offset = new Vector(0, entry!.ChapterOffsets[_chapterIndex]);
        _suppressScrollTracking = false;
    }

    private void ShowWelcome()
    {
        BookTitle.Text = "Your reading room";
        BookAuthor.Text = "";
        ProgressText.Text = "A quiet place for your next chapter";
        PreviousButton.IsEnabled = false;
        NextButton.IsEnabled = false;
        WelcomePanel.IsVisible = true;
        ReaderPanel.IsVisible = false;
    }

    private void ShowError(string message)
    {
        BookAuthor.Text = $"Couldn't open that book: {message}";
    }

    private void RefreshLibraryList()
    {
        _suppressSelection = true;
        LibraryList.ItemsSource = null;
        LibraryList.ItemsSource = _index.Books;
        LibraryList.SelectedItem = _index.Books.FirstOrDefault(book => book.Id == _index.SelectedBookId);
        _suppressSelection = false;
    }

    private void SelectEntryInList(LibraryEntry entry)
    {
        _suppressSelection = true;
        LibraryList.SelectedItem = _index.Books.FirstOrDefault(book => book.Id == entry.Id);
        _suppressSelection = false;
    }
}
