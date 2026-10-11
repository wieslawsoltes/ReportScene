# Reusable data providers

## XML files and streams

```csharp
using ReportScene.Data;

var table = XmlDataReader.Read(xmlText, "Sales");
// No path or network request occurs inside this call.
// Untyped strings such as 001 stay strings unless InferNumbers is explicitly enabled.
```

Accepted scalar-table example:

```xml
<data xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
      xmlns:xs="http://www.w3.org/2001/XMLSchema">
  <row id="A01">
    <Customer>Northwind</Customer>
    <Amount xsi:type="xs:decimal">12.50</Amount>
    <Paid xsi:type="xs:boolean">true</Paid>
    <Note xsi:nil="true" />
  </row>
</data>
```

Columns are `Attribute_id`, `Customer`, `Amount`, `Paid`, and `Note`. Missing fields become null; empty elements remain empty strings. xsi:type supports string, boolean, decimal, bounded signed/unsigned integers, decimal-representable integer, finite round-trippable float/double, date and dateTime. Typed decimals that require precision loss are rejected. Stored date/time strings retain their supplied timezone or absence of one.

`Read(Stream)` honors BOM/XML-declaration encodings and leaves its caller-owned stream open. `XmlDataProvider` takes a host-supplied asynchronous stream factory, owns the returned stream, reads it asynchronously under an encoded-byte budget, then parses the bounded content. The parser never resolves an external resource. Limits apply to characters, rows, columns and normalized cells. Cancellation is checked throughout parsing and normalization.

`DataFileReader.Read(fileName, bytes, tableName, ...)` is the common headless/designer entry point. It accepts UTF-8 CSV/JSON and declaration-encoded XML. It does not load files or URLs itself.

## Existing .NET data

```csharp
using ReportScene.Data.AdoNet;

var table = AdoDataReader.FromDataTable(existingDataTable);
var snapshot = AdoDataReader.FromDataSet(existingDataSet);
var report = new ReportScene.Model.ReportDocument
{
    Data = snapshot.Tables.ToList(),
    Relations = snapshot.Relations.ToList()
};
```

Reader APIs consume the current result set and leave the supplied DbDataReader open:

```csharp
var table = await AdoDataReader.ReadAsync(reader, "Orders", cancellationToken: token);
```

DBNull becomes null; integral values and decimal amounts remain exact; strings and booleans retain their types. Date/time, Guid and TimeSpan values use invariant strings. Byte arrays are bounded and encoded as lossless base64 strings. Finite float/double values must round-trip through the decimal report representation. Unsupported CLR objects and composite relations require explicit application conversion.

## Explicit database queries

The optional `ReportScene.Data.AdoNet` package does not choose or ship a database vendor's driver. Application code supplies a fresh closed DbConnection from its installed driver:

```csharp
var provider = new DbQueryDataProvider(
    "Orders",
    createConnection: CreateApplicationConnection,
    query: "select Id, Amount from Orders where CustomerId = @CustomerId",
    parameters: [new QueryParameter("@CustomerId", 42, System.Data.DbType.Int32)],
    options: new AdoDataOptions(MaximumRows: 50_000),
    timeoutSeconds: 30);
var orders = await provider.ReadAsync(token);
```

Use the parameter syntax required by the selected driver. Values are passed through DbParameter objects, not interpolated into SQL. The provider owns and disposes the connection, command and reader on success, validation/materialization failure and cancellation. It rejects multiple result sets instead of truncating them.

Reports imported in the designer do not instantiate this provider, execute queries or import database credentials. Browser hosts are explicitly rejected before invoking the connection factory; use imported data or an application-controlled authenticated service for browser data access. True asynchronous execution and cancellation support depend on the selected ADO.NET driver.
