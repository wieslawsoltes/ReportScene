using ReportScene.Serialization;
using ReportScene.Model;

namespace ReportScene.Validation;

public static class ImportDefaultTests
{
    public static IEnumerable<TestResult> Run()
    {
        TestResult Test(string name, Action action)
        { try { action(); return new(name, true); } catch (Exception e) { return new(name, false, e.ToString()); } }
        yield return Test("minimal native document retains initialized defaults", () =>
        {
            var document = ReportJson.Deserialize("{}");
            Conformance.Equal(1, document.Pages.Count); Conformance.Equal("en-US", document.Culture);
            Conformance.Equal(0, document.Data.Count); Conformance.Equal(595.276, document.Pages[0].Width);
        });
        yield return Test("partial object and style defaults are normalized", () =>
        {
            var document = ReportJson.Deserialize("{\"pages\":[{\"bands\":[{\"items\":[{\"$type\":\"text\",\"text\":\"Hello\",\"style\":{\"bold\":true}}]}]}]}");
            var item = (TextItem)document.Pages[0].Bands[0].Items[0];
            Conformance.Equal("Hello", item.Text); Conformance.Equal(true, item.Style.Bold);
            Conformance.Equal(10d, item.Style.FontSize); Conformance.Equal("#202020", item.Style.Foreground);
            Conformance.Equal(true, !string.IsNullOrEmpty(item.Id));
        });
        yield return Test("explicit null required collection is rejected", () => Conformance.Throws<FormatException>(() => ReportJson.Deserialize("{\"pages\":null}")));
        yield return Test("explicit null style is rejected", () => Conformance.Throws<FormatException>(() => ReportJson.Deserialize("{\"pages\":[{\"bands\":[{\"items\":[{\"$type\":\"text\",\"style\":null}]}]}]}")));
        yield return Test("unknown native properties are diagnosed", () => Conformance.Throws<FormatException>(() => ReportJson.Deserialize("{\"unrecognizedFutureFeature\":42}")));
        yield return Test("omitted object identifiers remain unique", () =>
        {
            var document = ReportJson.Deserialize("{\"pages\":[{\"bands\":[{\"items\":[{\"$type\":\"text\"},{\"$type\":\"text\"}]}]}]}");
            var items = document.Pages[0].Bands[0].Items;
            Conformance.Equal(false, items[0].Id == items[1].Id);
        });
    }
}
