using ReportScene.Controls.Uno;
using ReportScene.Designer;

namespace ReportScene.UnoPackageTests;

/// <summary>Compile only against the generated nupkgs; native input is exercised separately by the application browser suite.</summary>
public static class ColorEditorConsumer
{
    public static ReportColorEditor Create()
    {
        var color = DesignerColorValue.FromChannels("128", "17", "34", "51");
        var editor = new ReportColorEditor(color); editor.SetValue(DesignerColorValue.Parse("#108840"));
        editor.RestoreOriginal(); _ = editor.ReadValue(); _ = editor.Elements(); _ = editor.IsValid;
        _ = editor.HasUserChanges; _ = editor.ValueChangeCount; _ = editor.PaletteUpdateCount;
        return editor;
    }
}
