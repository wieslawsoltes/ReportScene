using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using ReportScene.Controls.Uno;
using ReportScene.Model;

namespace ReportScene.Designer.Uno;

public sealed partial class ReportDesignerControl
{
    private DesignerPreferenceSession preferences = new();
    private readonly DispatcherTimer preferenceTimer = new() { Interval = TimeSpan.FromMilliseconds(400) };
    private bool preferenceTracking, applyingPreferences, observingPreferences, preferencesLoaded;
    private string preferenceError = "";
    private double preferredSidebarWidth = 280, preferredTreeHeight = 210;
    public DesignerPreferences CurrentPreferences => preferences.Current;

    /// <summary>The existing two-argument constructor remains available with in-memory options.</summary>
    public ReportDesignerControl(IReportWorkspaceHost host, ReportDocument? document, IDesignerPreferenceStore? preferenceStore)
        : this(host, document)
    {
        preferences.Changed -= PreferencesChanged; preferences.Dispose();
        preferences = new(preferenceStore, CapturePreferences()); preferences.Changed += PreferencesChanged;
    }
    private void InitializePreferenceTracking()
    {
        preferences.Changed += PreferencesChanged;
        preferenceTimer.Tick += async (_, _) => { preferenceTimer.Stop(); await FlushPreferencesAsync(); };
        preferenceTracking = true;
    }
    private DesignerPreferences CapturePreferences() => new()
    {
        SidebarWidth = preferredSidebarWidth, TreeHeight = preferredTreeHeight, ShowTree = showTree, ShowProperties = showProperties,
        ShowGrid = DesignView.ShowGrid, ShowRulers = DesignView.ShowRulers, ShowFullPaper = DesignView.ShowFullPage,
        SnapToGrid = Session.SnapToGrid, GridSize = Session.GridSize, Unit = displayUnit,
        AlphabeticalProperties = properties.Alphabetical, ExplorerTab = activeTree == "Report" ? DesignerExplorerTab.Report : DesignerExplorerTab.Data,
        DesignZoom = DesignView.Zoom, PreviewZoom = PreviewView.Zoom
    };
    private void ObservePreferences()
    {
        if (!preferenceTracking || applyingPreferences || observingPreferences || disposed || sideSplitter.IsDragging || treeSplitter.IsDragging) return;
        var old = preferences.Current;
        // Constant-size comparisons, no document walk or settings allocation on unchanged
        // selection, rendering and status notifications. Snapshot diagnostics never call this.
        if (old.SidebarWidth == preferredSidebarWidth && old.TreeHeight == preferredTreeHeight && old.ShowTree == showTree && old.ShowProperties == showProperties
            && old.ShowGrid == DesignView.ShowGrid && old.ShowRulers == DesignView.ShowRulers && old.ShowFullPaper == DesignView.ShowFullPage
            && old.SnapToGrid == Session.SnapToGrid && old.GridSize == Session.GridSize && old.Unit == displayUnit
            && old.AlphabeticalProperties == properties.Alphabetical && old.ExplorerTab == (activeTree == "Report" ? DesignerExplorerTab.Report : DesignerExplorerTab.Data)
            && old.DesignZoom == DesignView.Zoom && old.PreviewZoom == PreviewView.Zoom) return;
        observingPreferences = true;
        try { preferences.Update(CapturePreferences()); }
        catch (ArgumentException e) { preferenceError = e.Message; }
        finally { observingPreferences = false; }
    }
    private void PreferencesChanged()
    {
        if (disposed) return;
        if (!observingPreferences) ApplyPreferenceValues(preferences.Current);
        SchedulePreferenceSave();
        if (!observingPreferences) NotifyState();
    }
    private void SchedulePreferenceSave()
    {
        preferenceTimer.Stop();
        if (!disposed && preferences.HasPending && !sideSplitter.IsDragging && !treeSplitter.IsDragging) preferenceTimer.Start();
    }
    private void ApplyPreferenceValues(DesignerPreferences value)
    {
        value.Validate(); applyingPreferences = true;
        try
        {
            bool gridChanged = Session.GridSize != value.GridSize;
            preferredSidebarWidth = value.SidebarWidth; preferredTreeHeight = value.TreeHeight;
            showTree = value.ShowTree; showProperties = value.ShowProperties;
            Session.SnapToGrid = value.SnapToGrid; Session.GridSize = value.GridSize;
            displayUnit = value.Unit; activeTree = value.ExplorerTab.ToString();
            DesignView.ShowGrid = value.ShowGrid; DesignView.ShowRulers = value.ShowRulers;
            DesignView.ShowFullPage = value.ShowFullPaper; DesignView.Unit = value.Unit;
            DesignView.Zoom = value.DesignZoom; PreviewView.Zoom = value.PreviewZoom;
            properties.Alphabetical = value.AlphabeticalProperties;
            if (gridChanged) DesignView.Refresh();
            ApplyPaneLayout(); RebuildTree(); BuildRibbon(); RefreshPropertyEditors(); UpdateStatus();
        }
        finally { applyingPreferences = false; }
    }
    public async Task RestorePreferencesAsync(CancellationToken cancellationToken = default)
    {
        if (disposed) return;
        try { await preferences.LoadAsync(cancellationToken); if (!disposed) preferenceError = ""; }
        catch (OperationCanceledException) when (disposed || cancellationToken.IsCancellationRequested) { return; }
        catch (Exception e) { if (!disposed) { preferenceError = e.Message; SetStatus("Designer preferences were not loaded: " + e.Message); } }
        finally { if (!disposed) { preferencesLoaded = true; NotifyState(); } }
    }
    /// <summary>Hosts can flush before a controlled close. Dispose cancels outstanding work, not an asynchronous disk flush.</summary>
    public async Task<bool> FlushPreferencesAsync(CancellationToken cancellationToken = default)
    {
        if (disposed) return false; preferenceTimer.Stop();
        try
        {
            await preferences.FlushWhenReadyAsync(() => !disposed && !sideSplitter.IsDragging && !treeSplitter.IsDragging, cancellationToken);
            if (disposed) return false;
            preferenceError = ""; NotifyState(); return !preferences.HasPending;
        }
        catch (OperationCanceledException) when (disposed || cancellationToken.IsCancellationRequested) { return false; }
        catch (Exception e)
        {
            if (!disposed) { preferenceError = e.Message; SetStatus("Preferences remain unsaved: " + e.Message); }
            return false;
        }
    }
    private async Task DesignerOptionsAsync()
    {
        var baseline = preferences.Current;
        var editor = new DesignerOptionsEditor(baseline);
        var registered = new HashSet<string>(StringComparer.Ordinal);
        void Register(string key, FrameworkElement value) { commands[key] = value; registered.Add(key); }
        foreach (var pair in editor.Editors) Register(pair.Key, pair.Value);
        var error = new TextBlock { Text = preferenceError, TextWrapping = TextWrapping.Wrap, FontSize = 11, Foreground = ControlTheme.Brush("#A3372E") };
        Register("options-error", error);
        var content = new StackPanel { Spacing = 6, Width = Math.Clamp(ActualWidth - 120, 280, 600) };
        content.Children.Add(new ScrollViewer { Content = editor, MaxHeight = Math.Clamp(ActualHeight - 240, 180, 460), VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        content.Children.Add(error);
        var dialog = new ContentDialog { Title = "Designer Options", Content = content, XamlRoot = XamlRoot,
            PrimaryButtonText = "Apply", SecondaryButtonText = "Restore defaults", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Primary };
        dialog.Resources["ContentDialogMaxWidth"] = 720d;
        dialog.Opened += (_, _) =>
        {
            var pending = new Stack<DependencyObject>(); pending.Push(dialog);
            while (pending.TryPop(out var node))
            {
                if (node is Button button)
                {
                    if (button.Name == "PrimaryButton" || button.Content is string text && text == "Apply") Register("options-dialog-apply", button);
                    if (button.Name == "SecondaryButton" || button.Content is string reset && reset == "Restore defaults") Register("options-dialog-defaults", button);
                    if (button.Name == "CloseButton" || button.Content is string close && close == "Cancel") Register("options-dialog-cancel", button);
                }
                for (int n = 0; n < VisualTreeHelper.GetChildrenCount(node); n++) pending.Push(VisualTreeHelper.GetChild(node, n));
            }
            NotifyState();
        };
        dialog.SecondaryButtonClick += (_, e) => { editor.RestoreDefaults(); error.Text = "Defaults are in the draft. Apply to save, or Cancel to keep your settings."; e.Cancel = true; };
        dialog.PrimaryButtonClick += (_, e) =>
        {
            try
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                if (!ReferenceEquals(baseline, preferences.Current)) throw new InvalidOperationException("Preferences changed while this dialog was open. Cancel and reopen it to edit the current settings.");
                var value = editor.ReadValue(); preferences.Update(value, forceSave: true);
            }
            catch (Exception problem) { error.Text = problem.Message; e.Cancel = true; }
        };
        try
        {
            if (await dialog.ShowAsync() == ContentDialogResult.Primary && !disposed)
            {
                if (await FlushPreferencesAsync()) SetStatus(preferences.CanPersist ? "Designer preferences saved locally." : "Designer preferences applied for this workspace.");
            }
        }
        finally { foreach (var key in registered) commands.Remove(key); NotifyState(); }
    }
}
