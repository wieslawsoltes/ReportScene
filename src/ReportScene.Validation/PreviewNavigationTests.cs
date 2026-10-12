using System.Globalization;
using ReportScene.Model;

namespace ReportScene.Validation;

public static class PreviewNavigationTests
{
    public static IEnumerable<TestResult> Run()
    {
        TestResult Test(string name, Action action) { try { action(); return new("preview navigation: " + name, true); } catch (Exception e) { return new("preview navigation: " + name, false, e.ToString()); } }
        yield return Test("empty reports have no navigable page", () =>
        {
            var empty = new ReportPagePosition(0, 99);
            Conformance.Equal(0, empty.PageNumber); Conformance.Equal(false, empty.HasPages || empty.CanNext || empty.CanPrevious);
            foreach (var command in Enum.GetValues<ReportPageCommand>()) Conformance.Equal(empty, empty.Move(command));
            Conformance.Equal(false, empty.TrySelect("1", out var result)); Conformance.Equal(empty, result);
        });
        yield return Test("single page disables both directions", () =>
        {
            var position = new ReportPagePosition(1);
            Conformance.Equal(1, position.PageNumber); Conformance.Equal(false, position.CanNext || position.CanPrevious);
            foreach (var command in Enum.GetValues<ReportPageCommand>()) Conformance.Equal(position, position.Move(command));
        });
        yield return Test("first last next and previous clamp at boundaries", () =>
        {
            var position = new ReportPagePosition(5, 2);
            Conformance.Equal(0, position.Move(ReportPageCommand.First).Index); Conformance.Equal(4, position.Move(ReportPageCommand.Last).Index);
            Conformance.Equal(1, position.Move(ReportPageCommand.Previous).Index); Conformance.Equal(3, position.Move(ReportPageCommand.Next).Index);
            Conformance.Equal(0, new ReportPagePosition(5, -1).Move(ReportPageCommand.Previous).Index);
            Conformance.Equal(4, new ReportPagePosition(5, 99).Move(ReportPageCommand.Next).Index);
        });
        yield return Test("maximum counts and indices do not overflow", () =>
        {
            var position = new ReportPagePosition(int.MaxValue, int.MaxValue);
            Conformance.Equal(int.MaxValue, position.PageNumber); Conformance.Equal(position, position.Move(ReportPageCommand.Next));
            Conformance.Equal(int.MaxValue - 1, position.Move(ReportPageCommand.Previous).PageNumber);
            Conformance.Equal(true, position.TrySelect("2147483647", out var parsed)); Conformance.Equal(position, parsed);
        });
        yield return Test("invalid input retains exact position", () =>
        {
            var position = new ReportPagePosition(8, 2);
            foreach (string? text in new string?[] { null, "", " ", "0", "-1", "+1", "9", "1.0", "1,000", "1e0", "NaN", "2147483648", new('9', 10000) })
            { Conformance.Equal(false, position.TrySelect(text, out var result)); Conformance.Equal(position, result); }
        });
        yield return Test("valid ordinal input is trimmed and culture independent", () =>
        {
            var culture = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("pl-PL");
                var position = new ReportPagePosition(8);
                Conformance.Equal(true, position.TrySelect("  003\t", out var result)); Conformance.Equal(2, result.Index);
                Conformance.Equal(false, position.TrySelect("3,0", out _));
            }
            finally { CultureInfo.CurrentCulture = culture; }
        });
        yield return Test("invalid count and commands are rejected", () =>
        {
            Conformance.Throws<ArgumentOutOfRangeException>(() => new ReportPagePosition(-1));
            Conformance.Throws<ArgumentOutOfRangeException>(() => new ReportPagePosition(3).Move((ReportPageCommand)99));
        });
        yield return Test("whole page uses the limiting axis and retains paper gutters", () =>
        {
            double zoom = ReportPreviewFit.Calculate(600, 800, 840, 1040, ReportFitMode.WholePage)!.Value;
            Conformance.Equal(.9375, zoom);
            Conformance.Equal(true, 600 * zoom * 96 / 72 <= 800 && 800 * zoom * 96 / 72 <= 1000);
            Conformance.Equal(1d, ReportPreviewFit.Calculate(600, 800, 840, 1040, ReportFitMode.Width)!.Value);
        });
        yield return Test("landscape fitting uses width rather than portrait assumptions", () =>
        { Conformance.Equal(.375, ReportPreviewFit.Calculate(1200, 600, 640, 1040, ReportFitMode.WholePage)!.Value); });
        yield return Test("unmeasured or exhausted viewport does not request a new zoom", () =>
        {
            Conformance.Equal(true, ReportPreviewFit.Calculate(600, 800, 0, 0, ReportFitMode.Width) is null);
            Conformance.Equal(true, ReportPreviewFit.Calculate(600, 800, 800, 40, ReportFitMode.WholePage) is null);
            Conformance.Equal(true, ReportPreviewFit.Calculate(600, 800, 40, 800, ReportFitMode.WholePage) is null);
        });
        yield return Test("fit respects established preview zoom limits", () =>
        {
            Conformance.Equal(.1, ReportPreviewFit.Calculate(100000, 100000, 100, 100, ReportFitMode.WholePage)!.Value);
            Conformance.Equal(4d, ReportPreviewFit.Calculate(1, 1, 100000, 100000, ReportFitMode.WholePage)!.Value);
        });
        yield return Test("invalid geometry is rejected before applying surface dimensions", () =>
        {
            Conformance.Throws<ArgumentException>(() => ReportPreviewFit.Calculate(double.NaN, 100, 800, 600, ReportFitMode.Width));
            Conformance.Throws<ArgumentException>(() => ReportPreviewFit.Calculate(100, 0, 800, 600, ReportFitMode.Width));
            Conformance.Throws<ArgumentException>(() => ReportPreviewFit.Calculate(100, 100, -1, 600, ReportFitMode.Width));
            Conformance.Throws<ArgumentOutOfRangeException>(() => ReportPreviewFit.Calculate(100, 100, 800, 600, (ReportFitMode)99));
        });
    }
}
