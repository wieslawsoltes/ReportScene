using ReportScene.Designer;

namespace ReportScene.Validation;

public static class DesignerPreferenceTests
{
    public static IEnumerable<TestResult> Run()
    {
        TestResult Test(string name, Action action)
        { try { action(); return new("designer preferences: " + name, true); } catch (Exception error) { return new("designer preferences: " + name, false, error.ToString()); } }
        yield return Test("default schema round trips without reflection", () =>
        {
            var value = new DesignerPreferences();
            Conformance.Equal(value, DesignerPreferencesJson.Deserialize(DesignerPreferencesJson.Serialize(value)));
        });
        yield return Test("all implemented options round trip", () =>
        {
            var value = new DesignerPreferences { SidebarWidth = 530, TreeHeight = 400, ShowTree = false, ShowProperties = false,
                ShowGrid = false, ShowRulers = false, ShowFullPaper = true, SnapToGrid = false, GridSize = 12,
                Unit = DesignerUnit.Millimeters, AlphabeticalProperties = true, ExplorerTab = DesignerExplorerTab.Report,
                DesignZoom = 1.25, PreviewZoom = .5 };
            Conformance.Equal(value, DesignerPreferencesJson.Deserialize(DesignerPreferencesJson.Serialize(value)));
        });
        yield return Test("optional fields retain explicit version defaults", () =>
            Conformance.Equal(new DesignerPreferences(), DesignerPreferencesJson.Deserialize("{\"version\":1}")));
        yield return Test("unknown missing duplicate and future version fields are rejected", () =>
        {
            foreach (string json in new[] { "{}", "null", "[]", "{\"version\":2}", "{\"version\":1,\"version\":1}",
                "{\"version\":1,\"reportPath\":\"unexpected\"}", "{\"Version\":1}" })
                Conformance.Throws<FormatException>(() => DesignerPreferencesJson.Deserialize(json));
        });
        yield return Test("storage size and nesting are bounded", () =>
        {
            Conformance.Throws<FormatException>(() => DesignerPreferencesJson.Deserialize(new string(' ', DesignerPreferencesJson.MaximumCharacters + 1)));
            Conformance.Throws<FormatException>(() => DesignerPreferencesJson.Deserialize(""));
            Conformance.Throws<FormatException>(() => DesignerPreferencesJson.Deserialize("{\"version\":1,\"unit\":[[[[[[[[[[0]]]]]]]]]]}"));
        });
        yield return Test("invalid numerical and enum values cannot be serialized", () =>
        {
            var value = new DesignerPreferences();
            foreach (var invalid in new[] { value with { SidebarWidth = 179 }, value with { SidebarWidth = 641 },
                value with { TreeHeight = 95 }, value with { TreeHeight = 481 }, value with { GridSize = 0 },
                value with { GridSize = double.NaN }, value with { DesignZoom = double.PositiveInfinity },
                value with { PreviewZoom = .09 }, value with { Unit = (DesignerUnit)100 }, value with { ExplorerTab = (DesignerExplorerTab)100 } })
                Conformance.Throws<ArgumentException>(() => DesignerPreferencesJson.Serialize(invalid));
        });
        yield return Test("invalid stored values do not silently clamp", () =>
        {
            foreach (string json in new[] { "{\"version\":1,\"sidebarWidth\":900}", "{\"version\":1,\"gridSize\":\"NaN\"}",
                "{\"version\":1,\"unit\":99}", "{\"version\":1,\"showTree\":\"false\"}" })
                Conformance.Throws<FormatException>(() => DesignerPreferencesJson.Deserialize(json));
        });
        yield return Test("resize down and back restores preferred geometry without mutating settings", () =>
        {
            var preferences = new DesignerPreferences { SidebarWidth = 640, TreeHeight = 480 };
            string before = DesignerPreferencesJson.Serialize(preferences);
            var large = DesignerPaneLayout.Calculate(preferences, 1400, 1000);
            var small = DesignerPaneLayout.Calculate(preferences, 500, 500);
            Conformance.Equal(640d, large.SidebarWidth); Conformance.Equal(480d, large.TreeHeight);
            Conformance.Equal(223d, small.SidebarWidth); Conformance.Equal(296d, small.TreeHeight);
            Conformance.Equal(large, DesignerPaneLayout.Calculate(preferences, 1400, 1000));
            Conformance.Equal(before, DesignerPreferencesJson.Serialize(preferences));
        });
        yield return Test("narrow viewport hides panes without discarding preferred visibility", () =>
        {
            var preferences = new DesignerPreferences();
            var narrow = DesignerPaneLayout.Calculate(preferences, 400, 800);
            Conformance.Equal(false, narrow.HasSidebar); Conformance.Equal(0d, narrow.SidebarWidth);
            var restored = DesignerPaneLayout.Calculate(preferences, 1000, 800);
            Conformance.Equal(true, restored.ShowTree && restored.ShowProperties);
        });
        yield return Test("short viewport temporarily prioritizes properties and restores split later", () =>
        {
            var preferences = new DesignerPreferences();
            var shortView = DesignerPaneLayout.Calculate(preferences, 1000, 250);
            Conformance.Equal(false, shortView.ShowTree); Conformance.Equal(true, shortView.ShowProperties);
            Conformance.Equal(250d, shortView.PropertiesHeight);
            Conformance.Equal(true, DesignerPaneLayout.Calculate(preferences, 1000, 800).HasInnerDivider);
        });
        yield return Test("single visible panes fill available height", () =>
        {
            var onlyTree = DesignerPaneLayout.Calculate(new() { ShowProperties = false }, 1000, 200);
            Conformance.Equal(200d, onlyTree.TreeHeight); Conformance.Equal(0d, onlyTree.PropertiesHeight);
            var onlyProperties = DesignerPaneLayout.Calculate(new() { ShowTree = false }, 1000, 700);
            Conformance.Equal(700d, onlyProperties.PropertiesHeight); Conformance.Equal(false, onlyProperties.HasInnerDivider);
        });
        yield return Test("zero viewport and explicitly closed panes produce no negative geometry", () =>
        {
            foreach (var view in new[] { DesignerPaneLayout.Calculate(new(), 0, 0),
                DesignerPaneLayout.Calculate(new() { ShowTree = false, ShowProperties = false }, 1200, 900) })
            { Conformance.Equal(false, view.HasSidebar); Conformance.Equal(0d, view.SidebarWidth + view.TreeHeight + view.PropertiesHeight); }
        });
        yield return Test("invalid viewport is rejected before layout", () =>
        {
            foreach (double value in new[] { -1d, double.NaN, double.PositiveInfinity })
            {
                Conformance.Throws<ArgumentOutOfRangeException>(() => DesignerPaneLayout.Calculate(new(), value, 800));
                Conformance.Throws<ArgumentOutOfRangeException>(() => DesignerPaneLayout.Calculate(new(), 1000, value));
            }
        });
        yield return Test("tree maximum stays bounded in very tall workspaces", () =>
        {
            var view = DesignerPaneLayout.Calculate(new() { TreeHeight = 480 }, 5000, 5000);
            Conformance.Equal(480d, view.MaximumTreeHeight); Conformance.Equal(640d, view.MaximumSidebarWidth);
            Conformance.Equal(480d, view.TreeHeight);
        });
    }
}
