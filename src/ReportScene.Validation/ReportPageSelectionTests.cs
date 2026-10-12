using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using ReportScene.Export;
using ReportScene.Model;

namespace ReportScene.Validation;

public static class ReportPageSelectionTests
{
    public static IEnumerable<TestResult> Run()
    {
        TestResult Test(string name, Action action) { try { action(); return new("export selection: " + name, true); } catch (Exception e) { return new("export selection: " + name, false, e.ToString()); } }
        PreparedReport Fixture() => new("Range fixture", new[] { 0, 7, 7, 20 }.Select((number, n) => new PreparedPage(number, 100, 100,
            [new(new TextItem(), new(10, 10, 70, 20), "Position " + (n + 1))])).ToArray(), [], TimeSpan.Zero);
        yield return Test("all and current modes use displayed ordinals", () =>
        {
            Conformance.Equal(true, ReportPageSelection.Create(4).SequenceEqual([0, 1, 2, 3]));
            Conformance.Equal(true, ReportPageSelection.Create(4, PageSelectionMode.Current, 2).SequenceEqual([2]));
        });
        yield return Test("interval normalization sorts coalesces and deduplicates", () =>
        {
            var s = ReportPageSelection.Create(12, PageSelectionMode.Custom, pageNumbers: "8, 3-5, 1, 4-6, 8, 9-10");
            Conformance.Equal(true, s.SequenceEqual([0, 2, 3, 4, 5, 7, 8, 9])); Conformance.Equal(3, s.IntervalCount);
            for (int n = 0; n < s.Count; n++) Conformance.Equal(s.ElementAt(n), s[n]);
        });
        yield return Test("large spans are represented without expanding the page list", () =>
        {
            var s = ReportPageSelection.Create(int.MaxValue, PageSelectionMode.Custom, pageNumbers: "1-2147483647");
            Conformance.Equal(int.MaxValue, s.Count); Conformance.Equal(1, s.IntervalCount); Conformance.Equal(int.MaxValue - 1, s[s.Count - 1]);
            s = ReportPageSelection.Create(int.MaxValue, PageSelectionMode.Custom, pageNumbers: "1-3,2147483646-2147483647");
            Conformance.Equal(5, s.Count); Conformance.Equal(int.MaxValue - 1, s[4]);
        });
        yield return Test("empty reports cannot select nonexistent current or custom pages", () =>
        {
            Conformance.Equal(0, ReportPageSelection.Create(0).Count);
            Conformance.Throws<ArgumentException>(() => ReportPageSelection.Create(0, PageSelectionMode.Current));
            Conformance.Throws<ArgumentException>(() => ReportPageSelection.Create(0, PageSelectionMode.Custom, pageNumbers: "1"));
        });
        foreach (string invalid in new[] { "", " ", "0", "5", "-1", "+1", "2.5", "2e0", "1,,2", ",1", "1,", "1-", "-2", "3-1", "1-2-3", "2147483648", "1;2" })
        {
            var expression = invalid;
            yield return Test("reject malformed or out-of-range input " + expression, () =>
                Conformance.Throws<FormatException>(() => ReportPageSelection.Create(4, PageSelectionMode.Custom, pageNumbers: expression)));
        }
        yield return Test("bounded syntax does not partially select on error", () =>
        {
            Conformance.Throws<FormatException>(() => ReportPageSelection.Create(5, PageSelectionMode.Custom, pageNumbers: new string('1', 4097)));
            Conformance.Throws<FormatException>(() => ReportPageSelection.Create(5, PageSelectionMode.Custom, pageNumbers: string.Join(',', Enumerable.Repeat("1", 513))));
        });
        yield return Test("culture and whitespace do not change ordinal parsing", () =>
        {
            var old = CultureInfo.CurrentCulture;
            try { CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("pl-PL"); Conformance.Equal(true, ReportPageSelection.Create(5, PageSelectionMode.Custom, pageNumbers: " 01 , 3 - 4 ").SequenceEqual([0, 2, 3])); }
            finally { CultureInfo.CurrentCulture = old; }
        });
        yield return Test("invalid modes indices and cancellation fail before selection", () =>
        {
            Conformance.Throws<ArgumentOutOfRangeException>(() => ReportPageSelection.Create(-1));
            Conformance.Throws<ArgumentOutOfRangeException>(() => ReportPageSelection.Create(1, (PageSelectionMode)99));
            Conformance.Throws<ArgumentOutOfRangeException>(() => ReportPageSelection.Create(1, PageSelectionMode.Current, -1));
            Conformance.Throws<ArgumentOutOfRangeException>(() => { _ = ReportPageSelection.Create(1)[1]; });
            using var c = new CancellationTokenSource(); c.Cancel();
            Conformance.Throws<OperationCanceledException>(() => ReportPageSelection.Create(4, cancellationToken: c.Token));
            Conformance.Throws<OperationCanceledException>(() => ReportPageSelection.Create(4).Apply(Fixture(), c.Token));
        });
        yield return Test("selection snapshots the list but preserves original objects and numbering", () =>
        {
            var source = Fixture(); var mutable = source.Pages.ToList(); source = source with { Pages = mutable };
            var selected = ReportPageSelection.Create(4, PageSelectionMode.Custom, pageNumbers: "1,3").Apply(source);
            Conformance.Equal(true, ReferenceEquals(source.Pages[0], selected.Pages[0]));
            Conformance.Equal(true, selected.Pages.Select(p => p.Number).SequenceEqual([0, 7])); mutable.Clear(); Conformance.Equal(2, selected.Pages.Count);
            Conformance.Throws<NotSupportedException>(() => ((IList<PreparedPage>)selected.Pages).Clear());
            Conformance.Throws<ArgumentException>(() => ReportPageSelection.Create(2).Apply(Fixture()));
        });
        yield return Test("random interval unions agree with a simple page set", () =>
        {
            var random = new Random(12392);
            for (int round = 0; round < 100; round++)
            {
                var expected = new SortedSet<int>(); var tokens = new List<string>();
                for (int n = 0; n < 30; n++) { int a = random.Next(1, 101), b = random.Next(a, 101); tokens.Add(a + "-" + b); for (int p = a; p <= b; p++) expected.Add(p - 1); }
                var s = ReportPageSelection.Create(100, PageSelectionMode.Custom, pageNumbers: string.Join(',', tokens));
                Conformance.Equal(true, s.SequenceEqual(expected)); Conformance.Equal(true, Enumerable.Range(0, s.Count).Select(n => s[n]).SequenceEqual(expected));
            }
        });
        yield return Test("native prepared output selects positions without renumbering", () =>
        {
            var report = Fixture(); var file = new ReportExporter().ExportSelection(report, ExportFormat.PreparedJson,
                ReportPageSelection.Create(4, PageSelectionMode.Custom, pageNumbers: "1,3-4"));
            using var json = JsonDocument.Parse(file.Content); var pages = json.RootElement.GetProperty("pages");
            Conformance.Equal(3, pages.GetArrayLength()); Conformance.Equal(0, pages[0].GetProperty("number").GetInt32());
            Conformance.Equal(7, pages[1].GetProperty("number").GetInt32()); Conformance.Equal(20, pages[2].GetProperty("number").GetInt32());
            Conformance.Equal(4, report.Pages.Count);
        });
        yield return Test("legacy physical range filter retains its previous semantics", () =>
        {
            var exporter = new ReportExporter(); var report = Fixture();
            string text = Encoding.UTF8.GetString(exporter.Export(report, ExportFormat.Text, new(FirstPage: 7, LastPage: 7)).Content);
            Conformance.Equal(true, text.Contains("Position 2") && text.Contains("Position 3") && !text.Contains("Position 1"));
            Conformance.Throws<ArgumentException>(() => exporter.ExportSelection(report, ExportFormat.Text, ReportPageSelection.Create(4), new(FirstPage: 2)));
        });
        yield return Test("PNG and SVG archives use unique names for duplicate physical numbers", () =>
        {
            foreach (var format in new[] { ExportFormat.Png, ExportFormat.Svg })
            {
                var file = new ReportExporter().ExportSelection(Fixture(), format, ReportPageSelection.Create(4, PageSelectionMode.Custom, pageNumbers: "2-3"), new(Dpi: 72));
                using var zip = new ZipArchive(new MemoryStream(file.Content)); Conformance.Equal(2, zip.Entries.Count);
                Conformance.Equal(2, zip.Entries.Select(e => e.FullName).Distinct().Count());
                Conformance.Equal(true, zip.Entries.All(e => e.Length > 0 && e.FullName.Contains("position-")));
            }
        });
        yield return Test("CSV cannot pretend to filter source rows by report page", () =>
        {
            Conformance.Throws<ArgumentException>(() => new ReportExporter().ExportSelection(Fixture(), ExportFormat.Csv,
                ReportPageSelection.Create(4, PageSelectionMode.Current, 1)));
        });
    }
}
