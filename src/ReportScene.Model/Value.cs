using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ReportScene.Model;

public enum ValueKind { Null, Number, String, Boolean }

/// <summary>A culture-independent scalar. Money uses decimal, never binary floating-point.</summary>
[JsonConverter(typeof(ValueJsonConverter))]
public readonly record struct Value
{
    public ValueKind Kind { get; }
    public decimal Number { get; }
    public string? Text { get; }
    public bool Boolean { get; }
    private Value(ValueKind kind, decimal number = 0, string? text = null, bool boolean = false)
        => (Kind, Number, Text, Boolean) = (kind, number, text, boolean);
    public static Value Null => default;
    public static Value From(decimal n) => new(ValueKind.Number, number: n);
    public static Value From(string? s) => s is null ? Null : new(ValueKind.String, text: s);
    public static Value From(bool b) => new(ValueKind.Boolean, boolean: b);
    public static Value From(DateTime d) => From(d.ToString("O", CultureInfo.InvariantCulture));
    public bool IsNull => Kind == ValueKind.Null;
    public decimal AsNumber() => Kind switch
    {
        ValueKind.Number => Number,
        ValueKind.Boolean => Boolean ? 1 : 0,
        ValueKind.String when decimal.TryParse(Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) => n,
        _ => throw new FormatException($"'{AsString()}' is not a number.")
    };
    public bool AsBoolean() => Kind switch
    {
        ValueKind.Null => false,
        ValueKind.Boolean => Boolean,
        ValueKind.Number => Number != 0,
        ValueKind.String when bool.TryParse(Text, out var b) => b,
        _ => !string.IsNullOrEmpty(Text)
    };
    public string AsString(string? format = null, CultureInfo? culture = null)
    {
        culture ??= CultureInfo.InvariantCulture;
        return Kind switch
        {
            ValueKind.Number => Number.ToString(format, culture),
            ValueKind.Boolean => Boolean ? "True" : "False",
            ValueKind.String => Text ?? "",
            _ => ""
        };
    }
    public DateTime AsDate() => DateTime.Parse(AsString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
    public override string ToString() => AsString();
    public static int Compare(Value a, Value b)
    {
        if (a.IsNull || b.IsNull) return a.IsNull == b.IsNull ? 0 : a.IsNull ? -1 : 1;
        if (a.Kind == ValueKind.Number || b.Kind == ValueKind.Number) return a.AsNumber().CompareTo(b.AsNumber());
        if (a.Kind == ValueKind.Boolean && b.Kind == ValueKind.Boolean) return a.Boolean.CompareTo(b.Boolean);
        return StringComparer.Ordinal.Compare(a.AsString(), b.AsString());
    }
    public static implicit operator Value(string v) => From(v);
    public static implicit operator Value(int v) => From((decimal)v);
    public static implicit operator Value(decimal v) => From(v);
    public static implicit operator Value(bool v) => From(v);
}

public sealed class ValueJsonConverter : JsonConverter<Value>
{
    public override Value Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => reader.TokenType switch
    {
        JsonTokenType.Null => Value.Null,
        JsonTokenType.Number => Value.From(reader.GetDecimal()),
        JsonTokenType.String => Value.From(reader.GetString()),
        JsonTokenType.True => Value.From(true),
        JsonTokenType.False => Value.From(false),
        _ => throw new JsonException("Only scalar report values are supported.")
    };
    public override void Write(Utf8JsonWriter writer, Value value, JsonSerializerOptions options)
    {
        switch (value.Kind)
        {
            case ValueKind.Null: writer.WriteNullValue(); break;
            case ValueKind.Number: writer.WriteNumberValue(value.Number); break;
            case ValueKind.Boolean: writer.WriteBooleanValue(value.Boolean); break;
            default: writer.WriteStringValue(value.Text); break;
        }
    }
}

public sealed class DataRow : Dictionary<string, Value>
{
    public DataRow() : base(StringComparer.OrdinalIgnoreCase) { }
    public DataRow(IEnumerable<KeyValuePair<string, Value>> values) : this()
    { foreach (var (key, value) in values) Add(key, value); }
}

public sealed record DataTable
{
    public string Name { get; init; } = "Data";
    public List<string> Columns { get; init; } = [];
    public List<DataRow> Rows { get; init; } = [];
}

public sealed record DataRelation(string ParentTable, string ParentColumn, string ChildTable, string ChildColumn);
public sealed record ReportParameter(string Name, Value DefaultValue, string Description = "", bool Required = false);
