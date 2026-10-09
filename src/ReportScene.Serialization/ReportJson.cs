using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using ReportScene.Model;

namespace ReportScene.Serialization;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true, UseStringEnumConverter = true, MaxDepth = 96, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(ReportDocument))]
[JsonSerializable(typeof(ReportPage))]
[JsonSerializable(typeof(Band))]
[JsonSerializable(typeof(GroupDefinition))]
[JsonSerializable(typeof(ItemStyle))]
[JsonSerializable(typeof(List<ReportItem>))]
[JsonSerializable(typeof(List<Diagnostic>))]
[JsonSerializable(typeof(DataTable))]
[JsonSerializable(typeof(PreparedReport))]
public partial class ReportJsonContext : JsonSerializerContext;

public static class ReportJson
{
    public const int MaximumCharacters = 64_000_000;
    private static readonly ReportJsonContext Context = ReportJsonContext.Default;
    public static string Serialize(ReportDocument report) => JsonSerializer.Serialize(report, Context.ReportDocument);
    public static ReportDocument Deserialize(string json)
    {
        if (json.Length > MaximumCharacters) throw new FormatException("Report exceeds the 64-million-character input limit.");
        try
        {
            var root = Parse(json) as JsonObject ?? throw new FormatException("A report must be a JSON object.");
            if (root["formatVersion"] is { } version && version.GetValue<int>() != 1) throw new FormatException("Unsupported report schema version.");
            Defaults(root, new ReportDocument(), Context.ReportDocument);
            foreach (var node in Array(root, "pages"))
            {
                var page = Object(node, "page"); var defaults = new ReportPage(); Defaults(page, defaults, Context.ReportPage);
                DefaultsObject(page, "margins", JsonSerializer.SerializeToNode(defaults, Context.ReportPage)!["margins"]!.AsObject());
                foreach (var band in Array(page, "bands")) NormalizeBand(Object(band, "band"), 0);
            }
            foreach (var node in Array(root, "data"))
            {
                var table = Object(node, "data table"); Defaults(table, new DataTable(), Context.DataTable);
                foreach (var row in Array(table, "rows")) _ = Object(row, "data row");
                foreach (var column in Array(table, "columns")) if (column is null) throw new FormatException("A data column name cannot be null.");
            }
            foreach (var pair in Object(root["styles"], "styles")) NormalizeStyle(Object(pair.Value, "style"));
            var report = root.Deserialize(Context.ReportDocument) ?? throw new FormatException("Empty report document.");
            var errors = DocumentValidator.Validate(report);
            if (errors.Any(e => e.Severity == DiagnosticSeverity.Error)) throw new FormatException(string.Join("\n", errors.Select(e => e.Message)));
            return report;
        }
        catch (JsonException e) { throw new FormatException("Invalid report JSON: " + e.Message, e); }
        catch (InvalidOperationException e) { throw new FormatException("Invalid report value: " + e.Message, e); }
    }
    public static ReportDocument Clone(ReportDocument report) => Deserialize(Serialize(report));
    public static string SerializeItems(IEnumerable<ReportItem> items) => JsonSerializer.Serialize(items.ToList(), Context.ListReportItem);
    public static List<ReportItem> DeserializeItems(string json)
    {
        if (json.Length > 16_000_000) throw new FormatException("Clipboard report objects exceed 16 MB.");
        try
        {
            var array = Parse(json) as JsonArray ?? throw new FormatException("Report clipboard data must be an array.");
            foreach (var node in array) NormalizeItem(Object(node, "clipboard object"));
            return array.Deserialize(Context.ListReportItem) ?? [];
        }
        catch (JsonException e) { throw new FormatException("Invalid report clipboard: " + e.Message, e); }
    }
    private static JsonNode? Parse(string json) => JsonNode.Parse(json, documentOptions: new JsonDocumentOptions { MaxDepth = 96 });
    private static JsonObject Object(JsonNode? node, string name) => node as JsonObject ?? throw new FormatException($"The {name} must be a non-null object.");
    private static JsonArray Array(JsonObject parent, string name) => parent[name] as JsonArray ?? throw new FormatException($"'{name}' must be a non-null array.");
    private static void Defaults<T>(JsonObject node, T value, JsonTypeInfo<T> info)
        => Merge(node, JsonSerializer.SerializeToNode(value, info)!.AsObject());
    private static void Merge(JsonObject node, JsonObject defaults)
    {
        // Source-generated deserialization of init-only members can overwrite property
        // initializers with default(T) for missing JSON properties (dotnet/runtime#84484).
        // Populate the versioned wire schema explicitly, retaining immutable model records
        // and reflection-free WebAssembly serialization. Explicit null is not omission.
        foreach (var pair in defaults)
        {
            if (!node.ContainsKey(pair.Key)) node.Add(pair.Key, pair.Value?.DeepClone());
            else if (node[pair.Key] is null && pair.Value is not null) throw new FormatException($"'{pair.Key}' cannot be null.");
        }
    }
    private static void DefaultsObject(JsonObject node, string property, JsonObject defaults) => Merge(Object(node[property], property), defaults);
    private static void NormalizeStyle(JsonObject style) => Defaults(style, new ItemStyle(), Context.ItemStyle);
    private static void NormalizeBand(JsonObject band, int depth)
    {
        if (depth > 32) throw new FormatException("Band nesting exceeds 32 levels.");
        Defaults(band, new Band(), Context.Band);
        foreach (var item in Array(band, "items")) NormalizeItem(Object(item, "report object"));
        foreach (var child in Array(band, "children")) NormalizeBand(Object(child, "child band"), depth + 1);
        if (band["header"] is { } header) NormalizeBand(Object(header, "header band"), depth + 1);
        if (band["footer"] is { } footer) NormalizeBand(Object(footer, "footer band"), depth + 1);
        foreach (var node in Array(band, "groups"))
        {
            var group = Object(node, "group"); Defaults(group, new GroupDefinition(), Context.GroupDefinition);
            NormalizeBand(Object(group["header"], "group header"), depth + 1); NormalizeBand(Object(group["footer"], "group footer"), depth + 1);
        }
    }
    private static void NormalizeItem(JsonObject item)
    {
        string type = item["$type"]?.GetValue<string>() ?? throw new FormatException("Every report object requires a '$type' discriminator.");
        ReportItem defaults = type switch
        {
            "text" => new TextItem(), "shape" => new ShapeItem(), "image" => new ImageItem(), "checkbox" => new CheckBoxItem(),
            "barcode" => new BarcodeItem(), "chart" => new ChartItem(), "table" => new TableItem(),
            _ => throw new FormatException($"Unknown report object type '{type}'.")
        };
        var defaultNode = JsonSerializer.SerializeToNode(new List<ReportItem> { defaults }, Context.ListReportItem)![0]!.AsObject();
        Merge(item, defaultNode);
        DefaultsObject(item, "bounds", defaultNode["bounds"]!.AsObject());
        NormalizeStyle(Object(item["style"], "object style"));
    }
}
