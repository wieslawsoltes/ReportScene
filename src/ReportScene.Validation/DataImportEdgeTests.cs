using System.Text;
using ReportScene.Data;
using ReportScene.Model;

namespace ReportScene.Validation;

public static class DataImportEdgeTests
{
    public static IEnumerable<TestResult> Run()
    {
        TestResult Test(string name, Action action)
        { try { action(); return new("data import: " + name, true); } catch (Exception e) { return new("data import: " + name, false, e.ToString()); } }
        Value Typed(string type, string value) => XmlDataReader.Read($"<d xmlns:xsi='http://www.w3.org/2001/XMLSchema-instance' xmlns:xs='http://www.w3.org/2001/XMLSchema'><r><Value xsi:type='xs:{type}'>{value}</Value></r></d>").Rows[0]["Value"];
        yield return Test("XML DateTime zone is not invented", () => Conformance.Equal("2026-10-11T08:00:00", Typed("dateTime", "2026-10-11T08:00:00").AsString()));
        yield return Test("XML DateTime offset is preserved", () => Conformance.Equal("2026-10-11T08:00:00+02:00", Typed("dateTime", "2026-10-11T08:00:00+02:00").AsString()));
        yield return Test("XML date retains its timezone", () => Conformance.Equal("2026-10-11+02:00", Typed("date", "2026-10-11+02:00").AsString()));
        yield return Test("XML typed decimal cannot silently round", () => Conformance.Throws<FormatException>(() => Typed("decimal", "0.12345678901234567890123456789")));
        yield return Test("XML decimal trailing zeroes are not precision loss", () => Conformance.Equal(1m, Typed("decimal", "1.00000000000000000000000000000000").AsNumber()));
        yield return Test("XML finite float conversion", () => Conformance.Equal(.1m, Typed("float", "0.1").AsNumber()));
        yield return Test("XML nonfinite float rejected", () => Conformance.Throws<FormatException>(() => Typed("float", "INF")));
        yield return Test("XML underflow rejected", () => Conformance.Throws<FormatException>(() => Typed("double", "1e-100")));
        yield return Test("file dispatch handles uppercase extension", () => Conformance.Equal(1, DataFileReader.Read("data.XML", Encoding.UTF8.GetBytes("<d><r><Name>x</Name></r></d>"), "Data").Rows.Count));
        yield return Test("file dispatch preserves JSON number types", () => Conformance.Equal(ValueKind.Number, DataFileReader.Read("data.json", Encoding.UTF8.GetBytes("[{\"N\":12.5}]"), "Data").Rows[0]["N"].Kind));
        yield return Test("file dispatch handles UTF8 BOM", () => Conformance.Equal("A", DataFileReader.Read("data.csv", Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes("Name\nA")).ToArray(), "Data").Rows[0]["Name"].AsString()));
        yield return Test("invalid UTF8 cannot corrupt text silently", () => Conformance.Throws<FormatException>(() => DataFileReader.Read("data.csv", [0xFF, 0xFE, 0x00], "Data")));
        yield return Test("unsupported file format diagnosed", () => Conformance.Throws<NotSupportedException>(() => DataFileReader.Read("data.bin", [], "Data")));
        yield return Test("async-only stream provider works and owns stream", () =>
        {
            var stream = new AsyncOnlyStream(Encoding.UTF8.GetBytes("<d><r><N>12</N></r></d>"));
            var provider = new XmlDataProvider("Data", _ => ValueTask.FromResult<Stream>(stream));
            Conformance.Equal("12", provider.ReadAsync().GetAwaiter().GetResult().Rows[0]["N"].AsString());
            Conformance.Equal(true, stream.Disposed);
        });
        yield return Test("provider limits checked before opening stream", () => Conformance.Throws<ArgumentOutOfRangeException>(() => new XmlDataProvider("Data", _ => throw new Exception(), new(MaximumCells: 0))));
    }
    private sealed class AsyncOnlyStream(byte[] bytes) : Stream
    {
        private readonly MemoryStream input = new(bytes);
        public bool Disposed { get; private set; }
        public override bool CanRead => !Disposed;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException("Synchronous reads disabled.");
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => input.ReadAsync(buffer, cancellationToken);
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { Disposed = true; if (disposing) input.Dispose(); base.Dispose(disposing); }
    }
}
