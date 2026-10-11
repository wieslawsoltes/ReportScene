using ReportScene.Data;
using ReportScene.Data.AdoNet;
using ReportScene.Designer;
using ReportScene.Engine;
using ReportScene.Matrix;
using ReportScene.Model;
using ReportScene.Serialization;

int passed = 0;
void Check(bool condition, string name)
{ if (!condition) throw new InvalidOperationException(name); Console.WriteLine("PASS packaged " + name); passed++; }
var xml = XmlDataReader.Read("<d><r><Amount>12.50</Amount></r><r><Amount>7.25</Amount></r></d>", "Sales", new(InferNumbers: true));
Check(xml.Rows.Count == 2 && xml.Rows[0]["Amount"].Number == 12.5m, "XML provider");
var source = new System.Data.DataTable("Host"); source.Columns.Add("Amount", typeof(decimal)); source.Rows.Add(2.5m);
Check(AdoDataReader.FromDataTable(source).Rows[0]["Amount"].Number == 2.5m, "ADO.NET adapter");
var matrix = new MatrixEngine().Build(new MatrixItem { Measures = [new() { Expression = "Amount" }] }, xml.Rows);
Check(matrix.GrandTotal(0).Number == 19.75m, "matrix engine");
var report = new ReportDocument { Data = [xml], Pages = [new() { Bands = [new() { Kind = BandKind.ReportSummary, Items = [new TextItem { Id = "sum", Text = "[Sum(Sales.Amount)]" }] }] }] };
var clone = ReportJson.Clone(report);
Check(clone.Data[0].Rows[0]["Amount"].Number == 12.50m, "source-generated serialization");
var session = new DesignerSession(clone); session.Select("sum"); session.UpdateSelection("Rename", item => item with { Name = "Edited" }); session.Undo();
Check(session.CanRedo, "designer undo history");
var prepared = new ReportPreparer().Prepare(clone);
Check(prepared.Pages[0].Items.Single(i => i.Source is TextItem).Text == "19.75", "engine and transitive expressions");
Console.WriteLine($"{passed}/{passed} isolated package-consumer checks passed.");
