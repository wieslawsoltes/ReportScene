using System.Diagnostics;
using ReportScene.Expressions;
using ReportScene.Model;

namespace ReportScene.Engine;

public sealed record PreparationOptions(int MaximumPages = 10_000, int MaximumItems = 1_000_000,
    int MaximumBandInstances = 1_000_000, int MaximumPasses = 5, DateTime? ReportDate = null,
    int MaximumSubreportDepth = 16, int MaximumSubreportInvocations = 10_000);
public sealed class ReportPreparationException(IReadOnlyList<Diagnostic> diagnostics) : Exception(string.Join("\n", diagnostics.Select(d => d.Message)))
{ public IReadOnlyList<Diagnostic> Diagnostics { get; } = diagnostics; }

/// <summary>Stateless per preparation; only immutable expression ASTs are cached between reports.</summary>
public sealed class ReportPreparer(ITextMeasurer? textMeasurer = null, ExpressionEngine? expressions = null)
{
    private readonly ITextMeasurer measurer = textMeasurer ?? new ApproximateTextMeasurer();
    private readonly ExpressionEngine expressions = expressions ?? new();
    public PreparedReport Prepare(ReportDocument document, IReadOnlyDictionary<string, Value>? parameters = null, PreparationOptions? options = null, CancellationToken cancellationToken = default)
    {
        options ??= new();
        if (options.MaximumPages < 1 || options.MaximumItems < 1 || options.MaximumBandInstances < 1 || options.MaximumPasses < 1 || options.MaximumSubreportDepth is < 1 or > 32 || options.MaximumSubreportInvocations < 1) throw new ArgumentOutOfRangeException(nameof(options));
        cancellationToken.ThrowIfCancellationRequested();
        var validation = DocumentValidator.Validate(document);
        if (validation.Any(d => d.Severity == DiagnosticSeverity.Error)) throw new ReportPreparationException(validation);
        var values = new Dictionary<string, Value>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in document.Parameters) values.Add(p.Name, p.DefaultValue);
        if (parameters is not null) foreach (var p in parameters) values[p.Key] = p.Value;
        foreach (var p in document.Parameters.Where(p => p.Required))
            if (!values.TryGetValue(p.Name, out var v) || v.IsNull || v.Kind == ValueKind.String && string.IsNullOrWhiteSpace(v.Text))
                throw new ReportPreparationException([new("RS1002", $"Required parameter '{p.Name}' has no value.")]);
        var context = new EvaluationContext
        {
            Parameters = values, Tables = document.Data.ToDictionary(t => t.Name, StringComparer.OrdinalIgnoreCase),
            ReportDate = options.ReportDate ?? DateTime.UtcNow
        };
        var watch = Stopwatch.StartNew(); int totalPages = 1;
        for (int pass = 0; pass < options.MaximumPasses; pass++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var run = new PreparationRun(document, context with { TotalPages = totalPages }, options, expressions, measurer, cancellationToken);
            var pages = run.Prepare();
            if (pages.Count == totalPages) return new(document.Name, pages, [], watch.Elapsed);
            totalPages = pages.Count;
        }
        throw new ReportPreparationException([new("RS2032", "Page-count dependent layout did not converge within the configured pass limit.")]);
    }
}
