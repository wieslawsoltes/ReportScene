using System.Collections;
using System.Globalization;

namespace ReportScene.Model;

public enum PageSelectionMode { All, Current, Custom }

/// <summary>
/// Immutable ordinal page selection, stored as merged intervals rather than one entry
/// per page. It never retains a report. Page numbers entered by the user are one-based
/// positions; physical PreparedPage.Number values are neither interpreted nor rewritten.
/// </summary>
public sealed class ReportPageSelection : IReadOnlyList<int>
{
    private readonly record struct Interval(int First, int Last);
    private readonly Interval[] intervals;
    private readonly int[] ends;
    public const int MaximumExpressionLength = 4096, MaximumRanges = 512;
    public int SourcePageCount { get; }
    public PageSelectionMode Mode { get; }
    public int Count { get; }
    public int IntervalCount => intervals.Length;
    private ReportPageSelection(int pageCount, PageSelectionMode mode, Interval[] ranges)
    {
        SourcePageCount = pageCount; Mode = mode; intervals = ranges; ends = new int[ranges.Length];
        int count = 0;
        for (int n = 0; n < ranges.Length; n++) ends[n] = count = checked(count + (ranges[n].Last - ranges[n].First + 1));
        Count = count;
    }
    public static ReportPageSelection Create(int pageCount, PageSelectionMode mode = PageSelectionMode.All,
        int currentPageIndex = 0, string? pageNumbers = null, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(pageCount);
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        cancellationToken.ThrowIfCancellationRequested();
        if (mode == PageSelectionMode.All) return new(pageCount, mode, pageCount == 0 ? [] : [new(0, pageCount - 1)]);
        if (pageCount == 0) throw new ArgumentException("There are no prepared pages to select.", nameof(pageCount));
        if (mode == PageSelectionMode.Current)
        {
            if ((uint)currentPageIndex >= (uint)pageCount) throw new ArgumentOutOfRangeException(nameof(currentPageIndex));
            return new(pageCount, mode, [new(currentPageIndex, currentPageIndex)]);
        }
        if (string.IsNullOrWhiteSpace(pageNumbers)) throw new FormatException("Enter pages such as 1, 3-5, 8.");
        if (pageNumbers.Length > MaximumExpressionLength) throw new FormatException($"Page selection is limited to {MaximumExpressionLength} characters.");
        var tokens = pageNumbers.Split(',');
        if (tokens.Length > MaximumRanges) throw new FormatException($"At most {MaximumRanges} page ranges are allowed.");
        var ranges = new List<Interval>(tokens.Length);
        int Number(ReadOnlySpan<char> value)
        {
            value = value.Trim();
            if (value.IsEmpty || value.Length > 10 || !int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int number)
                || number < 1 || number > pageCount)
                throw new FormatException($"Every page must be a whole number from 1 to {pageCount}.");
            return number - 1;
        }
        foreach (var token in tokens)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var text = token.AsSpan().Trim(); int dash = text.IndexOf('-');
            int first = Number(dash < 0 ? text : text[..dash]);
            int last = dash < 0 ? first : Number(text[(dash + 1)..]);
            if (last < first) throw new FormatException("A page range must run from a smaller page to a larger page.");
            ranges.Add(new(first, last));
        }
        ranges.Sort((a, b) => a.First != b.First ? a.First.CompareTo(b.First) : a.Last.CompareTo(b.Last));
        var merged = new List<Interval>(ranges.Count);
        foreach (var range in ranges)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (merged.Count > 0 && (long)range.First <= (long)merged[^1].Last + 1)
                merged[^1] = new(merged[^1].First, Math.Max(merged[^1].Last, range.Last));
            else merged.Add(range);
        }
        return new(pageCount, mode, merged.ToArray());
    }
    /// <summary>Get a selected zero-based page index without expanding all intervals.</summary>
    public int this[int index]
    {
        get
        {
            if ((uint)index >= (uint)Count) throw new ArgumentOutOfRangeException(nameof(index));
            int low = 0, high = ends.Length - 1;
            while (low < high) { int middle = low + (high - low) / 2; if (index < ends[middle]) high = middle; else low = middle + 1; }
            return intervals[low].First + (index - (low == 0 ? 0 : ends[low - 1]));
        }
    }
    public IEnumerator<int> GetEnumerator()
    {
        foreach (var range in intervals)
            for (int n = range.First; n <= range.Last; n++) yield return n;
    }
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    /// <summary>Snapshot only the selected page list; page objects, metadata and physical numbering are preserved.</summary>
    public PreparedReport Apply(PreparedReport report, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(report); cancellationToken.ThrowIfCancellationRequested();
        if (report.Pages.Count != SourcePageCount) throw new ArgumentException("The prepared page count changed; select pages again.", nameof(report));
        var pages = new PreparedPage[Count]; int n = 0;
        foreach (int index in this)
        { if ((n & 255) == 0) cancellationToken.ThrowIfCancellationRequested(); pages[n++] = report.Pages[index]; }
        cancellationToken.ThrowIfCancellationRequested();
        return report with { Pages = Array.AsReadOnly(pages) };
    }
}
