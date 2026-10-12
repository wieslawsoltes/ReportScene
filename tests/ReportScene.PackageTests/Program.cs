using ReportScene.Data;
using ReportScene.Data.AdoNet;
using ReportScene.Designer;
using ReportScene.Engine;
using ReportScene.Matrix;
using ReportScene.Model;
using ReportScene.Serialization;

int passed = 0;
void Check(bool condition, string name)
{ if (!condition) throw new InvalidOperationException(name); Console.WriteLine("PASS packaged " + name); passed++; }
var xml = XmlDataReader.Read("<d><r><Amount>12.50</Amount></r><r><Amount>7.25</Amount></r></d>", "Sales", new(InferNumbers: true));
Check(xml.Rows.Count == 2 && xml.Rows[0]["Amount"].Number == 12.5m, "XML provider");
var source = new System.Data.DataTable("Host"); source.Columns.Add("Amount", typeof(decimal)); source.Rows.Add(2.5m);
Check(AdoDataReader.FromDataTable(source).Rows[0]["Amount"].Number == 2.5m, "ADO.NET adapter");
var matrix = new MatrixEngine().Build(new MatrixItem { Measures = [new() { Expression = "Amount" }] }, xml.Rows);
Check(matrix.GrandTotal(0).Number == 19.75m, "matrix engine");
var report = new ReportDocument { Data = [xml], Pages = [new() { Bands = [new() { Kind = BandKind.ReportSummary, Items = [new TextItem { Id = "sum", Text = "[Sum(Sales.Amount)]" }] }] }] };
var clone = ReportJson.Clone(report); Check(clone.Data[0].Rows[0]["Amount"].Number == 12.50m, "source-generated serialization");
var session = new DesignerSession(clone); session.Select("sum"); session.UpdateSelection("Rename", item => item with { Name = "Edited" }); session.Undo();
Check(session.CanRedo, "designer undo history");
var prepared = new ReportPreparer().Prepare(clone); Check(prepared.Pages[0].Items.Single(i => i.Source is TextItem).Text == "19.75", "engine and transitive expressions");
var target = new ReportPage { Name = "Detail", Bands = [new() { Kind = BandKind.ReportTitle, Items = [new TextItem { Name = "ChildValue", Text = "[Parameters.Factor * Sum(Sales.Amount)]" }] }] };
session.AddSubreport(target, new(0, 40, 220, 24));
session.UpdateSelection("Bind", i => ((SubreportItem)i) with { ParameterBindings = [new("Factor", "2")] });
var composed = new ReportPreparer().Prepare(ReportJson.Clone(session.Document));
Check(composed.Pages.Count == 1 && composed.Pages[0].Items.Single(i => i.Source.Name == "ChildValue").Text == "39.50", "scoped subreport preparation");
session.SetPage(1); session.UpdatePage(session.Page with { Name = "RenamedDetail" });
Check(DocumentTree.AllItems(session.Document).OfType<SubreportItem>().Single().PageName == "RenamedDetail", "reference-safe page rename");
var index = new PreparedPageIndex(composed.Pages[0]);
Check(index.Query(new(0, 0, index.Page.Width, index.Page.Height), out int examined).Count > 0 && examined <= index.Count, "immutable prepared-page index");
var search = new PreparedSearchSession(composed); search.SetQuery("39.50", new(MatchCase: true, WholeWord: true));
Check(search.Matches.Count == 1 && search.MovePrevious()!.Page == 1, "advanced prepared search session");
var intervals = new VerticalIntervalIndex([(20d, 30d), (0d, 10d)]);
Check(intervals.Query(0, 25, out _).SequenceEqual([0, 1]), "ordered pagination interval index");
var tracker = new DesignerActivityTracker(session); session.MarkSaved();
Check(!tracker.Observe(session).SaveRecovery, "revision-aware designer activity");
var keys = new DataKeyIndex(xml.Rows, "Amount");
Check(keys.Find(12.50m, out _).SequenceEqual([0]), "column-key snapshot index");
using var operations = new DesignerOperations(session);
var written = session.CaptureRevision(); session.UpdateParameters([new("Factor", 3)]);
Check(!session.MarkSaved(written) && session.IsDirty, "captured save revision preserves newer edits");
Check((await operations.SaveAsync((snapshot, _) => Task.FromResult(snapshot.Json.Contains("Factor")))).Current && !session.IsDirty, "awaited operations packaged API");
var captured = session.CaptureTarget(); session.SetPage(0);
Check(!session.IsCurrent(captured), "awaited target invalidation");
var geometry = DesignerGeometry.Resize(new(20, 30, 100, 80), 10, 15, ResizeHandle.TopLeft);
Check(geometry == new RectD(30, 45, 90, 65) && DesignerGeometry.PointsPerUnit(DesignerUnit.Inches) == 72, "resize geometry and display units");
var outline = DesignerOutline.Report(session.Document); var filtered = outline.Filter("ChildValue");
Check(filtered.Count == 3 && ReferenceEquals(filtered, outline.Filter("ChildValue")) && outline.QueryScanCount == 1, "cached hierarchical outline filtering");
var batch = new DesignerSession(new() { Pages = [new() { Bands = [new() { Items = [new TextItem { Id = "a" }, new TextItem { Id = "b" }] }] }] });
batch.SelectAll(); batch.Move(10, 10); int scan = batch.LastReplacementScanCount; batch.Undo();
Check(scan == 2 && batch.Selection.All(i => i.Bounds.X == 0), "atomic batch geometry and undo");
batch.Select("a"); batch.BeginDrag(); batch.PreviewResize(8, 8, ResizeHandle.BottomRight); batch.CommitDrag();
Check(batch.Selection.Single().Bounds.Width == 128, "directional resize session");
batch.SetSelectionLocked(true); batch.SetSelectionLocked(false); batch.Undo();
Check(batch.Selection.Single().Locked, "reversible selection locking");
batch.SelectPage(); double oldWidth = batch.Page.Width; batch.UpdatePageProperties(batch.Page with { Width = 700 }); batch.Undo();
Check(batch.SelectedBand is null && batch.Page.Width == oldWidth, "validated page properties preserve page selection through undo");
Check(DesignLayout.Create(batch.Page, false).Paper.Height < DesignLayout.Create(batch.Page).Paper.Height, "compact band paper extent");
var layoutCache = new TextLayoutCache(new ApproximateTextMeasurer()); var style = new ItemStyle();
string wrapped = layoutCache.GetWrappedText("Reusable cached text", 50, style);
Check(wrapped == layoutCache.GetWrappedText("Reusable cached text", 50, style) && layoutCache.Hits == 1 && layoutCache.Misses == 1, "bounded authoring text cache");
var projection = new DesignerOutlineProjection(outline, "ChildValue");
Check(projection.Count == 3 && projection.ContainsKey(projection.Last().Key) && !projection.ContainsKey("absent"), "indexed immutable outline projection");
var epoch = session.DocumentId; session.MarkSaved(); session.SetPage(1);
Check(session.DocumentId == epoch, "stable loaded-document identity");
var navigation = new DesignerActivityTracker(session); session.MarkSaved(); var activity = navigation.Observe(session);
Check(!activity.RefreshStructure && !activity.SaveRecovery && !activity.InvalidatePrepared, "save skips structural metadata work");
batch.Select("a"); batch.SetSelectionLocked(false); batch.SelectAll(); var layout = DesignLayout.Create(batch.Page);
var point = layout.Objects[0].Bounds;
Check(layout.HitSelectedHandle(batch.SelectedIds, point.X + point.Width, point.Y, 1)?.Handle == ResizeHandle.TopRight, "selection handle hit test packaged API");
session.Load(session.Document); Check(session.DocumentId != epoch, "reload issues a distinct document identity");
var options = new DesignerPreferences { SidebarWidth = 600, TreeHeight = 450, Unit = DesignerUnit.Inches };
Check(options == DesignerPreferencesJson.Deserialize(DesignerPreferencesJson.Serialize(options)), "versioned preference wire format");
Check(DesignerPreferencesJson.Deserialize("{\"version\":1}") == new DesignerPreferences(), "preference defaults retained by generated serialization");
var reduced = DesignerPaneLayout.Calculate(options, 500, 500); var expanded = DesignerPaneLayout.Calculate(options, 1600, 1000);
Check(reduced.SidebarWidth == 223 && expanded.SidebarWidth == 600 && options.SidebarWidth == 600, "viewport layout preserves desired dimensions");
var preferenceStore = new PackagePreferenceStore(); using var settings = new DesignerPreferenceSession(preferenceStore);
for (int n = 0; n < 32; n++) settings.Update(settings.Current with { SidebarWidth = 300 + n });
await settings.FlushWhenReadyAsync(() => false); bool deferred = settings.HasPending && preferenceStore.Writes == 0;
await settings.FlushAsync();
Check(deferred && preferenceStore.Writes == 1 && DesignerPreferencesJson.Deserialize(preferenceStore.Json!).SidebarWidth == 331, "coalesced preference persistence and readiness");
string untouchedReport = ReportJson.Serialize(session.Document); using var restored = new DesignerPreferenceSession(preferenceStore);
Check(await restored.LoadAsync() && restored.Current.SidebarWidth == 331 && untouchedReport == ReportJson.Serialize(session.Document), "optional store load does not mutate report data");
string directory = Path.Combine(Path.GetTempPath(), "ReportScene-package-preferences-" + Guid.NewGuid().ToString("N"));
try
{
    var fileStore = new FileDesignerPreferenceStore(Path.Combine(directory, "settings.json"));
    await fileStore.SaveAsync(DesignerPreferencesJson.Serialize(options));
    Check(DesignerPreferencesJson.Deserialize((await fileStore.LoadAsync())!) == options, "native file preference store from isolated package");
}
finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
var pagePosition = new ReportPagePosition(3).Move(ReportPageCommand.Last);
Check(pagePosition.PageNumber == 3 && !pagePosition.CanNext && pagePosition.CanPrevious, "ordinal preview page navigation");
Check(!pagePosition.TrySelect("2147483648", out var unchanged) && unchanged == pagePosition, "invalid preview entry preserves page");
Check(pagePosition.TrySelect(" 1 ", out var firstPage) && firstPage.Index == 0 && !firstPage.CanPrevious, "trimmed page ordinal selection");
Check(ReportPreviewFit.Calculate(600, 800, 840, 1040, ReportFitMode.WholePage) == .9375, "whole-page point-to-DIP fitting");
var emptyPosition = new ReportPagePosition(0);
Check(emptyPosition.Move(ReportPageCommand.Last) == emptyPosition && !emptyPosition.TrySelect("1", out _), "empty preview boundary navigation");
Check(ReportPreviewViewport.Calculate(new(20, 16, 300, 400), 800, 600, .5) == new RectD(0, 0, 600, 800), "native viewport geometry converted to points");
Check(ReportPreviewViewport.Calculate(new(-300, -800, 2000, 5000), 800, 600, 2) == new RectD(149, 399, 402, 302), "native scroll viewport translation and pixel guard");
Check(ReportPreviewViewport.Calculate(new(1000, 1000, 100, 100), 800, 600, 1) == default, "offscreen preview has no visible candidate region");
Console.WriteLine($"{passed}/{passed} isolated package-consumer checks passed.");

sealed class PackagePreferenceStore : IDesignerPreferenceStore
{
    public string? Json; public int Writes;
    public Task<string?> LoadAsync(CancellationToken token = default) => Task.FromResult(Json);
    public Task SaveAsync(string json, CancellationToken token = default) { token.ThrowIfCancellationRequested(); Json = json; Writes++; return Task.CompletedTask; }
}
