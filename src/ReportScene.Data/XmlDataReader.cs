using System.Xml;
using ReportScene.Model;

namespace ReportScene.Data;

public sealed record XmlDataOptions(int MaximumRows = 100_000, int MaximumColumns = 1024,
    int MaximumCharacters = 32_000_000, int MaximumCells = 1_000_000, bool InferNumbers = false,
    string? RowElement = null, string? RowNamespace = null, string AttributePrefix = "Attribute_");

/// <summary>Bounded scalar XML tables. DTDs, external resolution, nested objects and ambiguous columns are rejected.</summary>
public static class XmlDataReader
{
    public static DataTable Read(string xml, string name = "Data", XmlDataOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(xml); options ??= new(); ValidateOptions(options, name);
        cancellationToken.ThrowIfCancellationRequested();
        if (xml.Length > options.MaximumCharacters) throw new FormatException("XML input exceeds its character limit.");
        using var input = new StringReader(xml); using var reader = XmlReader.Create(input, Settings(options));
        return Parse(reader, name, options, cancellationToken);
    }
    /// <summary>Honors XML declaration/BOM encoding. The caller retains ownership of input.</summary>
    public static DataTable Read(Stream input, string name = "Data", XmlDataOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input); options ??= new(); ValidateOptions(options, name);
        cancellationToken.ThrowIfCancellationRequested(); using var reader = XmlReader.Create(input, Settings(options));
        return Parse(reader, name, options, cancellationToken);
    }
    private static DataTable Parse(XmlReader reader, string name, XmlDataOptions options, CancellationToken cancellation)
    {
        try { return new XmlTableParser(reader, name, options, cancellation).Read(); }
        catch (FormatException error) when (error.InnerException is XmlException xml && xml.Message.StartsWith("Xml_DtdIsProhibited", StringComparison.Ordinal))
        {
            // Trimmed WebAssembly BCL builds may expose a resource key rather than its localized
            // message. Keep the parser's prohibition intact, but show actionable application text.
            throw new FormatException("DTD declarations are prohibited. Remove the document type and entity definitions from the XML data file.", error);
        }
    }
    private static XmlReaderSettings Settings(XmlDataOptions options) => new()
    {
        DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, CloseInput = false,
        MaxCharactersInDocument = options.MaximumCharacters, MaxCharactersFromEntities = options.MaximumCharacters,
        IgnoreComments = true, IgnoreProcessingInstructions = true, ConformanceLevel = ConformanceLevel.Document
    };
    internal static void ValidateOptions(XmlDataOptions options, string name)
    {
        if (options.MaximumRows < 1 || options.MaximumColumns < 1 || options.MaximumCharacters < 1 || options.MaximumCells < 1)
            throw new ArgumentOutOfRangeException(nameof(options), "XML limits must be positive.");
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (options.AttributePrefix is null) throw new ArgumentException("An attribute prefix cannot be null.", nameof(options));
        if (options.RowElement is { } row) XmlConvert.VerifyNCName(row);
    }
}

/// <summary>Host-supplied stream factory; asynchronous I/O is bounded and does not require synchronous reads from network streams.</summary>
public sealed class XmlDataProvider : IDataProvider
{
    private readonly Func<CancellationToken, ValueTask<Stream>> open;
    private readonly XmlDataOptions options;
    public string Name { get; }
    public XmlDataProvider(string name, Func<CancellationToken, ValueTask<Stream>> open, XmlDataOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(open); this.options = options ?? new(); XmlDataReader.ValidateOptions(this.options, name);
        Name = name; this.open = open;
    }
    public async ValueTask<DataTable> ReadAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await using var stream = await open(cancellationToken).ConfigureAwait(false) ?? throw new InvalidOperationException("XML stream factory returned null.");
        using var content = new MemoryStream(); byte[] buffer = new byte[16_384];
        long maximumBytes = 4L * options.MaximumCharacters + 1024;
        while (true)
        {
            int count = await stream.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested(); if (count == 0) break;
            if (content.Length + count > maximumBytes) throw new FormatException("Encoded XML stream exceeds its byte limit.");
            content.Write(buffer, 0, count);
        }
        content.Position = 0; return XmlDataReader.Read(content, Name, options, cancellationToken);
    }
}
