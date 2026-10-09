using System.Text.Json;
using System.Text.Json.Serialization;
using ReportScene.Model;

namespace ReportScene.Serialization;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true, UseStringEnumConverter = true, MaxDepth = 96)]
[JsonSerializable(typeof(ReportDocument))]
[JsonSerializable(typeof(List<ReportItem>))]
[JsonSerializable(typeof(List<Diagnostic>))]
[JsonSerializable(typeof(DataTable))]
[JsonSerializable(typeof(PreparedReport))]
public partial class ReportJsonContext : JsonSerializerContext;

public static class ReportJson
{
    public const int MaximumCharacters = 64_000_000;
    public static string Serialize(ReportDocument report) => JsonSerializer.Serialize(report, ReportJsonContext.Default.ReportDocument);
    public static ReportDocument Deserialize(string json)
    {
        if (json.Length > MaximumCharacters) throw new FormatException("Report exceeds the 64-million-character input limit.");
        var report = JsonSerializer.Deserialize(json, ReportJsonContext.Default.ReportDocument) ?? throw new FormatException("Empty report document.");
        var errors = DocumentValidator.Validate(report);
        if (errors.Any(e => e.Severity == DiagnosticSeverity.Error)) throw new FormatException(string.Join("\n", errors.Select(e => e.Message)));
        return report;
    }
    public static ReportDocument Clone(ReportDocument report) => Deserialize(Serialize(report));
    public static string SerializeItems(IEnumerable<ReportItem> items) => JsonSerializer.Serialize(items.ToList(), ReportJsonContext.Default.ListReportItem);
    public static List<ReportItem> DeserializeItems(string json)
    {
        if (json.Length > 16_000_000) throw new FormatException("Clipboard report objects exceed 16 MB.");
        return JsonSerializer.Deserialize(json, ReportJsonContext.Default.ListReportItem) ?? [];
    }
}
