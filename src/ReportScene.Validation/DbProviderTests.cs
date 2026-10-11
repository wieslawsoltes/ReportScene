using System.Collections;
using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using ReportScene.Data.AdoNet;

namespace ReportScene.Validation;

internal static class DbProviderTests
{
    public static IEnumerable<TestResult> Run()
    {
        TestResult Test(string name, Action action)
        { try { action(); return new("ADO.NET query: " + name, true); } catch (Exception e) { return new("ADO.NET query: " + name, false, e.ToString()); } }
        yield return Test("typed parameters are bound without modifying SQL", () =>
        {
            var connection = new Connection(); var p = new DbQueryDataProvider("Orders", () => connection, "select Amount where Id = @Id", [new("@Id", 42, DbType.Int32), new("@Name", "O'Reilly", DbType.String, 100)], timeoutSeconds: 12);
            var t = p.ReadAsync().GetAwaiter().GetResult(); Conformance.Equal(2, t.Rows.Count); Conformance.Equal(12.50m, t.Rows[0]["Amount"].Number);
            var command = connection.LastCommand!; Conformance.Equal("select Amount where Id = @Id", command.CommandText); Conformance.Equal(12, command.CommandTimeout);
            Conformance.Equal(42m, command.Parameters[0].Value); Conformance.Equal("O'Reilly", command.Parameters[1].Value); Conformance.Equal(DbType.Int32, command.Parameters[0].DbType);
            Conformance.Equal(true, connection.Disposed); Conformance.Equal(true, command.Disposed); Conformance.Equal(true, command.Reader!.IsClosed);
        });
        yield return Test("materialization failure disposes all resources", () => { var c = new Connection(); var p = new DbQueryDataProvider("Data", () => c, "select Amount", options: new(MaximumRows: 1)); Conformance.Throws<FormatException>(() => p.ReadAsync().GetAwaiter().GetResult()); Conformance.Equal(true, c.Disposed && c.LastCommand!.Disposed && c.LastCommand.Reader!.IsClosed); });
        yield return Test("multiple result sets are not silently truncated", () => { var c = new Connection { MultipleResults = true }; var p = new DbQueryDataProvider("Data", () => c, "select Amount"); Conformance.Throws<NotSupportedException>(() => p.ReadAsync().GetAwaiter().GetResult()); Conformance.Equal(true, c.Disposed && c.LastCommand!.Reader!.IsClosed); });
        yield return Test("connection open failure still disposes connection", () => { var c = new Connection { FailOpen = true }; var p = new DbQueryDataProvider("Data", () => c, "select Amount"); Conformance.Throws<InvalidOperationException>(() => p.ReadAsync().GetAwaiter().GetResult()); Conformance.Equal(true, c.Disposed); });
        yield return Test("mid-query cancellation disposes reader and connection", () => { using var token = new CancellationTokenSource(); var c = new Connection { CancelOnExecute = token }; var p = new DbQueryDataProvider("Data", () => c, "select Amount"); Conformance.Throws<OperationCanceledException>(() => p.ReadAsync(token.Token).GetAwaiter().GetResult()); Conformance.Equal(true, c.Disposed && c.LastCommand!.Reader!.IsClosed); });
        yield return Test("each invocation receives an independent connection", () => { var opened = new List<Connection>(); var p = new DbQueryDataProvider("Data", () => { var c = new Connection(); opened.Add(c); return c; }, "select Amount"); p.ReadAsync().GetAwaiter().GetResult(); p.ReadAsync().GetAwaiter().GetResult(); Conformance.Equal(2, opened.Count); Conformance.Equal(true, opened.All(c => c.Disposed)); });
    }
    private sealed class Connection : DbConnection
    {
        private ConnectionState state;
        [AllowNull] public override string ConnectionString { get; set; } = "";
        public override string Database => "Fixture";
        public override string DataSource => "Fixture";
        public override string ServerVersion => "1";
        public override ConnectionState State => state;
        public bool Disposed { get; private set; }
        public bool FailOpen { get; init; }
        public bool MultipleResults { get; init; }
        public CancellationTokenSource? CancelOnExecute { get; init; }
        public Command? LastCommand { get; private set; }
        public override void ChangeDatabase(string databaseName) => throw new NotSupportedException();
        public override void Open() { if (FailOpen) throw new InvalidOperationException("Fixture open failure."); state = ConnectionState.Open; }
        public override Task OpenAsync(CancellationToken cancellationToken) { cancellationToken.ThrowIfCancellationRequested(); Open(); return Task.CompletedTask; }
        public override void Close() => state = ConnectionState.Closed;
        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) => throw new NotSupportedException();
        protected override DbCommand CreateDbCommand() => LastCommand = new(this);
        protected override void Dispose(bool disposing) { Disposed = true; Close(); base.Dispose(disposing); }
    }
    private sealed class Command(Connection owner) : DbCommand
    {
        [AllowNull] public override string CommandText { get; set; } = "";
        public override int CommandTimeout { get; set; }
        public override CommandType CommandType { get; set; }
        public override bool DesignTimeVisible { get; set; }
        public override UpdateRowSource UpdatedRowSource { get; set; }
        protected override DbConnection? DbConnection { get; set; } = owner;
        protected override DbTransaction? DbTransaction { get; set; }
        protected override DbParameterCollection DbParameterCollection { get; } = new Parameters();
        public DbDataReader? Reader { get; private set; }
        public bool Disposed { get; private set; }
        public override void Cancel() { }
        public override void Prepare() { }
        public override int ExecuteNonQuery() => throw new NotSupportedException();
        public override object? ExecuteScalar() => throw new NotSupportedException();
        protected override DbParameter CreateDbParameter() => new Parameter();
        protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior)
        {
            var table = new DataTable("Sales"); table.Columns.Add("Amount", typeof(decimal)); table.Rows.Add(12.50m); table.Rows.Add(7.25m);
            Reader = new DataTableReader(owner.MultipleResults ? [table, table.Copy()] : [table]);
            owner.CancelOnExecute?.Cancel(); return Reader;
        }
        protected override void Dispose(bool disposing) { Disposed = true; base.Dispose(disposing); }
    }
    private sealed class Parameter : DbParameter
    {
        public override DbType DbType { get; set; }
        public override ParameterDirection Direction { get; set; } = ParameterDirection.Input;
        public override bool IsNullable { get; set; }
        [AllowNull] public override string ParameterName { get; set; } = "";
        [AllowNull] public override string SourceColumn { get; set; } = "";
        public override object? Value { get; set; }
        public override bool SourceColumnNullMapping { get; set; }
        public override int Size { get; set; }
        public override void ResetDbType() => DbType = DbType.Object;
    }
    private sealed class Parameters : DbParameterCollection
    {
        private readonly List<DbParameter> items = [];
        public override int Count => items.Count;
        public override object SyncRoot => ((ICollection)items).SyncRoot;
        public override int Add(object value) { items.Add((DbParameter)value); return items.Count - 1; }
        public override void AddRange(Array values) { foreach (var value in values) Add(value!); }
        public override void Clear() => items.Clear();
        public override bool Contains(object value) => items.Contains((DbParameter)value);
        public override bool Contains(string value) => IndexOf(value) >= 0;
        public override void CopyTo(Array array, int index) => ((ICollection)items).CopyTo(array, index);
        public override IEnumerator GetEnumerator() => items.GetEnumerator();
        public override int IndexOf(object value) => items.IndexOf((DbParameter)value);
        public override int IndexOf(string name) => items.FindIndex(p => p.ParameterName == name);
        public override void Insert(int index, object value) => items.Insert(index, (DbParameter)value);
        public override void Remove(object value) => items.Remove((DbParameter)value);
        public override void RemoveAt(int index) => items.RemoveAt(index);
        public override void RemoveAt(string name) => items.RemoveAt(IndexOf(name));
        protected override DbParameter GetParameter(int index) => items[index];
        protected override DbParameter GetParameter(string name) => items[IndexOf(name)];
        protected override void SetParameter(int index, DbParameter value) => items[index] = value;
        protected override void SetParameter(string name, DbParameter value) => items[IndexOf(name)] = value;
    }
}
