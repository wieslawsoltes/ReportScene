using System.Globalization;

namespace ReportScene.Model;

public enum ReportPageCommand { First, Previous, Next, Last }
public enum ReportFitMode { Width, WholePage }

/// <summary>Ordinal navigation within a displayed page list, independent of physical page-number metadata.</summary>
public readonly record struct ReportPagePosition
{
    public int Count { get; }
    public int Index { get; }
    public int PageNumber => Count == 0 ? 0 : Index + 1;
    public bool HasPages => Count > 0;
    public bool CanPrevious => HasPages && Index > 0;
    public bool CanNext => HasPages && Index < Count - 1;
    public ReportPagePosition(int count, int index = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        Count = count; Index = Math.Clamp(index, 0, Math.Max(0, count - 1));
    }
    public ReportPagePosition Move(ReportPageCommand command) => command switch
    {
        ReportPageCommand.First => new(Count),
        ReportPageCommand.Previous => CanPrevious ? new(Count, Index - 1) : this,
        ReportPageCommand.Next => CanNext ? new(Count, Index + 1) : this,
        ReportPageCommand.Last => new(Count, Math.Max(0, Count - 1)),
        _ => throw new ArgumentOutOfRangeException(nameof(command))
    };
    /// <summary>Accept a trimmed, positive, in-range decimal ordinal. Failure preserves the current position.</summary>
    public bool TrySelect(string? text, out ReportPagePosition position)
    {
        position = this;
        if (!HasPages || text is null) return false;
        var input = text.AsSpan().Trim();
        if (input.Length is < 1 or > 10 || !int.TryParse(input, NumberStyles.None, CultureInfo.InvariantCulture, out int number)
            || number < 1 || number > Count) return false;
        position = new(Count, number - 1); return true;
    }
}

/// <summary>Point-to-DIP preview fitting, excluding a 20-DIP gutter on each side. The zoom range matches the surface.</summary>
public static class ReportPreviewFit
{
    public const double MinimumZoom = .1, MaximumZoom = 4, ViewportGutter = 40;
    public static double? Calculate(double pageWidth, double pageHeight, double viewportWidth, double viewportHeight, ReportFitMode mode)
    {
        if (!double.IsFinite(pageWidth) || !double.IsFinite(pageHeight) || pageWidth <= 0 || pageHeight <= 0)
            throw new ArgumentException("Page dimensions must be positive and finite.");
        if (!double.IsFinite(viewportWidth) || !double.IsFinite(viewportHeight) || viewportWidth < 0 || viewportHeight < 0)
            throw new ArgumentException("Viewport dimensions must be nonnegative and finite.");
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        if (viewportWidth <= ViewportGutter || mode == ReportFitMode.WholePage && viewportHeight <= ViewportGutter) return null;
        double ratio = (viewportWidth - ViewportGutter) / pageWidth;
        if (mode == ReportFitMode.WholePage) ratio = Math.Min(ratio, (viewportHeight - ViewportGutter) / pageHeight);
        return Math.Clamp(ratio * (72d / 96), MinimumZoom, MaximumZoom);
    }
}
