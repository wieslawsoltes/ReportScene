using System.Collections;
using System.Text;
using ReportScene.Export;
using ReportScene.Model;

namespace ReportScene.Validation;

public static class DataOnlyExportTests
{
    private sealed class CountOnlyPages(int count) : IReadOnlyList<PreparedPage>
    {
        public int Count => count;
        public int Reads { get; private set; }
        public PreparedPage this[int index] { get { Reads++; throw new InvalidOperationException("CSV must not inspect prepared pages."); } }
        public IEnumerator<PreparedPage> GetEnumerator() { Reads++; throw new InvalidOperationException("CSV must not enumerate prepared pages."); }
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
    public static IEnumerable<TestResult> Run()
    {
        TestResult Test(string name, Action action) { try { action(); return new("data-only export: " + name, true); } catch (Exception e) { return new("data-only export: " + name, false, e.ToString()); } }
        ReportDocument Document() => new() { Data = [new() { Name = "Rows", Columns = ["Value"], Rows = [new() { ["Value"] = "alpha" }] }] };
        PreparedReport Report(IReadOnlyList<PreparedPage> pages) => new("Data", pages, [], TimeSpan.Zero);
        yield return Test("CSV accesses no page elements at zero or maximum count", () =>
        {
            var exporter = new ReportExporter();
            foreach (int count in new[] { 0, 1, 100000, int.MaxValue })
            {
                var pages = new CountOnlyPages(count);
                var result = exporter.ExportSelection(Report(pages), ExportFormat.Csv, ReportPageSelection.Create(count), document: Document());
                Conformance.Equal(0, pages.Reads); Conformance.Equal(true, Encoding.UTF8.GetString(result.Content).Contains("alpha"));
            }
        });
        yield return Test("stale ordinal counts still reject data-only output", () =>
        {
            var pages = new CountOnlyPages(100000);
            Conformance.Throws<ArgumentException>(() => new ReportExporter().ExportSelection(Report(pages), ExportFormat.Csv, ReportPageSelection.Create(1), document: Document()));
            Conformance.Equal(0, pages.Reads);
        });
        yield return Test("current and custom page filters cannot silently filter table rows", () =>
        {
            var pages = new CountOnlyPages(3);
            foreach (var mode in new[] { PageSelectionMode.Current, PageSelectionMode.Custom })
                Conformance.Throws<ArgumentException>(() => new ReportExporter().ExportSelection(Report(pages), ExportFormat.Csv, ReportPageSelection.Create(3, mode, 1, "2"), document: Document()));
            Conformance.Equal(0, pages.Reads);
        });
        yield return Test("cancellation diagnostics and absent tables remain enforced", () =>
        {
            var pages = new CountOnlyPages(3); var report = Report(pages); var selection = ReportPageSelection.Create(3); var exporter = new ReportExporter();
            using var c = new CancellationTokenSource(); c.Cancel();
            Conformance.Throws<OperationCanceledException>(() => exporter.ExportSelection(report, ExportFormat.Csv, selection, document: Document(), cancellationToken: c.Token));
            Conformance.Throws<InvalidOperationException>(() => exporter.ExportSelection(report with { Diagnostics = [new("CSV_TEST", "Invalid report")] }, ExportFormat.Csv, selection, document: Document()));
            Conformance.Throws<ArgumentException>(() => exporter.ExportSelection(report, ExportFormat.Csv, selection));
            Conformance.Equal(0, pages.Reads);
        });
        yield return Test("valid CSV bytes equal the existing bound-table export", () =>
        {
            var report = Report([new PreparedPage(1, 100, 100, [])]); var document = Document(); var exporter = new ReportExporter();
            Conformance.Equal(true, exporter.Export(report, ExportFormat.Csv, document: document).Content.SequenceEqual(
                exporter.ExportSelection(report, ExportFormat.Csv, ReportPageSelection.Create(1), document: document).Content));
        });
    }
}
