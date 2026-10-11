using System.Runtime.CompilerServices;
using ReportScene.Model;

namespace ReportScene.Engine;

internal sealed partial class PreparationRun
{
    private sealed class RelationIndexes
    {
        private readonly Dictionary<IReadOnlyList<DataRow>, Dictionary<string, DataKeyIndex>> sources = new(ReferenceEqualityComparer.Instance);
        private int count, indexedRows;
        public DataKeyIndex? Get(IReadOnlyList<DataRow> source, string column, CancellationToken cancellation)
        {
            if (source.Count < 64) return null;
            if (sources.TryGetValue(source, out var columns) && columns.TryGetValue(column, out var existing)) return existing;
            // Bound retention across all source/column pairs in this pass, including nested
            // invocations. Refuse additional caching rather than repeatedly flush and rebuild.
            if (count >= 16 || source.Count > 262144 - indexedRows) return null;
            var created = new DataKeyIndex(source, column, cancellation);
            if (columns is null) sources.Add(source, columns = new(StringComparer.OrdinalIgnoreCase));
            columns.Add(column, created); count++; indexedRows += source.Count; return created;
        }
    }
    // Child runs already share PreparationBudget. An ephemeron ties these bounded indexes
    // to that pass's lifetime without adding a strong global root for reports or data rows.
    private static readonly ConditionalWeakTable<PreparationBudget, RelationIndexes> relationIndexes = new();
    private DataKeyIndex? RelationIndex(IReadOnlyList<DataRow> source, string column)
        => options.EnableRelationIndexes ? relationIndexes.GetValue(budget, static _ => new()).Get(source, column, cancellation) : null;
}
