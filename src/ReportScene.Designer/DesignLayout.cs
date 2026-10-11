using ReportScene.Model;

namespace ReportScene.Designer;

public sealed record DesignBand(Band Band, RectD Bounds, RectD LabelBounds);
public sealed record DesignObject(ReportItem Item, Band Band, RectD Bounds);
public sealed record DesignLayout(ReportPage Page, RectD Paper, IReadOnlyList<DesignBand> Bands, IReadOnlyList<DesignObject> Objects, double Width, double Height)
{
    public const double BandPanelWidth = 110, RulerSize = 20;
    public static DesignLayout Create(ReportPage page) => Create(page, true);
    /// <summary>Compact band authoring need not show unused physical-paper height. Stored geometry is unchanged.</summary>
    public static DesignLayout Create(ReportPage page, bool showFullPage)
    {
        var bands = new List<DesignBand>(); var objects = new List<DesignObject>();
        double y = RulerSize + page.Margins.Top;
        IEnumerable<Band> Ordered(Band b)
        {
            if (b.Header is not null) foreach (var h in Ordered(b.Header)) yield return h;
            foreach (var g in b.Groups) foreach (var h in Ordered(g.Header)) yield return h;
            yield return b;
            foreach (var c in b.Children) foreach (var h in Ordered(c)) yield return h;
            foreach (var g in b.Groups.AsEnumerable().Reverse()) foreach (var h in Ordered(g.Footer)) yield return h;
            if (b.Footer is not null) foreach (var h in Ordered(b.Footer)) yield return h;
        }
        foreach (var band in page.Bands.SelectMany(Ordered))
        {
            double height = Math.Max(18, Math.Max(band.Height, band.Items.Select(i => i.Bounds.Bottom).DefaultIfEmpty(0).Max()));
            var bounds = new RectD(BandPanelWidth + RulerSize + page.Margins.Left, y, page.ContentWidth, height);
            bands.Add(new(band, bounds, new(0, y, BandPanelWidth, height)));
            foreach (var item in band.Items) objects.Add(new(item, band, item.Bounds.Offset(bounds.X, y)));
            y += height + 1;
        }
        double paperHeight = Math.Max(showFullPage ? page.Height : RulerSize, y + page.Margins.Bottom - RulerSize);
        return new(page, new(BandPanelWidth + RulerSize, RulerSize, page.Width, paperHeight), bands, objects, BandPanelWidth + RulerSize + page.Width + 12, paperHeight + RulerSize + 12);
    }
    public DesignObject? HitTest(double x, double y) => Objects.Reverse().FirstOrDefault(o => o.Bounds.Contains(x, y));
    public DesignBand? BandAt(double x, double y) => Bands.FirstOrDefault(b => b.Bounds.Contains(x, y) || b.LabelBounds.Contains(x, y));
    public IReadOnlyList<DesignObject> Intersect(RectD rectangle) => Objects.Where(o => o.Bounds.Intersects(rectangle)).ToArray();
}
