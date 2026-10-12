namespace ReportScene.Validation;

/// <summary>Async hosts use RunAsync to include delayed-operation contracts.</summary>
public static class ConformanceSuite
{
    public static List<TestResult> Run() => Conformance.Run().Concat(ImportDefaultTests.Run())
        .Concat(AggregateRegressionTests.Run()).Concat(MatrixTests.Run()).Concat(MatrixReportingTests.Run())
        .Concat(MatrixRenderingTests.Run()).Concat(XmlDataTests.Run()).Concat(AdoDataTests.Run())
        .Concat(DataImportEdgeTests.Run()).Concat(DataBudgetRegressionTests.Run()).Concat(SubreportTests.Run())
        .Concat(SubreportDesignerTests.Run()).Concat(SubreportRenderingTests.Run()).Concat(SubreportGraphTests.Run())
        .Concat(SubreportPhysicalPageTests.Run()).Concat(SubreportNavigationTests.Run()).Concat(TextLayoutPerformanceTests.Run())
        .Concat(PreparedPageIndexTests.Run()).Concat(PreparedStrokeTests.Run()).Concat(LinearStrokeTests.Run()).Concat(PreparedQueryTests.Run()).Concat(DesignerActivityTests.Run()).Concat(PreparedSearchTests.Run()).Concat(ParameterScopeTests.Run())
        .Concat(PaginationPerformanceTests.Run()).Concat(PreparationCancellationTests.Run()).Concat(RecoveryReadinessTests.Run()).Concat(ExportVisibilityTests.Run())
        .Concat(ImplicitSubreportRowTests.Run()).Concat(RelationIndexTests.Run()).Concat(DesignerGeometryTests.Run()).Concat(DesignerBatchTests.Run())
        .Concat(DesignerOutlineTests.Run()).Concat(DesignerPageTests.Run()).Concat(AuthoringTextCacheTests.Run()).Concat(AuthoringIntegrationTests.Run())
        .Concat(DesignerNavigationTests.Run()).Concat(DesignerPreferenceTests.Run()).Concat(PreviewNavigationTests.Run()).Concat(PreviewViewportTests.Run())
        .Concat(PreparedOutlineTests.Run()).Concat(DesignerViewportTests.Run()).ToList();
    public static async Task<List<TestResult>> RunAsync()
    {
        var results = Run(); results.AddRange(await DesignerOperationTests.RunAsync());
        results.AddRange(await DesignerPreferenceStoreTests.RunAsync()); results.AddRange(await PreferenceStoreEdgeTests.RunAsync()); return results;
    }
}
