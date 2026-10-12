namespace ReportScene.Designer;

/// <summary>Visible pane geometry, distinct from user preferences. Computing layout never mutates or persists settings.</summary>
public readonly record struct DesignerPaneLayout(double SidebarWidth, double TreeHeight, double PropertiesHeight,
    bool ShowTree, bool ShowProperties, double MaximumSidebarWidth, double MaximumTreeHeight)
{
    public const double ToolbarWidth = 33, DividerWidth = 4, MinimumCanvasWidth = 240;
    public const double MinimumSidebarWidth = 180, MinimumTreeHeight = 96, MinimumPropertiesHeight = 200;
    public bool HasSidebar => ShowTree || ShowProperties;
    public bool HasInnerDivider => ShowTree && ShowProperties;

    public static DesignerPaneLayout Calculate(DesignerPreferences preferences, double viewportWidth, double viewportHeight)
    {
        ArgumentNullException.ThrowIfNull(preferences); preferences.Validate();
        if (!double.IsFinite(viewportWidth) || viewportWidth < 0) throw new ArgumentOutOfRangeException(nameof(viewportWidth));
        if (!double.IsFinite(viewportHeight) || viewportHeight < 0) throw new ArgumentOutOfRangeException(nameof(viewportHeight));
        double available = Math.Max(0, viewportWidth - ToolbarWidth - DividerWidth - MinimumCanvasWidth);
        double maximumWidth = Math.Max(MinimumSidebarWidth, Math.Min(640, available));
        double maximumHeight = Math.Max(MinimumTreeHeight, Math.Min(480, viewportHeight - DividerWidth - MinimumPropertiesHeight));
        bool tree = preferences.ShowTree, properties = preferences.ShowProperties;
        // Prefer an editable canvas over an unusably narrow property pane. These
        // temporary visibility decisions never overwrite the user's chosen visibility.
        if (available < MinimumSidebarWidth || viewportHeight == 0 || !tree && !properties)
            return new(0, 0, 0, false, false, maximumWidth, maximumHeight);
        if (tree && properties && viewportHeight < MinimumTreeHeight + DividerWidth + MinimumPropertiesHeight)
            tree = false;
        double width = Math.Min(preferences.SidebarWidth, maximumWidth);
        double treeHeight = tree ? properties ? Math.Min(preferences.TreeHeight, maximumHeight) : viewportHeight : 0;
        double propertiesHeight = properties ? viewportHeight - treeHeight - (tree ? DividerWidth : 0) : 0;
        return new(width, treeHeight, propertiesHeight, tree, properties, maximumWidth, maximumHeight);
    }
}
