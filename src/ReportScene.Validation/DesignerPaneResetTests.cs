using ReportScene.Designer;

namespace ReportScene.Validation;

public static class DesignerPaneResetTests
{
    public static IEnumerable<TestResult> Run()
    {
        TestResult Test(string name, Action body) { try { body(); return new("pane reset: " + name, true); } catch (Exception e) { return new("pane reset: " + name, false, e.ToString()); } }
        void Click(DesignerPaneResetGesture g) { g.Begin(); Conformance.Equal(false, g.Complete(false)); }
        yield return Test("early native double tap waits for an unmoved second release", () =>
        {
            var g = new DesignerPaneResetGesture(); Click(g); g.RequestReset();
            Conformance.Equal(false, g.TryCompleteReset()); g.Begin();
            Conformance.Equal(false, g.TryCompleteReset()); Conformance.Equal(true, g.Complete(false));
            Conformance.Equal(false, g.TryCompleteReset());
        });
        yield return Test("recognition during the second press completes exactly once", () =>
        {
            var g = new DesignerPaneResetGesture(); Click(g); g.Begin(); g.RequestReset();
            Conformance.Equal(false, g.TryCompleteReset()); Conformance.Equal(true, g.Complete(false));
            Conformance.Equal(false, g.Complete(false));
        });
        yield return Test("late native double tap after release is supported", () =>
        {
            var g = new DesignerPaneResetGesture(); Click(g); Click(g); g.RequestReset();
            Conformance.Equal(true, g.TryCompleteReset()); Conformance.Equal(false, g.TryCompleteReset());
        });
        yield return Test("a completed resize cannot become the first reset tap", () =>
        {
            var g = new DesignerPaneResetGesture(); g.Begin(); g.Move(-48, 0); g.Complete(false);
            g.RequestReset(); g.Begin(); Conformance.Equal(false, g.Complete(false));
        });
        yield return Test("keyboard sizing between presses invalidates the first tap", () =>
        {
            var g = new DesignerPaneResetGesture(); Click(g); g.Interrupt(); g.RequestReset();
            g.Begin(); g.Move(-32, 0); Conformance.Equal(false, g.Complete(true));
            Conformance.Equal(false, g.TryCompleteReset());
        });
        foreach (bool cancel in new[] { false, true })
            yield return Test("movement cancels pending reset including out-and-back " + cancel, () =>
            {
                var g = new DesignerPaneResetGesture(); Click(g); g.RequestReset(); g.Begin();
                g.Move(0, 24); g.Move(0, 0); Conformance.Equal(false, g.Complete(cancel));
                g.RequestReset(); Conformance.Equal(false, g.TryCompleteReset());
            });
        yield return Test("Escape or lost capture cannot complete or resurrect a reset", () =>
        {
            var g = new DesignerPaneResetGesture(); Click(g); g.RequestReset(); g.Begin();
            g.Interrupt(); Conformance.Equal(false, g.Complete(true)); g.RequestReset();
            Conformance.Equal(false, g.TryCompleteReset()); g.Complete(false);
            Conformance.Equal(false, g.TryCompleteReset());
        });
        yield return Test("unload invalidates a queued late request", () =>
        {
            var g = new DesignerPaneResetGesture(); Click(g); Click(g); g.RequestReset();
            g.Interrupt(); Conformance.Equal(false, g.TryCompleteReset());
        });
        yield return Test("small pointer jitter retains an actual double tap", () =>
        {
            var g = new DesignerPaneResetGesture(); Click(g); g.Begin(); g.Move(2, -2); g.RequestReset();
            Conformance.Equal(true, g.Complete(false));
        });
        yield return Test("no request no reset and invalid input is rejected", () =>
        {
            var g = new DesignerPaneResetGesture(); g.RequestReset(); Click(g); Click(g);
            Conformance.Equal(false, g.TryCompleteReset()); g.Begin();
            Conformance.Throws<InvalidOperationException>(g.Begin);
            Conformance.Throws<ArgumentOutOfRangeException>(() => g.Move(double.NaN, 0));
            Conformance.Equal(false, g.Complete(true));
        });
    }
}
