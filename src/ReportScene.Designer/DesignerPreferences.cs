using System.Text.Json;
using System.Text.Json.Serialization;

namespace ReportScene.Designer;

public enum DesignerExplorerTab { Data, Report }

/// <summary>Application preferences only; never includes report content, file names, credentials or history.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record DesignerPreferences
{
    [JsonRequired]
    public int Version { get; init; } = 1;
    public double SidebarWidth { get; init; } = 280;
    public double TreeHeight { get; init; } = 210;
    public bool ShowTree { get; init; } = true;
    public bool ShowProperties { get; init; } = true;
    public bool ShowGrid { get; init; } = true;
    public bool ShowRulers { get; init; } = true;
    public bool ShowFullPaper { get; init; }
    public bool SnapToGrid { get; init; } = true;
    public double GridSize { get; init; } = 4;
    public DesignerUnit Unit { get; init; } = DesignerUnit.Points;
    public bool AlphabeticalProperties { get; init; }
    public DesignerExplorerTab ExplorerTab { get; init; } = DesignerExplorerTab.Data;
    public double DesignZoom { get; init; } = .85;
    public double PreviewZoom { get; init; } = 1;

    public DesignerPreferences Validate()
    {
        if (Version != 1) throw new ArgumentException("This designer preference version is not supported.", nameof(Version));
        Range(SidebarWidth, 180, 640, nameof(SidebarWidth));
        Range(TreeHeight, 96, 480, nameof(TreeHeight));
        Range(GridSize, 1, 72, nameof(GridSize));
        Range(DesignZoom, .15, 4, nameof(DesignZoom));
        Range(PreviewZoom, .1, 4, nameof(PreviewZoom));
        if (!Enum.IsDefined(Unit)) throw new ArgumentOutOfRangeException(nameof(Unit));
        if (!Enum.IsDefined(ExplorerTab)) throw new ArgumentOutOfRangeException(nameof(ExplorerTab));
        return this;
    }
    private static void Range(double value, double minimum, double maximum, string name)
    {
        if (!double.IsFinite(value) || value < minimum || value > maximum)
            throw new ArgumentOutOfRangeException(name, $"{name} must be finite and between {minimum} and {maximum}.");
    }
}

/// <summary>Bounded, strict, reflection-free preference serialization for desktop and WebAssembly hosts.</summary>
public static class DesignerPreferencesJson
{
    public const int MaximumCharacters = 8192;
    public static string Serialize(DesignerPreferences value)
    {
        ArgumentNullException.ThrowIfNull(value); value.Validate();
        string json = JsonSerializer.Serialize(value, DesignerPreferencesJsonContext.Default.DesignerPreferences);
        if (json.Length > MaximumCharacters) throw new ArgumentException("Designer preferences exceed the storage limit.", nameof(value));
        return json;
    }
    public static DesignerPreferences Deserialize(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        if (json.Length == 0 || json.Length > MaximumCharacters) throw new FormatException("Designer preference storage has an invalid size.");
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 8 });
            if (document.RootElement.ValueKind != JsonValueKind.Object) throw new JsonException("Preferences must be an object.");
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in document.RootElement.EnumerateObject())
                if (!names.Add(property.Name)) throw new JsonException("Duplicate preference field: " + property.Name);
            var value = JsonSerializer.Deserialize(json, DesignerPreferencesJsonContext.Default.DesignerPreferences)
                ?? throw new JsonException("Preferences must not be null.");
            return value.Validate();
        }
        catch (Exception error) when (error is JsonException or ArgumentException)
        { throw new FormatException("The stored designer preferences are invalid: " + error.Message, error); }
    }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, UseStringEnumConverter = true)]
[JsonSerializable(typeof(DesignerPreferences))]
internal partial class DesignerPreferencesJsonContext : JsonSerializerContext;
