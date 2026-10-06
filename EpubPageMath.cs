namespace PaperwhiteReader;

public static class EpubPageMath
{
    public static int CountPages(double contentHeight, double viewportHeight)
    {
        if (!double.IsFinite(contentHeight) || !double.IsFinite(viewportHeight) ||
            contentHeight <= 0 || viewportHeight <= 0) return 1;
        return (int)Math.Clamp(Math.Ceiling(contentHeight / viewportHeight), 1, int.MaxValue);
    }

    public static int PageForOffset(double offset, double contentHeight, double viewportHeight)
    {
        var pages = CountPages(contentHeight, viewportHeight);
        if (pages == 1 || !double.IsFinite(offset)) return 0;
        var maxOffset = Math.Max(0, contentHeight - viewportHeight);
        var clamped = Math.Clamp(offset, 0, maxOffset);
        if (clamped >= maxOffset - 1) return pages - 1;
        return Math.Clamp((int)Math.Floor(clamped / viewportHeight), 0, pages - 1);
    }

    public static double OffsetForPage(int page, double contentHeight, double viewportHeight)
    {
        var pages = CountPages(contentHeight, viewportHeight);
        if (pages == 1) return 0;
        var maxOffset = Math.Max(0, contentHeight - viewportHeight);
        return Math.Min(Math.Clamp(page, 0, pages - 1) * viewportHeight, maxOffset);
    }

    public static int GlobalPage(IReadOnlyList<int> counts, int chapterIndex, int pageWithinChapter)
    {
        if (counts.Count == 0) return 1;
        var chapter = Math.Clamp(chapterIndex, 0, counts.Count - 1);
        var before = counts.Take(chapter).Sum(value => Math.Max(1, value));
        return before + Math.Clamp(pageWithinChapter, 0, Math.Max(1, counts[chapter]) - 1) + 1;
    }
}
