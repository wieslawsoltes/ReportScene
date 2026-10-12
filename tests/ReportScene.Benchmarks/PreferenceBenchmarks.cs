using System.Diagnostics;
using ReportScene.Designer;

internal static class PreferenceBenchmarks
{
    private sealed class Store : IDesignerPreferenceStore
    {
        public int Writes;
        public Task<string?> LoadAsync(CancellationToken token = default) => Task.FromResult<string?>(null);
        public Task SaveAsync(string json, CancellationToken token = default) { Writes++; return Task.CompletedTask; }
    }
    public static IEnumerable<object> Run()
    {
        var preferences = new DesignerPreferences { SidebarWidth = 560, TreeHeight = 420 };
        string before = DesignerPreferencesJson.Serialize(preferences);
        var sizes = new[] { (1600d, 1000d), (700d, 620d), (440d, 600d), (1000d, 240d) };
        var layout = Measure(() =>
        {
            int checksum = 0;
            for (int n = 0; n < 10000; n++)
            { var size = sizes[n % sizes.Length]; var actual = DesignerPaneLayout.Calculate(preferences, size.Item1, size.Item2); checksum += (int)actual.SidebarWidth; }
            return checksum;
        });
        if (DesignerPreferencesJson.Serialize(preferences) != before) throw new InvalidOperationException("Viewport calculation changed preferred settings.");
        var store = new Store(); using var session = new DesignerPreferenceSession(store); int events = 0;
        session.Changed += () => events++;
        var unchanged = Measure(() => { for (int n = 0; n < 10000; n++) session.Update(session.Current); return (int)session.Generation; });
        if (events != 0 || session.HasPending || store.Writes != 0) throw new InvalidOperationException("Unchanged preferences queued persistence.");
        for (int n = 0; n < 1000; n++) session.Update(session.Current with { SidebarWidth = 300 + n % 200 });
        session.FlushAsync().GetAwaiter().GetResult();
        if (store.Writes != 1 || session.HasPending) throw new InvalidOperationException("Pending settings were not coalesced.");
        yield return new
        {
            name = "preference-layout-and-coalescing", viewportCalculations = 10000, layout,
            unchangedUpdatesPerRun = 10000, unchanged, unchangedNotificationCount = 0,
            queuedChangedValues = 1000, acknowledgedWrites = store.Writes,
            note = "Current pure layout and headless coordinator only. No comparison with an old persistence implementation is claimed. File/browser storage latency, native pane layout, rendering and frame rate are excluded. Writes use a counting store, not physical IO."
        };
    }
    private static Measurement Measure(Func<int> action)
    {
        _ = action(); var measurements = new List<Measurement>();
        for (int n = 0; n < 3; n++)
        {
            long allocation = GC.GetAllocatedBytesForCurrentThread(), start = Stopwatch.GetTimestamp(); int checksum = action();
            measurements.Add(new(Stopwatch.GetElapsedTime(start).TotalMilliseconds, GC.GetAllocatedBytesForCurrentThread() - allocation, checksum));
        }
        return measurements.OrderBy(m => m.Milliseconds).ElementAt(1);
    }
}
