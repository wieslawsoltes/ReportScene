using System.Globalization;

namespace ReportScene.Model;

/// <summary>A bookmark occurrence, not a unique label or source object. Positions are ordinal within the displayed page list.</summary>
public sealed record PreparedBookmark(string Key, int PageIndex, int PageNumber, int ItemIndex, string Text, RectD Bounds, string ObjectId);

/// <summary>
/// Bounded immutable bookmark metadata. One pass over prepared items; no source pages,
/// report objects, images or source rows are retained. Duplicate captions remain distinct.
/// Build on the owning thread, without concurrent changes to the supplied report lists.
/// </summary>
public sealed class PreparedOutlineSnapshot
{
    public const int MaximumBookmarks = 100000, MaximumTextCharacters = 1048576;
    private readonly Dictionary<string, PreparedBookmark> byKey = new(StringComparer.Ordinal);
    public IReadOnlyList<PreparedBookmark> Entries { get; }
    public int Count => Entries.Count;
    public long ItemsExamined { get; }
    public long RetainedCharacters { get; }
    public PreparedOutlineSnapshot(PreparedReport report, int maximumBookmarks = MaximumBookmarks,
        int maximumTextCharacters = MaximumTextCharacters, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(report);
        if (maximumBookmarks < 1 || maximumBookmarks > MaximumBookmarks) throw new ArgumentOutOfRangeException(nameof(maximumBookmarks));
        if (maximumTextCharacters < 1 || maximumTextCharacters > MaximumTextCharacters) throw new ArgumentOutOfRangeException(nameof(maximumTextCharacters));
        cancellationToken.ThrowIfCancellationRequested();
        var entries = new List<PreparedBookmark>(); long examined = 0, characters = 0;
        for (int p = 0; p < report.Pages.Count; p++)
        {
            cancellationToken.ThrowIfCancellationRequested(); var page = report.Pages[p];
            if (page is null || !double.IsFinite(page.Width) || !double.IsFinite(page.Height) || page.Width <= 0 || page.Height <= 0)
                throw new ArgumentException("Prepared page dimensions must be positive and finite.", nameof(report));
            for (int n = 0; n < page.Items.Count; n++)
            {
                if ((n & 255) == 0) cancellationToken.ThrowIfCancellationRequested(); examined++;
                var item = page.Items[n];
                if (item?.Source is null) throw new ArgumentException("Prepared items require a source object.", nameof(report));
                string? label = item.Source.Bookmark;
                if (string.IsNullOrWhiteSpace(label)) continue;
                if (!Finite(item.Bounds) || item.Clip is { } clip && !Finite(clip)) throw new ArgumentException("Bookmark geometry must be finite.", nameof(report));
                if (PreparedVisibility.VisibleBounds(page, item) is not { } bounds) continue;
                if (entries.Count >= maximumBookmarks) throw new ArgumentException("Prepared outline exceeds the bookmark limit.", nameof(report));
                string key = "bookmark:" + p.ToString(CultureInfo.InvariantCulture) + ":" + n.ToString(CultureInfo.InvariantCulture);
                string id = item.Source.Id ?? "";
                characters += (long)label.Length + key.Length + id.Length;
                if (characters > maximumTextCharacters) throw new ArgumentException("Prepared outline exceeds the retained-text limit.", nameof(report));
                var entry = new PreparedBookmark(key, p, page.Number, n, label, bounds, id);
                entries.Add(entry); byKey.Add(key, entry);
            }
        }
        cancellationToken.ThrowIfCancellationRequested();
        Entries = entries.AsReadOnly(); ItemsExamined = examined; RetainedCharacters = characters;
    }
    public PreparedBookmark? Find(string? key) => key is not null && byKey.TryGetValue(key, out var item) ? item : null;
    private static bool Finite(RectD r) => double.IsFinite(r.X) && double.IsFinite(r.Y)
        && double.IsFinite(r.Width) && double.IsFinite(r.Height) && double.IsFinite(r.Right) && double.IsFinite(r.Bottom);
}
