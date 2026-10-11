using System.Text;
using ReportScene.Data;
using ReportScene.Engine;
using ReportScene.Model;
using ReportScene.Serialization;

namespace ReportScene.Validation;

public static class XmlDataTests
{
    private const string Namespaces = " xmlns:xsi='http://www.w3.org/2001/XMLSchema-instance' xmlns:xs='http://www.w3.org/2001/XMLSchema'";
    public static IEnumerable<TestResult> Run()
    {
        TestResult Test(string name, Action action)
        { try { action(); return new("XML data: " + name, true); } catch (Exception e) { return new("XML data: " + name, false, e.ToString()); } }
        void Equal<T>(T expected, T actual) => Conformance.Equal(expected, actual);
        yield return Test("repeated rows and decoded scalar text", () => { var t = XmlDataReader.Read("<data><row><Name>A &amp; B</Name></row><row><Name>Second</Name></row></data>"); Equal(2, t.Rows.Count); Equal("A & B", t.Rows[0]["Name"].AsString()); });
        yield return Test("untyped numbers remain strings by default", () => Equal(ValueKind.String, XmlDataReader.Read("<d><r><Code>001</Code></r></d>").Rows[0]["Code"].Kind));
        yield return Test("explicit numeric inference is invariant", () => Equal(1.25m, XmlDataReader.Read("<d><r><Amount>1.25</Amount></r></d>", options: new(InferNumbers: true)).Rows[0]["Amount"].AsNumber()));
        yield return Test("empty and missing fields differ", () => { var t = XmlDataReader.Read("<d><r><A/></r><r><B>x</B></r></d>"); Equal("", t.Rows[0]["A"].Text); Equal(true, t.Rows[0]["B"].IsNull); Equal(true, t.Rows[1]["A"].IsNull); });
        yield return Test("nil becomes typed null", () => Equal(true, XmlDataReader.Read("<d" + Namespaces + "><r><A xsi:nil='true'/></r></d>").Rows[0]["A"].IsNull));
        yield return Test("nil numeric lexical form", () => Equal(true, XmlDataReader.Read("<d" + Namespaces + "><r><A xsi:nil='1'/></r></d>").Rows[0]["A"].IsNull));
        yield return Test("nil false does not erase content", () => Equal("value", XmlDataReader.Read("<d" + Namespaces + "><r><A xsi:nil='false'>value</A></r></d>").Rows[0]["A"].AsString()));
        yield return Test("nil with content is rejected", () => Conformance.Throws<FormatException>(() => XmlDataReader.Read("<d" + Namespaces + "><r><A xsi:nil='true'>value</A></r></d>")));
        yield return Test("schema decimals preserve exact amount and scale", () => { var v = XmlDataReader.Read("<d" + Namespaces + "><r><Amount xsi:type='xs:decimal'>12.50</Amount></r></d>").Rows[0]["Amount"]; Equal(12.50m, v.Number); Equal("12.50", v.AsString()); });
        yield return Test("schema bool accepts XML lexical values", () => Equal(true, XmlDataReader.Read("<d" + Namespaces + "><r><Enabled xsi:type='xs:boolean'>1</Enabled></r></d>").Rows[0]["Enabled"].Boolean));
        yield return Test("schema integer ranges enforced", () => Conformance.Throws<FormatException>(() => XmlDataReader.Read("<d" + Namespaces + "><r><A xsi:type='xs:byte'>128</A></r></d>")));
        yield return Test("unsigned long is exact decimal", () => Equal(18446744073709551615m, XmlDataReader.Read("<d" + Namespaces + "><r><A xsi:type='xs:unsignedLong'>18446744073709551615</A></r></d>").Rows[0]["A"].Number));
        yield return Test("unknown type namespace rejected", () => Conformance.Throws<FormatException>(() => XmlDataReader.Read("<d" + Namespaces + " xmlns:other='urn:other'><r><A xsi:type='other:decimal'>1</A></r></d>")));
        yield return Test("custom schema type rejected", () => Conformance.Throws<FormatException>(() => XmlDataReader.Read("<d" + Namespaces + "><r><A xsi:type='xs:custom'>x</A></r></d>")));
        yield return Test("CDATA and intentional whitespace preserved", () => Equal(" a < b ", XmlDataReader.Read("<d><r><A><![CDATA[ a < b ]]></A></r></d>").Rows[0]["A"].AsString()));
        yield return Test("attributes have a distinct configurable prefix", () => { var t = XmlDataReader.Read("<d><r id='5'><id>child</id></r></d>"); Equal("5", t.Rows[0]["Attribute_id"].AsString()); Equal("child", t.Rows[0]["id"].AsString()); });
        yield return Test("namespace declarations are not columns", () => Equal(1, XmlDataReader.Read("<d xmlns='urn:x'><r xmlns:p='urn:p'><p:A>x</p:A></r></d>").Columns.Count));
        yield return Test("row namespace selection", () => Equal(1, XmlDataReader.Read("<d xmlns='urn:x'><r><A>x</A></r></d>", options: new(RowElement: "r", RowNamespace: "urn:x")).Rows.Count));
        yield return Test("namespace collisions are diagnosed", () => Conformance.Throws<FormatException>(() => XmlDataReader.Read("<d xmlns:a='urn:a' xmlns:b='urn:b'><r><a:X>1</a:X></r><r><b:X>2</b:X></r></d>")));
        yield return Test("duplicate columns are diagnosed case-insensitively", () => Conformance.Throws<FormatException>(() => XmlDataReader.Read("<d><r><Name>A</Name><name>B</name></r></d>")));
        yield return Test("row kinds cannot be silently discarded", () => Conformance.Throws<FormatException>(() => XmlDataReader.Read("<d><first/><second/></d>")));
        yield return Test("nested data explicitly rejected", () => Conformance.Throws<FormatException>(() => XmlDataReader.Read("<d><r><A><B>x</B></A></r></d>")));
        yield return Test("mixed text rejected", () => Conformance.Throws<FormatException>(() => XmlDataReader.Read("<d><r>lost<A>x</A></r></d>")));
        yield return Test("scalar business attributes rejected", () => Conformance.Throws<FormatException>(() => XmlDataReader.Read("<d><r><A unit='USD'>1</A></r></d>")));
        yield return Test("DTD and external entities prohibited", () => Conformance.Throws<FormatException>(() => XmlDataReader.Read("<!DOCTYPE d [<!ENTITY x SYSTEM 'file:///not-read'>]><d><r><A>&x;</A></r></d>")));
        yield return Test("malformed XML diagnosed", () => Conformance.Throws<FormatException>(() => XmlDataReader.Read("<d><r></d>")));
        yield return Test("empty root is a valid empty table", () => Equal(0, XmlDataReader.Read("<d/>").Rows.Count));
        yield return Test("empty document rejected", () => Conformance.Throws<FormatException>(() => XmlDataReader.Read("")));
        yield return Test("row limit enforced", () => Conformance.Throws<FormatException>(() => XmlDataReader.Read("<d><r/><r/></d>", options: new(MaximumRows: 1))));
        yield return Test("column limit enforced", () => Conformance.Throws<FormatException>(() => XmlDataReader.Read("<d><r><A/><B/></r></d>", options: new(MaximumColumns: 1))));
        yield return Test("sparse normalization bounded before allocation", () => Conformance.Throws<FormatException>(() => XmlDataReader.Read("<d><r><A/></r><r><B/></r></d>", options: new(MaximumCells: 3))));
        yield return Test("character limit enforced", () => Conformance.Throws<FormatException>(() => XmlDataReader.Read("<d><r><A>long</A></r></d>", options: new(MaximumCharacters: 10))));
        yield return Test("invalid limits rejected", () => Conformance.Throws<ArgumentOutOfRangeException>(() => XmlDataReader.Read("<d/>", options: new(MaximumRows: 0))));
        yield return Test("pre-cancellation honored", () => Conformance.Throws<OperationCanceledException>(() => XmlDataReader.Read("<d/>", cancellationToken: new(true))));
        yield return Test("UTF16 stream declaration and caller ownership", () => { using var stream = new MemoryStream(Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes("<?xml version='1.0' encoding='utf-16'?><d><r><Name>Łódź</Name></r></d>")).ToArray()); Equal("Łódź", XmlDataReader.Read(stream).Rows[0]["Name"].AsString()); Equal(true, stream.CanRead); });
        yield return Test("provider owns stream even on failure", () => { var stream = new MemoryStream(Encoding.UTF8.GetBytes("<bad>")); var provider = new XmlDataProvider("Data", _ => ValueTask.FromResult<Stream>(stream)); Conformance.Throws<FormatException>(() => provider.ReadAsync().GetAwaiter().GetResult()); Equal(false, stream.CanRead); });
        yield return Test("provider cancellation precedes factory", () => { bool called = false; var p = new XmlDataProvider("Data", _ => { called = true; return ValueTask.FromResult<Stream>(new MemoryStream()); }); Conformance.Throws<OperationCanceledException>(() => p.ReadAsync(new(true)).GetAwaiter().GetResult()); Equal(false, called); });
        yield return Test("XML table drives the shared engine and native serialization", () =>
        {
            var t = XmlDataReader.Read("<d" + Namespaces + "><r><Amount xsi:type='xs:decimal'>12.50</Amount></r><r><Amount xsi:type='xs:decimal'>7.25</Amount></r></d>", "Sales");
            var d = new ReportDocument { Data = [t], Pages = [new() { Bands = [new() { Kind = BandKind.ReportSummary, Items = [new TextItem { Text = "[Sum(Sales.Amount)]" }] }] }] };
            var p = new ReportPreparer().Prepare(ReportJson.Clone(d)); Equal("19.75", p.Pages[0].Items.Single(i => i.Source is TextItem).Text);
        });
    }
}
