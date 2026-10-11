using System.Data;
using System.Data.Common;
using ReportScene.Model;
using ReportTable = ReportScene.Model.DataTable;

namespace ReportScene.Data.AdoNet;

public sealed record QueryParameter(string Name, Value Value, DbType? Type = null, int? Size = null);

/// <summary>
/// Explicit host-owned database integration. The factory must return a fresh closed connection whose
/// ownership transfers to this provider. Connections, commands and readers are disposed on every path.
/// Imported report text can never instantiate a connection or execute a query by itself.
/// </summary>
public sealed class DbQueryDataProvider : IDataProvider
{
    private readonly Func<DbConnection> createConnection;
    private readonly string query;
    private readonly QueryParameter[] parameters;
    private readonly AdoDataOptions options;
    private readonly int timeoutSeconds;
    public string Name { get; }
    public DbQueryDataProvider(string name, Func<DbConnection> createConnection, string query,
        IEnumerable<QueryParameter>? parameters = null, AdoDataOptions? options = null, int timeoutSeconds = 30)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name); ArgumentNullException.ThrowIfNull(createConnection);
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        if (timeoutSeconds < 1) throw new ArgumentOutOfRangeException(nameof(timeoutSeconds));
        this.parameters = parameters?.ToArray() ?? [];
        if (this.parameters.Any(p => p is null || string.IsNullOrWhiteSpace(p.Name) || p.Size is < 0 || p.Type is { } type && !Enum.IsDefined(type)))
            throw new ArgumentException("Invalid query parameter name, type or size.", nameof(parameters));
        if (this.parameters.Select(p => p.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != this.parameters.Length)
            throw new ArgumentException("Query parameter names must be unique.", nameof(parameters));
        Name = name; this.createConnection = createConnection; this.query = query; this.options = options ?? new(); this.timeoutSeconds = timeoutSeconds;
    }
    public async ValueTask<ReportTable> ReadAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (OperatingSystem.IsBrowser()) throw new PlatformNotSupportedException("Direct ADO.NET queries require a desktop/server host. Browser applications should import data or use an authenticated data service.");
        await using var connection = createConnection() ?? throw new InvalidOperationException("The connection factory returned null.");
        if (connection.State != ConnectionState.Closed) throw new InvalidOperationException("The connection factory must return a fresh closed connection.");
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = query; command.CommandType = CommandType.Text; command.CommandTimeout = timeoutSeconds;
        foreach (var input in parameters)
        {
            var parameter = command.CreateParameter(); parameter.ParameterName = input.Name;
            parameter.Value = input.Value.Kind switch
            {
                ValueKind.Null => DBNull.Value, ValueKind.Number => input.Value.Number,
                ValueKind.Boolean => input.Value.Boolean, _ => input.Value.Text ?? ""
            };
            if (input.Type is { } type) parameter.DbType = type;
            if (input.Size is { } size) parameter.Size = size;
            command.Parameters.Add(parameter);
        }
        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SequentialAccess, cancellationToken).ConfigureAwait(false);
        var result = await AdoDataReader.ReadAsync(reader, Name, options, cancellationToken).ConfigureAwait(false);
        if (await reader.NextResultAsync(cancellationToken).ConfigureAwait(false))
            throw new NotSupportedException("A query data provider must return exactly one result set. Use separate providers for additional result sets.");
        return result;
    }
}
