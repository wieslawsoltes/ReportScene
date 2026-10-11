using System.Globalization;
using System.Text;
using System.Xml;
using ReportScene.Model;

namespace ReportScene.Data;

internal sealed class XmlTableParser(XmlReader reader, string name, XmlDataOptions options, CancellationToken cancellation)
{
    private const string Xsi = "http://www.w3.org/2001/XMLSchema-instance", Xsd = "http://www.w3.org/2001/XMLSchema", Xmlns = "http://www.w3.org/2000/xmlns/";
    private readonly DataTable table = new() { Name = name };
    private readonly Dictionary<string, string> origins = new(StringComparer.OrdinalIgnoreCase);
    private bool Move() { cancellation.ThrowIfCancellationRequested(); bool result = reader.Read(); cancellation.ThrowIfCancellationRequested(); return result; }
    private FormatException Error(string message)
    {
        var line = reader as IXmlLineInfo;
        return new($"{message} (line {line?.LineNumber ?? 0}, position {line?.LinePosition ?? 0}).");
    }
    public DataTable Read()
    {
        string? rowName = options.RowElement, rowNamespace = options.RowNamespace; bool root = false;
        try
        {
            while (Move())
            {
                if (reader.NodeType == XmlNodeType.Element && reader.Depth == 0) { root = true; continue; }
                if (reader.NodeType == XmlNodeType.Element)
                {
                    if (reader.Depth != 1) throw Error("Rows must be immediate children of the document root");
                    rowName ??= reader.LocalName; rowNamespace ??= reader.NamespaceURI;
                    if (reader.LocalName != rowName || reader.NamespaceURI != rowNamespace) throw Error("All XML rows must have the configured expanded element name");
                    if (table.Rows.Count >= options.MaximumRows) throw Error("XML row limit exceeded");
                    table.Rows.Add(ReadRow());
                    if ((long)table.Rows.Count * table.Columns.Count > options.MaximumCells) throw Error("Normalized XML cell limit exceeded");
                }
                else if (IsText() && !string.IsNullOrWhiteSpace(reader.Value)) throw Error("The XML root cannot contain mixed text");
            }
            if (!root) throw Error("An XML document root is required");
            foreach (var row in table.Rows)
            { cancellation.ThrowIfCancellationRequested(); foreach (var column in table.Columns) row.TryAdd(column, Value.Null); }
            return table;
        }
        catch (XmlException e) { throw new FormatException($"Invalid XML data at line {e.LineNumber}, position {e.LinePosition}: {e.Message}", e); }
    }
    private bool IsText() => reader.NodeType is XmlNodeType.Text or XmlNodeType.CDATA or XmlNodeType.Whitespace or XmlNodeType.SignificantWhitespace;
    private void Add(DataRow row, string column, string origin, Value value)
    {
        if (string.IsNullOrWhiteSpace(column)) throw Error("XML column names cannot be empty");
        if (!row.TryAdd(column, value)) throw Error($"Duplicate XML column '{column}'");
        if (origins.TryGetValue(column, out var previous))
        { if (previous != origin) throw Error($"XML names/namespaces collide in column '{column}'"); }
        else
        {
            if (origins.Count >= options.MaximumColumns) throw Error("XML column limit exceeded");
            origins.Add(column, origin); table.Columns.Add(column);
        }
        if ((long)(table.Rows.Count + 1) * table.Columns.Count > options.MaximumCells) throw Error("Normalized XML cell limit exceeded");
    }
    private DataRow ReadRow()
    {
        var row = new DataRow();
        if (reader.MoveToFirstAttribute())
        {
            do
            {
                cancellation.ThrowIfCancellationRequested();
                if (reader.NamespaceURI == Xmlns || reader.Prefix == "xml") continue;
                if (reader.NamespaceURI == Xsi) throw Error("Row-level xsi metadata is not a scalar field");
                Add(row, options.AttributePrefix + XmlConvert.DecodeName(reader.LocalName), "@{" + reader.NamespaceURI + "}" + reader.LocalName, Scalar(reader.Value, null, null, false));
            } while (reader.MoveToNextAttribute());
            reader.MoveToElement();
        }
        if (reader.IsEmptyElement) return row;
        int depth = reader.Depth;
        while (Move())
        {
            if (reader.NodeType == XmlNodeType.EndElement && reader.Depth == depth) return row;
            if (reader.NodeType == XmlNodeType.Element)
            {
                string column = XmlConvert.DecodeName(reader.LocalName), origin = "{" + reader.NamespaceURI + "}" + reader.LocalName;
                Add(row, column, origin, ReadField());
            }
            else if (IsText() && !string.IsNullOrWhiteSpace(reader.Value)) throw Error("XML rows cannot contain mixed text and fields");
        }
        throw Error("Unclosed XML row");
    }
    private Value ReadField()
    {
        string? type = null, typeNamespace = null; bool nil = false;
        if (reader.MoveToFirstAttribute())
        {
            do
            {
                cancellation.ThrowIfCancellationRequested();
                if (reader.NamespaceURI == Xmlns || reader.Prefix == "xml") continue;
                if (reader.NamespaceURI != Xsi || reader.LocalName is not ("nil" or "type")) throw Error("Scalar XML fields cannot contain business attributes; normalize them into columns");
                if (reader.LocalName == "nil") nil = XmlConvert.ToBoolean(reader.Value.Trim());
                else
                {
                    string value = reader.Value.Trim(); int colon = value.IndexOf(':');
                    string prefix = colon < 0 ? "" : value[..colon]; type = colon < 0 ? value : value[(colon + 1)..];
                    XmlConvert.VerifyNCName(type); typeNamespace = reader.LookupNamespace(prefix);
                }
            } while (reader.MoveToNextAttribute());
            reader.MoveToElement();
        }
        if (reader.IsEmptyElement) return Scalar("", type, typeNamespace, nil);
        int depth = reader.Depth; var text = new StringBuilder();
        while (Move())
        {
            if (reader.NodeType == XmlNodeType.EndElement && reader.Depth == depth) return Scalar(text.ToString(), type, typeNamespace, nil);
            if (reader.NodeType == XmlNodeType.Element) throw Error("Nested XML data must be normalized into a related table");
            if (IsText()) text.Append(reader.Value);
        }
        throw Error("Unclosed XML field");
    }
    private Value Scalar(string text, string? type, string? typeNamespace, bool nil)
    {
        if (nil)
        {
            if (text.Length > 0) throw Error("An xsi:nil field cannot contain text");
            return Value.Null;
        }
        if (type is null) return options.InferNumbers && decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) ? n : Value.From(text);
        if (typeNamespace != Xsd) throw Error("Only XML Schema built-in scalar types are supported");
        try
        {
            return type switch
            {
                "string" => Value.From(text), "boolean" => XmlConvert.ToBoolean(text.Trim()),
                "decimal" => ExactDecimal(text.Trim()),
                "byte" => XmlConvert.ToSByte(text.Trim()), "unsignedByte" => XmlConvert.ToByte(text.Trim()),
                "short" => XmlConvert.ToInt16(text.Trim()), "unsignedShort" => XmlConvert.ToUInt16(text.Trim()),
                "int" => XmlConvert.ToInt32(text.Trim()), "unsignedInt" => (decimal)XmlConvert.ToUInt32(text.Trim()),
                "long" => (decimal)XmlConvert.ToInt64(text.Trim()), "unsignedLong" => (decimal)XmlConvert.ToUInt64(text.Trim()),
                "integer" => decimal.Parse(text.Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture),
                "float" => Floating(text.Trim(), true), "double" => Floating(text.Trim(), false),
                "dateTime" => Date(text.Trim(), true), "date" => Date(text.Trim(), false),
                _ => throw Error($"Unsupported XML scalar type '{type}'")
            };
        }
        catch (Exception e) when (e is FormatException or OverflowException)
        { throw Error($"Invalid '{type}' XML field: {e.Message}"); }
    }
    private static decimal ExactDecimal(string text)
    {
        decimal value = XmlConvert.ToDecimal(text);
        static (string Digits, int Scale) Canonical(string input)
        {
            int dot = input.IndexOf('.');
            string digits = input.TrimStart('+', '-').Replace(".", "", StringComparison.Ordinal).TrimStart('0');
            int scale = dot < 0 ? 0 : input.Length - dot - 1, length = digits.Length;
            // Use indices rather than successively allocating substrings for long zero suffixes.
            while (scale > 0 && length > 0 && digits[length - 1] == '0') { length--; scale--; }
            return length == 0 ? ("", 0) : (digits[..length], scale);
        }
        if (Canonical(text) != Canonical(value.ToString(CultureInfo.InvariantCulture)))
            throw new FormatException("Decimal precision exceeds the report model; rounding is not permitted for typed XML decimals.");
        return value;
    }
    private static Value Floating(string text, bool single)
    {
        double parsed = single ? XmlConvert.ToSingle(text) : XmlConvert.ToDouble(text);
        if (!double.IsFinite(parsed) || !decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) || (single ? (float)value != (float)parsed : (double)value != parsed))
            throw new FormatException("Floating-point value cannot round-trip through the decimal report model.");
        return value;
    }
    private static Value Date(string text, bool withTime)
    {
        if (text.Length < 10 || text[4] != '-' || text[7] != '-' || (withTime ? text.Length < 19 || text[10] != 'T' : text.Contains('T')))
            throw new FormatException("Expected an XML Schema date/dateTime value.");
        _ = XmlConvert.ToDateTime(text, XmlDateTimeSerializationMode.RoundtripKind);
        // Preserve supplied offsets, fractional precision and the absence of a time zone.
        return text;
    }
}
