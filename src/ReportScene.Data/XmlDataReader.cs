using System.Globalization;
using System.Text;
using System.Xml;
using ReportScene.Model;

namespace ReportScene.Data;

public sealed record XmlDataOptions(int MaximumRows = 100_000, int MaximumColumns = 1024,
    int MaximumCharacters = 32_000_000, int MaximumCells = 1_000_000, bool InferNumbers = false,
    string? RowElement = null, string? RowNamespace = null, string AttributePrefix = "Attribute_");

/// <summary>
/// Streaming scalar XML tables: one document root containing repeated row elements. Row attributes
/// and scalar child elements become columns. Missing fields are null, empty fields remain empty.
/// DTDs, external resolution, mixed row kinds and nested objects are rejected, not silently flattened.
/// </summary>
public static class XmlDataReader
{
    private const string Xsi = "http://www.w3.org/2001/XMLSchema-instance";
    private const string Xsd = "http://www.w3.org/2001/XMLSchema";
    private const string Xmlns = "http://www.w3.org/2000/xmlns/";
    public static DataTable Read(string xml, string name = "Data", XmlDataOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(xml); options ??= new(); ValidateOptions(options, name);
        cancellationToken.ThrowIfCancellationRequested();
        if (xml.Length > options.MaximumCharacters) throw new FormatException("XML input exceeds its character limit.");
        using var input = new StringReader(xml);
        using var reader = XmlReader.Create(input, Settings(options));
        return ReadCore(reader, name, options, cancellationToken);
    }
    /// <summary>Honors the XML declaration/BOM, including UTF-16. The caller retains ownership of input.</summary>
    public static DataTable Read(Stream input, string name = "Data", XmlDataOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input); options ??= new(); ValidateOptions(options, name);
        cancellationToken.ThrowIfCancellationRequested();
        using var reader = XmlReader.Create(input, Settings(options));
        return ReadCore(reader, name, options, cancellationToken);
    }
    private static XmlReaderSettings Settings(XmlDataOptions options) => new()
    {
        DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, CloseInput = false,
        MaxCharactersInDocument = options.MaximumCharacters, MaxCharactersFromEntities = options.MaximumCharacters,
        IgnoreComments = true, IgnoreProcessingInstructions = true, ConformanceLevel = ConformanceLevel.Document
    };
    private static void ValidateOptions(XmlDataOptions options, string name)
    {
        if (options.MaximumRows < 1 || options.MaximumColumns < 1 || options.MaximumCharacters < 1 || options.MaximumCells < 1)
            throw new ArgumentOutOfRangeException(nameof(options), "XML limits must be positive.");
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("A table name is required.", nameof(name));
        if (options.AttributePrefix is null) throw new ArgumentException("An attribute prefix cannot be null.", nameof(options));
        if (options.RowElement is { } row) XmlConvert.VerifyNCName(row);
    }
    private static DataTable ReadCore(XmlReader reader, string name, XmlDataOptions options, CancellationToken cancellation)
    {
        var table = new DataTable { Name = name };
        var origins = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string? rowName = options.RowElement, rowNamespace = options.RowNamespace;
        bool root = false;
        FormatException Error(string message)
        {
            var line = reader as IXmlLineInfo;
            return new FormatException($"{message} (line {line?.LineNumber ?? 0}, position {line?.LinePosition ?? 0}).");
        }
        void Add(DataRow row, string column, string origin, Value value)
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
        Value Scalar(string text, string? type, string? typeNamespace, bool nil)
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
                    "string" => Value.From(text),
                    "boolean" => XmlConvert.ToBoolean(text.Trim()),
                    "decimal" => XmlConvert.ToDecimal(text.Trim()),
                    "byte" => XmlConvert.ToSByte(text.Trim()), "unsignedByte" => XmlConvert.ToByte(text.Trim()),
                    "short" => XmlConvert.ToInt16(text.Trim()), "unsignedShort" => XmlConvert.ToUInt16(text.Trim()),
                    "int" => XmlConvert.ToInt32(text.Trim()), "unsignedInt" => (decimal)XmlConvert.ToUInt32(text.Trim()),
                    "long" => (decimal)XmlConvert.ToInt64(text.Trim()), "unsignedLong" => (decimal)XmlConvert.ToUInt64(text.Trim()),
                    "integer" => decimal.Parse(text.Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture),
                    "dateTime" => Value.From(XmlConvert.ToDateTimeOffset(text.Trim()).ToString("O", CultureInfo.InvariantCulture)),
                    "date" => Value.From(XmlConvert.ToDateTime(text.Trim(), XmlDateTimeSerializationMode.RoundtripKind).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
                    _ => throw Error($"Unsupported XML scalar type '{type}'")
                };
            }
            catch (Exception e) when (e is FormatException or OverflowException)
            { throw Error($"Invalid '{type}' XML field: {e.Message}"); }
        }
        Value Field()
        {
            string? type = null, typeNamespace = null; bool nil = false;
            if (reader.MoveToFirstAttribute())
            {
                do
                {
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
            while (reader.Read())
            {
                cancellation.ThrowIfCancellationRequested();
                if (reader.NodeType == XmlNodeType.EndElement && reader.Depth == depth) return Scalar(text.ToString(), type, typeNamespace, nil);
                if (reader.NodeType == XmlNodeType.Element) throw Error("Nested XML data must be normalized into a related table");
                if (reader.NodeType is XmlNodeType.Text or XmlNodeType.CDATA or XmlNodeType.Whitespace or XmlNodeType.SignificantWhitespace) text.Append(reader.Value);
            }
            throw Error("Unclosed XML field");
        }
        DataRow Row()
        {
            var row = new DataRow();
            if (reader.MoveToFirstAttribute())
            {
                do
                {
                    if (reader.NamespaceURI == Xmlns || reader.Prefix == "xml") continue;
                    if (reader.NamespaceURI == Xsi) throw Error("Row-level xsi metadata is not a scalar field");
                    Add(row, options.AttributePrefix + XmlConvert.DecodeName(reader.LocalName), "@{" + reader.NamespaceURI + "}" + reader.LocalName, Scalar(reader.Value, null, null, false));
                } while (reader.MoveToNextAttribute());
                reader.MoveToElement();
            }
            if (reader.IsEmptyElement) return row;
            int depth = reader.Depth;
            while (reader.Read())
            {
                cancellation.ThrowIfCancellationRequested();
                if (reader.NodeType == XmlNodeType.EndElement && reader.Depth == depth) return row;
                if (reader.NodeType == XmlNodeType.Element)
                {
                    string column = XmlConvert.DecodeName(reader.LocalName), origin = "{" + reader.NamespaceURI + "}" + reader.LocalName;
                    Add(row, column, origin, Field());
                }
                else if (reader.NodeType is XmlNodeType.Text or XmlNodeType.CDATA or XmlNodeType.SignificantWhitespace && !string.IsNullOrWhiteSpace(reader.Value))
                    throw Error("XML rows cannot contain mixed text and fields");
            }
            throw Error("Unclosed XML row");
        }
        try
        {
            while (reader.Read())
            {
                cancellation.ThrowIfCancellationRequested();
                if (reader.NodeType == XmlNodeType.Element && reader.Depth == 0) { root = true; continue; }
                if (reader.NodeType == XmlNodeType.Element)
                {
                    if (reader.Depth != 1) throw Error("Rows must be immediate children of the document root");
                    rowName ??= reader.LocalName; rowNamespace ??= reader.NamespaceURI;
                    if (reader.LocalName != rowName || reader.NamespaceURI != rowNamespace) throw Error("All XML rows must have the configured expanded element name");
                    if (table.Rows.Count >= options.MaximumRows) throw Error("XML row limit exceeded");
                    table.Rows.Add(Row());
                    if ((long)table.Rows.Count * table.Columns.Count > options.MaximumCells) throw Error("Normalized XML cell limit exceeded");
                }
                else if (reader.NodeType is XmlNodeType.Text or XmlNodeType.CDATA or XmlNodeType.SignificantWhitespace && !string.IsNullOrWhiteSpace(reader.Value))
                    throw Error("The XML root cannot contain mixed text");
            }
            if (!root) throw Error("An XML document root is required");
            foreach (var row in table.Rows)
            { cancellation.ThrowIfCancellationRequested(); foreach (var column in table.Columns) row.TryAdd(column, Value.Null); }
            return table;
        }
        catch (XmlException e) { throw new FormatException($"Invalid XML data at line {e.LineNumber}, position {e.LinePosition}: {e.Message}", e); }
    }
}

/// <summary>A host-supplied stream factory supports files, assets or authenticated HTTP without ambient I/O.</summary>
public sealed class XmlDataProvider : IDataProvider
{
    private readonly Func<CancellationToken, ValueTask<Stream>> open;
    private readonly XmlDataOptions options;
    public string Name { get; }
    public XmlDataProvider(string name, Func<CancellationToken, ValueTask<Stream>> open, XmlDataOptions? options = null)
    { ArgumentException.ThrowIfNullOrWhiteSpace(name); ArgumentNullException.ThrowIfNull(open); Name = name; this.open = open; this.options = options ?? new(); }
    public async ValueTask<DataTable> ReadAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await using var stream = await open(cancellationToken).ConfigureAwait(false) ?? throw new InvalidOperationException("XML stream factory returned null.");
        return XmlDataReader.Read(stream, Name, options, cancellationToken);
    }
}
