using System.Collections;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using ReportScene.Data;
using ReportScene.Export;
using ReportScene.Model;

namespace ReportScene.Validation;

public static class ExportWorkTests
{
    public static IEnumerable<TestResult> Run()
    {
        TestResult Test(string name, Action action) { try { action(); return new("export work: " + name, true); } catch (Exception e) { return new("export work: " + name, false, e.ToString()); } }
        ReportDocument Data() => new() { Data = [XmlDataReader.Read("<data><row><Name>Alpha</Name><Amount>12</Amount></row><row><Name>Beta</Name><Amount>24</Amount></row></data>", "Rows")] };
        PreparedReport Report(IReadOnlyList<PreparedPage> pages) => new("Data export", pages, [], TimeSpan.Zero);
        foreach (int count in new[] { 0, 1, 1000000, int.MaxValue })
        {
            int pageCount = count;
            yield return Test("CSV does not enumerate or copy " + pageCount + " unrelated pages", () =>
            {
                var pages = new UnreadablePages(pageCount); var report = Report(pages); var data = Data();
                byte[] expected = Encoding.UTF8.GetBytes(DataReaders.WriteCsv(data.Data[0]));
                var exporter = new ReportExporter();
                var legacy = exporter.Export(report, ExportFormat.Csv, new(FirstPage: 99, LastPage: 100), data);
                var ordinal = exporter.ExportSelection(report, ExportFormat.Csv, ReportPageSelection.Create(pageCount), document: data);
                Conformance.Equal(true, expected.SequenceEqual(legacy.Content) && expected.SequenceEqual(ordinal.Content));
                Conformance.Equal("Data export.csv", ordinal.Name); Conformance.Equal("text/csv", ordinal.MediaType);
                Conformance.Equal(0, pages.Reads); Conformance.Equal(0, pages.Enumerations);
            });
        }
        yield return Test("CSV guards reject stale/current selections without visiting pages", () =>
        {
            var pages = new UnreadablePages(3); var report = Report(pages); var exporter = new ReportExporter();
            Conformance.Throws<ArgumentException>(() => exporter.ExportSelection(report, ExportFormat.Csv, ReportPageSelection.Create(2), document: Data()));
            Conformance.Throws<ArgumentException>(() => exporter.ExportSelection(report, ExportFormat.Csv, ReportPageSelection.Create(3, PageSelectionMode.Current, 1), document: Data()));
            Conformance.Throws<ArgumentException>(() => exporter.ExportSelection(report, ExportFormat.Csv, ReportPageSelection.Create(3), new(FirstPage: 2), Data()));
            Conformance.Equal(0, pages.Reads); Conformance.Equal(0, pages.Enumerations);
        });
        yield return Test("missing tables and report errors remain explicit for data-only exports", () =>
        {
            var pages = new UnreadablePages(1000); var report = Report(pages); var exporter = new ReportExporter();
            Conformance.Throws<ArgumentException>(() => exporter.Export(report, ExportFormat.Csv));
            Conformance.Throws<ArgumentException>(() => exporter.ExportSelection(report, ExportFormat.Csv, ReportPageSelection.Create(1000), document: new ReportDocument()));
            var invalid = report with { Diagnostics = [new("EXPORT_TEST", "Invalid report", DiagnosticSeverity.Error)] };
            Conformance.Throws<InvalidOperationException>(() => exporter.Export(invalid, ExportFormat.Csv, document: Data()));
            Conformance.Equal(0, pages.Reads); Conformance.Equal(0, pages.Enumerations);
        });
        yield return Test("pre-cancelled export never starts enumerating a page source", () =>
        {
            var pages = new UnreadablePages(1000); var report = Report(pages); var exporter = new ReportExporter();
            using var cancel = new CancellationTokenSource(); cancel.Cancel();
            foreach (var format in Enum.GetValues<ExportFormat>())
            {
                Conformance.Throws<OperationCanceledException>(() => exporter.Export(report, format, document: Data(), cancellationToken: cancel.Token));
                Conformance.Throws<OperationCanceledException>(() => exporter.ExportSelection(report, format, ReportPageSelection.Create(1000), document: Data(), cancellationToken: cancel.Token));
            }
            Conformance.Equal(0, pages.Reads); Conformance.Equal(0, pages.Enumerations);
        });
        yield return Test("physical range filtering notices cancellation between pages", () =>
        {
            using var cancel = new CancellationTokenSource(); var pages = new CancellingPages(cancel);
            Conformance.Throws<OperationCanceledException>(() => new ReportExporter().Export(Report(pages), ExportFormat.Text, cancellationToken: cancel.Token));
            Conformance.Equal(2, pages.Visits);
        });
        yield return Test("data archive bytes names and table order are unchanged", () =>
        {
            var data = Data(); data.Data.Add(XmlDataReader.Read("<data><row><Value>Second table</Value></row></data>", "Second Table"));
            var pages = new UnreadablePages(1000);
            var output = new ReportExporter().ExportSelection(Report(pages), ExportFormat.Csv, ReportPageSelection.Create(1000), document: data);
            using var archive = new ZipArchive(new MemoryStream(output.Content));
            Conformance.Equal("application/zip", output.MediaType); Conformance.Equal(2, archive.Entries.Count);
            Conformance.Equal(true, archive.Entries.Select(e => e.FullName).SequenceEqual(["001-Rows.csv", "002-Second_Table.csv"]));
            for (int n = 0; n < data.Data.Count; n++)
            {
                using var stream = archive.Entries[n].Open(); using var reader = new StreamReader(stream, Encoding.UTF8);
                Conformance.Equal(DataReaders.WriteCsv(data.Data[n]), reader.ReadToEnd());
            }
            Conformance.Equal(0, pages.Reads); Conformance.Equal(0, pages.Enumerations);
        });
        yield return Test("PNG SVG archive disambiguation preserves source order and unique legacy names", () =>
        {
            var numbers = new[] { 7, 2, 7, 0 };
            var report = Report(numbers.Select(n => new PreparedPage(n, 16, 16, [])).ToArray());
            foreach (var format in new[] { ExportFormat.Png, ExportFormat.Svg })
            {
                var file = new ReportExporter().ExportSelection(report, format, ReportPageSelection.Create(4), new(Dpi: 72));
                using var archive = new ZipArchive(new MemoryStream(file.Content)); string extension = format == ExportFormat.Png ? "png" : "svg";
                Conformance.Equal(true, archive.Entries.Select(e => e.FullName).SequenceEqual(new[] {
                    "page-0007-position-0001." + extension, "page-0002." + extension,
                    "page-0007-position-0003." + extension, "page-0000." + extension }));
                Conformance.Equal(true, archive.Entries.All(e => e.Length > 0));
            }
        });
        yield return Test("text and prepared JSON retain selection ordering and physical page labels", () =>
        {
            var report = Report(new[] { 7, 2, 7 }.Select((n, i) => new PreparedPage(n, 100, 100,
                [new(new TextItem(), new(10, 10, 60, 20), "Position " + (i + 1))])).ToArray());
            var exporter = new ReportExporter(); var selection = ReportPageSelection.Create(3, PageSelectionMode.Custom, pageNumbers: "3,1");
            string text = Encoding.UTF8.GetString(exporter.ExportSelection(report, ExportFormat.Text, selection).Content);
            Conformance.Equal("Position 1\n\f\nPosition 3", text);
            using var json = JsonDocument.Parse(exporter.ExportSelection(report, ExportFormat.PreparedJson, selection).Content);
            var pages = json.RootElement.GetProperty("pages"); Conformance.Equal(2, pages.GetArrayLength());
            Conformance.Equal(7, pages[0].GetProperty("number").GetInt32()); Conformance.Equal(7, pages[1].GetProperty("number").GetInt32());
            Conformance.Equal(3, report.Pages.Count);
        });
    }
    private sealed class UnreadablePages(int count) : IReadOnlyList<PreparedPage>
    {
        public int Count => count;
        public int Reads { get; private set; }
        public int Enumerations { get; private set; }
        public PreparedPage this[int index] { get { Reads++; throw new InvalidOperationException("Data-only export read a prepared page."); } }
        public IEnumerator<PreparedPage> GetEnumerator() { Enumerations++; throw new InvalidOperationException("Data-only export enumerated prepared pages."); }
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
    private sealed class CancellingPages(CancellationTokenSource cancel) : IReadOnlyList<PreparedPage>
    {
        public int Count => 100;
        public int Visits { get; private set; }
        public PreparedPage this[int index] => new(index + 1, 100, 100, []);
        public IEnumerator<PreparedPage> GetEnumerator()
        {
            for (int n = 0; n < Count; n++)
            {
                if (n == 1) cancel.Cancel(); Visits++;
                yield return this[n];
            }
        }
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
