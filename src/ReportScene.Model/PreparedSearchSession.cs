using System.Globalization;
using System.Text;

namespace ReportScene.Model;

public sealed record ReportSearchOptions(bool MatchCase = false, bool WholeWord = false);

/// <summary>Snapshot-based search with cached matches and bidirectional wraparound navigation.</summary>
public sealed class PreparedSearchSession
{
    private readonly SearchHit[] searchable;
    private string? term;
    private ReportSearchOptions? options;
    private IReadOnlyList<SearchHit> matches = Array.Empty<SearchHit>();
    public IReadOnlyList<SearchHit> Matches => matches;
    public int CurrentIndex { get; private set; } = -1;
    public int QueryScanCount { get; private set; }
    public PreparedSearchSession(PreparedReport report, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(report); var hits = new List<SearchHit>();
        foreach (var page in report.Pages) foreach (var item in page.Items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (item.Text.Length > 0 && PreparedVisibility.VisibleBounds(page, item) is { } bounds) hits.Add(new(page.Number, bounds, item.Text, item.Source.Id));
        }
        searchable = hits.ToArray();
    }
    public void SetQuery(string text, ReportSearchOptions? searchOptions = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(text); searchOptions ??= new(); cancellationToken.ThrowIfCancellationRequested();
        if (text == term && searchOptions == options) return;
        var found = new List<SearchHit>();
        if (text.Length > 0) foreach (var item in searchable)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var comparison = searchOptions.MatchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
            int start = 0;
            while (start <= item.Text.Length - text.Length)
            {
                int index = item.Text.IndexOf(text, start, comparison); if (index < 0) break;
                if (!searchOptions.WholeWord || (!WordBefore(item.Text, index) && !WordAt(item.Text, index + text.Length))) { found.Add(item); break; }
                start = index + 1;
            }
        }
        // Commit only after the entire query succeeds; cancellation retains the previous result.
        matches = Array.AsReadOnly(found.ToArray()); term = text; options = searchOptions; CurrentIndex = -1; QueryScanCount++;
    }
    private static bool WordBefore(string text, int index)
    {
        if (index <= 0) return false;
        index--; if (index > 0 && char.IsLowSurrogate(text[index]) && char.IsHighSurrogate(text[index - 1])) index--;
        return WordAt(text, index);
    }
    private static bool WordAt(string text, int index)
    {
        if (index >= text.Length || !Rune.TryGetRuneAt(text, index, out var rune)) return false;
        return Rune.GetUnicodeCategory(rune) is UnicodeCategory.UppercaseLetter or UnicodeCategory.LowercaseLetter
            or UnicodeCategory.TitlecaseLetter or UnicodeCategory.ModifierLetter or UnicodeCategory.OtherLetter
            or UnicodeCategory.DecimalDigitNumber or UnicodeCategory.LetterNumber or UnicodeCategory.OtherNumber
            or UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark or UnicodeCategory.ConnectorPunctuation;
    }
    public SearchHit? MoveNext() => Move(1);
    public SearchHit? MovePrevious() => Move(-1);
    private SearchHit? Move(int direction)
    {
        if (matches.Count == 0) return null;
        CurrentIndex = CurrentIndex < 0 ? direction > 0 ? 0 : matches.Count - 1 : (CurrentIndex + direction + matches.Count) % matches.Count;
        return matches[CurrentIndex];
    }
}
