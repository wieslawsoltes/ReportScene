namespace ReportScene.Validation;

/// <summary>The single validation entry point used by both console and WebAssembly hosts.</summary>
public static class ConformanceSuite
{
    public static List<TestResult> Run() => Conformance.Run().Concat(ImportDefaultTests.Run())
        .Concat(AggregateRegressionTests.Run()).Concat(MatrixTests.Run()).Concat(MatrixReportingTests.Run())
        .Concat(MatrixRenderingTests.Run()).Concat(XmlDataTests.Run()).Concat(AdoDataTests.Run())
        .Concat(DataImportEdgeTests.Run()).Concat(DataBudgetRegressionTests.Run()).Concat(SubreportTests.Run())
        .Concat(SubreportDesignerTests.Run()).Concat(SubreportRenderingTests.Run()).Concat(SubreportGraphTests.Run())
        .Concat(SubreportPhysicalPageTests.Run()).Concat(SubreportNavigationTests.Run()).Concat(TextLayoutPerformanceTests.Run()).ToList();
}
