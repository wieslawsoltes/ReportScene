using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using ReportScene.Serialization;
using Windows.System;

namespace ReportScene.Designer.Uno;

public sealed partial class ReportDesignerControl
{
    private void Run(Action action) { if (disposed) return; try { action(); } catch (Exception e) { ShowError(e); } }
    private async void Run(Func<Task> action) { if (disposed) return; try { await action(); } catch (Exception e) { if (!disposed) ShowError(e); } }
    private void NotifyState()
    {
        if (disposed) return;
        ObservePreferences(); StateChanged?.Invoke();
    }
    private void HandleShortcuts(object sender, KeyRoutedEventArgs e)
    {
        if (disposed) return;
        bool ctrl = (Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control) & Windows.UI.Core.CoreVirtualKeyStates.Down) != 0;
        if (e.Key == VirtualKey.F5) { Run(PreparePreview); e.Handled = true; return; }
        if (!ctrl) return;
        if (e.OriginalSource is TextBox && e.Key is VirtualKey.C or VirtualKey.X or VirtualKey.V or VirtualKey.A or VirtualKey.Z or VirtualKey.Y) return;
        switch (e.Key)
        {
            case VirtualKey.S: Run(SaveAsync); break; case VirtualKey.O: Run(OpenAsync); break; case VirtualKey.N: Run(NewAsync); break;
            case VirtualKey.Z: Session.Undo(); break; case VirtualKey.Y: Session.Redo(); break; case VirtualKey.C: Run(CopyAsync); break; case VirtualKey.X: Run(CutAsync); break; case VirtualKey.V: Run(PasteAsync); break; case VirtualKey.A: Session.SelectAll(); break;
            case VirtualKey.P: Run(PrintAsync); break;
            default: return;
        }
        e.Handled = true;
    }
    public async Task RestoreRecoveryAsync()
    {
        try
        {
            await Operations.ReadAndApplyAsync(host.LoadRecoveryAsync, json =>
            {
                if (string.IsNullOrWhiteSpace(json)) return;
                Session.Load(ReportJson.Deserialize(json)); SetStatus("Restored the most recent local workspace.");
            });
        }
        catch (StaleDesignerOperationException) { if (!disposed) SetStatus("Recovery was not applied because the current report changed."); }
        catch (Exception e) { if (!disposed) SetStatus("Recovery could not be loaded: " + e.Message); }
    }
    public new void Dispose()
    {
        if (disposed) return; disposed = true; recoveryTimer.Stop(); workspaceOperations?.Dispose();
        preferenceTimer.Stop(); preferences.Changed -= PreferencesChanged; preferences.Dispose();
        Session.Changed -= SessionChanged; Session.SelectionChanged -= SelectedChanged; KeyDown -= HandleShortcuts;
        DesignView.ViewChanged -= ViewChanged; PreviewView.ViewChanged -= ViewChanged;
        PreviewView.ViewChanged -= SyncPreviewNavigator; pageNavigator = null;
        searchSession = null; prepared = null; PreviewView.Report = null; commands.Clear(); objectIndices.Clear();
        DesignView.Dispose(); PreviewView.Dispose(); renderer.Dispose(); base.Dispose();
    }
}
