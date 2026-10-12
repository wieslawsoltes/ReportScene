using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ReportScene.Designer;
using ReportScene.Model;

namespace ReportScene.Controls.Uno;

/// <summary>Searchable, page-grouped prepared bookmarks. Owns metadata only, never prepared pages or report content.</summary>
public sealed class ReportPreviewOutlineView : UserControl
{
    private readonly ReportOutlineView tree = new();
    private readonly TextBlock summary = ControlTheme.Label("No bookmarks in this report.", 11);
    private readonly Button close;
    private PreparedOutlineSnapshot? snapshot;
    public PreparedOutlineSnapshot? Snapshot => snapshot;
    public int Count => snapshot?.Count ?? 0;
    public long NodeCreationCount => tree.CreatedNodeCount;
    public string FilterText { get => tree.FilterText; set => tree.FilterText = value; }
    public int ProjectedCount => tree.ProjectedCount;
    public event Action<PreparedBookmark>? BookmarkRequested;
    public event Action? CloseRequested;
    public event Action? ViewChanged;
    public ReportPreviewOutlineView()
    {
        HorizontalContentAlignment = HorizontalAlignment.Stretch; VerticalContentAlignment = VerticalAlignment.Stretch;
        var root = new Grid { Background = ControlTheme.Brush("#FFFFFF"), BorderBrush = ControlTheme.Brush("#C8C8C8"), BorderThickness = new(0, 0, 1, 0) };
        root.RowDefinitions.Add(new() { Height = new(25) }); root.RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) }); root.RowDefinitions.Add(new() { Height = new(24) });
        var header = new Grid { Background = ControlTheme.Brush("#ECECEC") };
        header.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); header.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var caption = ControlTheme.Label("Document outline", 11, true); caption.Margin = new(6, 0, 0, 0); header.Children.Add(caption);
        close = ControlTheme.Button("preview-outline-close", "Close document outline", "delete", () => CloseRequested?.Invoke(), text: false);
        close.Width = 25; close.Height = 24; close.Padding = new(5); Grid.SetColumn(close, 1); header.Children.Add(close);
        root.Children.Add(header); Grid.SetRow(tree, 1); root.Children.Add(tree);
        summary.Margin = new(6, 0, 3, 0); summary.TextTrimming = TextTrimming.CharacterEllipsis;
        Grid.SetRow(summary, 2); root.Children.Add(summary); Content = root;
        tree.NavigationRequested += Request; tree.Invoked += Request; tree.ViewChanged += () => ViewChanged?.Invoke();
    }
    private void Request(DesignerOutlineEntry entry)
    {
        if (snapshot?.Find(entry.Key) is { } target) BookmarkRequested?.Invoke(target);
    }
    public void SetSnapshot(PreparedOutlineSnapshot? value)
    {
        if (ReferenceEquals(snapshot, value)) return;
        var entries = new List<DesignerOutlineEntry>(); int currentPage = -1;
        if (value is not null) foreach (var bookmark in value.Entries)
        {
            string parent = "page:" + bookmark.PageIndex.ToString(CultureInfo.InvariantCulture);
            if (currentPage != bookmark.PageIndex)
            {
                currentPage = bookmark.PageIndex;
                // The physical page number is the caption, not the dictionary identity.
                entries.Add(new(parent, null, "Page " + bookmark.PageNumber.ToString(CultureInfo.InvariantCulture), OutlineKind.Group));
            }
            entries.Add(new(bookmark.Key, parent, bookmark.Text, OutlineKind.Object, bookmark.PageIndex, ObjectId: bookmark.ObjectId));
        }
        var metadata = new DesignerOutline(entries);
        snapshot = value; tree.ResetContexts(); tree.SetOutline(metadata, "Prepared bookmarks");
        summary.Text = Count == 0 ? "No bookmarks in this report." : Count.ToString(CultureInfo.InvariantCulture) + " bookmark(s)";
        ViewChanged?.Invoke();
    }
    public void SelectBookmark(PreparedBookmark? bookmark) => tree.SelectKey(bookmark?.Key);
    public IEnumerable<KeyValuePair<string, FrameworkElement>> Elements()
    {
        yield return new("preview-outline-close", close); yield return new("preview-outline-summary", summary);
        foreach (var pair in tree.Elements()) yield return new("preview-" + pair.Key, pair.Value);
    }
}
