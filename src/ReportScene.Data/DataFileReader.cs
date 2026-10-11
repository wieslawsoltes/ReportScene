using System.Text;
using ReportScene.Model;

namespace ReportScene.Data;

/// <summary>Shared file-format dispatch for designer import and headless hosts; never reads a path or URL.</summary>
public static class DataFileReader
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    public static DataTable Read(string fileName, byte[] content, string tableName,
        bool inferNumbers = false, DataReadOptions? limits = null, XmlDataOptions? xmlOptions = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content); ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentException.ThrowIfNullOrWhiteSpace(tableName); cancellationToken.ThrowIfCancellationRequested(); limits ??= new();
        if (limits.MaximumRows < 1 || limits.MaximumColumns < 1 || limits.MaximumCharacters < 1) throw new ArgumentOutOfRangeException(nameof(limits));
        if (content.LongLength > 4L * limits.MaximumCharacters + 1024) throw new FormatException("Data file exceeds its encoded-byte limit.");
        string extension = Path.GetExtension(fileName).ToLowerInvariant();
        if (extension == ".xml")
        {
            var options = (xmlOptions ?? new()) with
            {
                MaximumRows = Math.Min(limits.MaximumRows, xmlOptions?.MaximumRows ?? limits.MaximumRows),
                MaximumColumns = Math.Min(limits.MaximumColumns, xmlOptions?.MaximumColumns ?? limits.MaximumColumns),
                MaximumCharacters = Math.Min(limits.MaximumCharacters, xmlOptions?.MaximumCharacters ?? limits.MaximumCharacters),
                InferNumbers = inferNumbers
            };
            using var stream = new MemoryStream(content, writable: false);
            return XmlDataReader.Read(stream, tableName, options, cancellationToken);
        }
        if (extension is not (".csv" or ".json")) throw new NotSupportedException("Supported data files are CSV, JSON and scalar XML tables.");
        string text;
        try { text = StrictUtf8.GetString(content).TrimStart('\uFEFF'); }
        catch (DecoderFallbackException e) { throw new FormatException("CSV and JSON data must be encoded as UTF-8.", e); }
        var table = extension == ".csv" ? DataReaders.Csv(text, tableName, options: limits with { InferNumbers = inferNumbers }) : DataReaders.Json(text, tableName, limits);
        cancellationToken.ThrowIfCancellationRequested(); return table;
    }
}
