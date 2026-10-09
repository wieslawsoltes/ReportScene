using System.Text.Json.Serialization;

namespace ReportScene.App;

public sealed record BridgeResult(string? Name = null, string? Data = null, string? Text = null, string? Error = null, bool Cancelled = false);
[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(BridgeResult))]
[JsonSerializable(typeof(string))]
public partial class AppJsonContext : System.Text.Json.Serialization.JsonSerializerContext;
