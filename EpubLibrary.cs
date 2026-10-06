using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Outline;

namespace PaperwhiteReader;

public sealed class EpubLibrary
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static readonly HashSet<string> BlockTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "p", "div", "section", "article", "h1", "h2", "h3", "h4", "h5", "h6", "li", "blockquote", "br", "tr"
    };

    public string DataFolder { get; }
    public string BooksFolder { get; }
    private string IndexPath { get; }

    public EpubLibrary(string? dataFolder = null)
    {
        DataFolder = dataFolder ?? GetDataFolder();
        BooksFolder = Path.Combine(DataFolder, "Books");
        IndexPath = Path.Combine(DataFolder, "library.json");
        Directory.CreateDirectory(BooksFolder);
    }

    public LibraryIndex Load()
    {
        if (!File.Exists(IndexPath)) return new LibraryIndex();
        var json = File.ReadAllText(IndexPath);
        var index = JsonSerializer.Deserialize<LibraryIndex>(json) ?? new LibraryIndex();
        index.Books = index.Books
            .Where(book => File.Exists(Path.Combine(BooksFolder, book.FileName)))
            .ToList();
        foreach (var book in index.Books)
        {
            book.ChapterOffsets ??= [];
            book.Bookmarks ??= [];
        }
        if (index.Books.All(book => book.Id != index.SelectedBookId)) index.SelectedBookId = null;
        return index;
    }

    public (LibraryIndex Index, LibraryEntry Entry, EpubBook Book) Add(string sourcePath)
    {
        var sourceBytes = File.ReadAllBytes(sourcePath);
        var contentHash = Convert.ToHexString(SHA256.HashData(sourceBytes));
        var index = Load();
        var duplicate = index.Books.FirstOrDefault(book => book.ContentHash == contentHash);
        if (duplicate is not null)
        {
            var priorBook = Read(Path.Combine(BooksFolder, duplicate.FileName));
            index.SelectedBookId = duplicate.Id;
            Save(index);
            return (index, duplicate, priorBook);
        }

        var parsed = Read(sourcePath);
        var id = Guid.NewGuid().ToString("N");
        var originalName = Path.GetFileName(sourcePath);
        var destinationName = originalName;
        var destination = Path.Combine(BooksFolder, destinationName);
        if (File.Exists(destination))
        {
            var extension = Path.GetExtension(originalName);
            var stem = Path.GetFileNameWithoutExtension(originalName);
            destinationName = $"{stem} ({id[..8]}){extension}";
            destination = Path.Combine(BooksFolder, destinationName);
        }
        File.Copy(sourcePath, destination, overwrite: false);

        var entry = new LibraryEntry
        {
            Id = id,
            Title = parsed.Title,
            Author = parsed.Author,
            FileName = destinationName,
            ContentHash = contentHash,
            ChapterCount = parsed.Chapters.Count,
            LastChapter = 0,
            ChapterOffsets = Enumerable.Repeat(0d, parsed.Chapters.Count).ToList()
        };
        index.Books.Add(entry);
        index.SelectedBookId = entry.Id;
        Save(index);
        return (index, entry, parsed.IsPdf ? parsed with { Path = destination } : parsed);
    }

    public EpubBook Open(LibraryEntry entry) => Read(Path.Combine(BooksFolder, entry.FileName));

    public void Remove(LibraryIndex index, LibraryEntry entry)
    {
        var source = Path.Combine(BooksFolder, entry.FileName);
        var removedFolder = Path.Combine(DataFolder, "Removed Books");
        Directory.CreateDirectory(removedFolder);
        var destination = Path.Combine(removedFolder,
            $"{Path.GetFileNameWithoutExtension(entry.FileName)}-{entry.Id[..8]}{Path.GetExtension(entry.FileName)}");
        if (File.Exists(destination)) destination = Path.Combine(removedFolder, $"{entry.Id}-{entry.FileName}");
        File.Move(source, destination);
        try
        {
            index.Books.RemoveAll(book => book.Id == entry.Id);
            if (index.SelectedBookId == entry.Id) index.SelectedBookId = null;
            Save(index);
        }
        catch
        {
            File.Move(destination, source);
            throw;
        }
    }

    public void Save(LibraryIndex index)
    {
        Directory.CreateDirectory(BooksFolder);
        var temporaryPath = IndexPath + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(index, JsonOptions));
        File.Move(temporaryPath, IndexPath, overwrite: true);
    }

    public void RevealBooksFolder()
    {
        Directory.CreateDirectory(BooksFolder);
        if (OperatingSystem.IsWindows())
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe")
            {
                Arguments = $"\"{BooksFolder}\"",
                UseShellExecute = true
            });
        else if (OperatingSystem.IsMacOS())
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("/usr/bin/open")
            {
                ArgumentList = { "-a", "Finder", BooksFolder },
                UseShellExecute = false
            });
    }

    public static string GetDataFolder()
    {
        if (OperatingSystem.IsMacOS())
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support", "PaperLike");
        if (OperatingSystem.IsWindows())
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PaperLike");
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PaperLike");
    }

    private static EpubBook Read(string path)
    {
        if (path.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)) return ReadPdf(path);
        using var archive = ZipFile.OpenRead(path);
        var container = ReadXml(archive, "META-INF/container.xml");
        var packagePath = container.Descendants().FirstOrDefault(element => element.Name.LocalName == "rootfile")?
            .Attribute("full-path")?.Value;
        if (string.IsNullOrWhiteSpace(packagePath)) throw new InvalidDataException("This EPUB has no package document.");

        packagePath = NormalizeArchivePath(packagePath);
        var package = ReadXml(archive, packagePath);
        var metadata = package.Descendants().FirstOrDefault(element => element.Name.LocalName == "metadata");
        var title = metadata?.Elements().FirstOrDefault(element => element.Name.LocalName == "title")?.Value.Trim();
        var author = metadata?.Elements().FirstOrDefault(element => element.Name.LocalName == "creator")?.Value.Trim();
        var manifest = package.Descendants().FirstOrDefault(element => element.Name.LocalName == "manifest");
        var items = manifest?.Elements()
            .Where(element => element.Name.LocalName == "item")
            .Select(element => new
            {
                Id = (string?)element.Attribute("id"),
                Href = (string?)element.Attribute("href")
            })
            .Where(item => !string.IsNullOrWhiteSpace(item.Id) && !string.IsNullOrWhiteSpace(item.Href))
            .ToDictionary(item => item.Id!, item => item.Href!) ?? new Dictionary<string, string>();
        var spine = package.Descendants().FirstOrDefault(element => element.Name.LocalName == "spine");
        var chapterDirectory = Path.GetDirectoryName(packagePath)?.Replace('\\', '/') ?? "";
        var chapters = new List<EpubChapter>();
        var ordinal = 0;
        foreach (var reference in spine?.Elements().Where(element => element.Name.LocalName == "itemref") ?? [])
        {
            var id = (string?)reference.Attribute("idref");
            if (id is null || !items.TryGetValue(id, out var href)) continue;
            var target = NormalizeArchivePath(Path.Combine(chapterDirectory, RemoveUriSuffix(Uri.UnescapeDataString(href))));
            var entry = archive.GetEntry(target);
            if (entry is null) continue;
            using var stream = entry.Open();
            var document = XDocument.Load(stream);
            var heading = document.Descendants().FirstOrDefault(element =>
                element.Name.LocalName is "h1" or "h2" or "h3" or "h4" or "h5" or "h6");
            var titleText = heading is null ? "" : Regex.Replace(heading.Value, @"\s+", " ").Trim();
            var text = ExtractText(document, heading);
            if (string.IsNullOrWhiteSpace(text)) continue;
            ordinal++;
            chapters.Add(new EpubChapter(string.IsNullOrWhiteSpace(titleText) ? $"Chapter {ordinal}" : titleText, text));
        }

        if (chapters.Count == 0) throw new InvalidDataException("No readable chapters were found in this EPUB.");
        return new EpubBook(
            string.IsNullOrWhiteSpace(title) ? Path.GetFileNameWithoutExtension(path) : title,
            string.IsNullOrWhiteSpace(author) ? "Unknown author" : author,
            chapters);
    }

    private static EpubBook ReadPdf(string path)
    {
        using var document = PdfDocument.Open(path);
        var chapters = document.GetPages()
            .Select((page, index) => new EpubChapter($"Page {index + 1}", page.Text ?? ""))
            .ToList();
        if (chapters.Count == 0) throw new InvalidDataException("This PDF has no pages.");
        var sections = ReadPdfSections(document, chapters);
        return new EpubBook(Path.GetFileNameWithoutExtension(path), "PDF document", chapters, true, path, sections);
    }

    private static IReadOnlyList<ReaderSection> ReadPdfSections(PdfDocument document, IReadOnlyList<EpubChapter> pages)
    {
        var sections = new List<ReaderSection>();
        if (document.TryGetBookmarks(out var bookmarks) && bookmarks is not null)
        {
            void Visit(IEnumerable<BookmarkNode> nodes)
            {
                foreach (var node in nodes)
                {
                    if (node is DocumentBookmarkNode destination && destination.PageNumber >= 1 && destination.PageNumber <= pages.Count)
                        sections.Add(new ReaderSection(node.Title, destination.PageNumber - 1));
                    Visit(node.Children);
                }
            }
            Visit(bookmarks.Roots);
        }
        if (sections.Count > 0) return sections.OrderBy(section => section.ChapterIndex).ToList();

        // Some exported books contain printed chapter headings but no PDF outline.
        foreach (var page in document.GetPages())
        {
            if (Regex.Matches(page.Text ?? "", @"\bChapter\s+\d+\b", RegexOptions.IgnoreCase).Count > 3) continue;
            var topLines = page.GetWords()
                .GroupBy(word => Math.Round(word.BoundingBox.Bottom / 3) * 3)
                .OrderByDescending(line => line.Key)
                .Take(3)
                .Select(line => string.Join(" ", line.OrderBy(word => word.BoundingBox.Left).Select(word => word.Text)).Trim());
            foreach (var line in topLines)
            {
                if (!Regex.IsMatch(line, @"^Chapter\s+\d+\b", RegexOptions.IgnoreCase)) continue;
                sections.Add(new ReaderSection(line, page.Number - 1));
                break;
            }
        }
        return sections
            .GroupBy(section => Regex.Match(section.Title, @"^Chapter\s+(\d+)", RegexOptions.IgnoreCase).Groups[1].Value)
            .Select(group => group.OrderByDescending(section => section.ChapterIndex).First())
            .OrderBy(section => section.ChapterIndex)
            .ToList();
    }

    private static XDocument ReadXml(ZipArchive archive, string path)
    {
        var entry = archive.GetEntry(path) ?? throw new InvalidDataException($"The EPUB is missing {path}.");
        using var stream = entry.Open();
        return XDocument.Load(stream);
    }

    private static string ExtractText(XDocument document, XElement? omittedHeading)
    {
        var output = new StringBuilder();
        void Visit(XNode node)
        {
            if (node is XDocument xmlDocument)
            {
                foreach (var child in xmlDocument.Nodes()) Visit(child);
                return;
            }
            if (node is XText text)
            {
                output.Append(text.Value);
                return;
            }
            if (node is XElement element)
            {
                if (ReferenceEquals(element, omittedHeading)) return;
                var tag = element.Name.LocalName;
                if (tag.Equals("script", StringComparison.OrdinalIgnoreCase) || tag.Equals("style", StringComparison.OrdinalIgnoreCase) || tag.Equals("head", StringComparison.OrdinalIgnoreCase)) return;
                if (BlockTags.Contains(tag)) output.Append("\n\n");
                foreach (var child in element.Nodes()) Visit(child);
                if (BlockTags.Contains(tag)) output.Append("\n\n");
                return;
            }
        }

        Visit(document);
        var normalized = output.ToString().Replace('\r', '\n');
        normalized = Regex.Replace(normalized, @"[ \t]+", " ");
        normalized = Regex.Replace(normalized, @" *\n *", "\n");
        normalized = Regex.Replace(normalized, @"\n{3,}", "\n\n");
        return normalized.Trim();
    }

    private static string RemoveUriSuffix(string path)
    {
        var suffix = path.IndexOfAny(['#', '?']);
        return suffix < 0 ? path : path[..suffix];
    }

    private static string NormalizeArchivePath(string path)
    {
        var segments = new List<string>();
        foreach (var segment in path.Replace('\\', '/').Split('/'))
        {
            if (segment is "" or ".") continue;
            if (segment == "..")
            {
                if (segments.Count == 0) throw new InvalidDataException("The EPUB contains an invalid path.");
                segments.RemoveAt(segments.Count - 1);
            }
            else segments.Add(segment);
        }
        return string.Join('/', segments);
    }
}
