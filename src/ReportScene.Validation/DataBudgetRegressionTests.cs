using ReportScene.Data;
using ReportScene.Data.AdoNet;

namespace ReportScene.Validation;

public static class DataBudgetRegressionTests
{
    public static IEnumerable<TestResult> Run()
    {
        TestResult Test(string name, Action action)
        { try { action(); return new("data budgets: " + name, true); } catch (Exception e) { return new("data budgets: " + name, false, e.ToString()); } }
        string Xml(string number) => "<d xmlns:xsi='http://www.w3.org/2001/XMLSchema-instance' xmlns:xs='http://www.w3.org/2001/XMLSchema'><r><N xsi:type='xs:decimal'>" + number + "</N></r></d>";
        yield return Test("29-digit coefficient cannot silently round", () => Conformance.Throws<FormatException>(() => XmlDataReader.Read(Xml("7.9999999999999999999999999999"))));
        yield return Test("long decimal zero suffix is normalized without repeated slicing", () => Conformance.Equal(1m, XmlDataReader.Read(Xml("1." + new string('0', 10000))).Rows[0]["N"].AsNumber()));
        yield return Test("decimal underflow does not become zero", () => Conformance.Throws<FormatException>(() => XmlDataReader.Read(Xml("0.00000000000000000000000000001"))));
        yield return Test("negative decimal and normalized negative zero are accepted", () => { Conformance.Equal(-12.5m, XmlDataReader.Read(Xml("-0012.5000")).Rows[0]["N"].AsNumber()); Conformance.Equal(0m, XmlDataReader.Read(Xml("-0.000")).Rows[0]["N"].AsNumber()); });
        yield return Test("query options validated before connection creation", () => Conformance.Throws<ArgumentOutOfRangeException>(() => new DbQueryDataProvider("Data", () => throw new Exception("Must not open"), "select 1", options: new(MaximumCells: 0))));
        yield return Test("empty DataSet still validates limits", () => { using var source = new System.Data.DataSet(); Conformance.Throws<ArgumentOutOfRangeException>(() => AdoDataReader.FromDataSet(source, new(MaximumRows: 0))); });
    }
}
