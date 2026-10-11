using ReportScene.Engine;
using ReportScene.Model;

namespace ReportScene.Validation;

public static class PreparationCancellationTests
{
    private sealed class CancelMeasurer(CancellationTokenSource source, bool atHeight) : ITextMeasurer
    {
        public int WidthCalls;
        public double MeasureWidth(string text, ItemStyle style)
        { WidthCalls++; if (!atHeight && WidthCalls == 3) source.Cancel(); return text.Length * 5.5; }
        public double LineHeight(ItemStyle style) { if (atHeight) source.Cancel(); return 12.5; }
    }
    public static IEnumerable<TestResult> Run()
    {
        TestResult Test(string name, Action action) { try { action(); return new("preparation cancellation: " + name, true); } catch (Exception e) { return new("preparation cancellation: " + name, false, e.ToString()); } }
        ReportDocument Document() => new() { Pages = [new() { Bands = [new()
        { Kind = BandKind.ReportTitle, Items = [new TextItem { Text = new string('x', 32768), CanGrow = true, Bounds = new(0, 0, 40, 20) }] }] }] };
        yield return Test("cancellation inside long text measurement stops before further probes", () =>
        {
            using var cancellation = new CancellationTokenSource(); var metrics = new CancelMeasurer(cancellation, false);
            Conformance.Throws<OperationCanceledException>(() => new ReportPreparer(metrics).Prepare(Document(), cancellationToken: cancellation.Token));
            Conformance.Equal(3, metrics.WidthCalls);
        });
        yield return Test("line-height cancellation is observed after the host callback", () =>
        {
            using var cancellation = new CancellationTokenSource(); var metrics = new CancelMeasurer(cancellation, true);
            var document = Document(); document.Pages[0].Bands[0].Items[0] = new TextItem { Text = "x" };
            Conformance.Throws<OperationCanceledException>(() => new ReportPreparer(metrics).Prepare(document, cancellationToken: cancellation.Token));
        });
        yield return Test("table cell shaping uses the same cancellation guard", () =>
        {
            using var cancellation = new CancellationTokenSource(); var metrics = new CancelMeasurer(cancellation, false);
            Conformance.Throws<OperationCanceledException>(() => new ReportPreparer(metrics).Prepare(PaginationPerformanceTests.TableReport(), cancellationToken: cancellation.Token));
            Conformance.Equal(3, metrics.WidthCalls);
        });
    }
}
