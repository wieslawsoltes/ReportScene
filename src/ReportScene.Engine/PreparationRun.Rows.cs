using ReportScene.Expressions;
using ReportScene.Model;

namespace ReportScene.Engine;

internal sealed partial class PreparationRun
{
    private IEnumerable<Instance> Emit(Band band, EvaluationContext parent, IReadOnlyList<Instance> inherited)
    {
        cancellation.ThrowIfCancellationRequested();
        if (!Visible(band.VisibleExpression, parent, band.Id)) yield break;
        if (band.Kind != BandKind.Data)
        {
            yield return Count(new(band, parent, inherited, band.KeepWithNext));
            foreach (var child in band.Children) foreach (var item in Emit(child, parent, inherited)) yield return item;
            yield break;
        }
        bool explicitSource = !string.IsNullOrEmpty(band.DataSource);
        IReadOnlyList<DataRow> source = explicitSource ? root.Tables[band.DataSource!].Rows : [parent.Row];
        string table = explicitSource ? band.DataSource! : parent.TableName;
        if (explicitSource && subreportDepth > 0 && table.Equals(parent.TableName, StringComparison.OrdinalIgnoreCase)) source = parent.Rows;
        var relation = document.Relations.FirstOrDefault(r => r.ParentTable.Equals(parent.TableName, StringComparison.OrdinalIgnoreCase) && r.ChildTable.Equals(table, StringComparison.OrdinalIgnoreCase));
        string? parentKey = band.ParentKey ?? relation?.ParentColumn, childKey = band.ChildKey ?? relation?.ChildColumn;
        var candidates = new List<(DataRow Row, Value[] Keys, int Index)>();
        var sort = band.Groups.Select(g => new Model.SortKey(g.Expression)).Concat(band.Sort).ToArray();
        IReadOnlyList<int>? positions = null;
        if (parentKey is not null && childKey is not null && RelationIndex(source, childKey) is { } keyIndex)
        {
            if (keyIndex.KeyedRowCount == 0) positions = Array.Empty<int>();
            else
            {
                var key = parent.Resolve(parentKey);
                // Mixed/coercing comparisons retain the original interleaved scan. In
                // particular, an early filter failure must not be replaced by a later
                // numeric-conversion failure from eagerly checking every relation key.
                if (keyIndex.CanUseLookup(key)) positions = keyIndex.Find(key, out _, cancellation);
            }
        }
        IEnumerable<int> ordinals = positions ?? (IEnumerable<int>)Enumerable.Range(0, source.Count);
        foreach (int ordinal in ordinals)
        {
            cancellation.ThrowIfCancellationRequested(); var row = source[ordinal]; int index = ordinal + 1;
            var context = parent with { Row = row, Rows = source, TableName = table, Parent = parent, RowNumber = index };
            if (positions is null && parentKey is not null && childKey is not null && (!row.TryGetValue(childKey, out var childValue) || Value.Compare(childValue, parent.Resolve(parentKey)) != 0)) continue;
            if (!Visible(band.Filter, context, band.Id)) continue;
            candidates.Add((row, sort.Select(s => Evaluate(s.Expression, context, band.Id)).ToArray(), index));
        }
        if (sort.Length > 0) candidates.Sort((a, b) =>
        {
            for (int i = 0; i < sort.Length; i++) { int n = Value.Compare(a.Keys[i], b.Keys[i]); if (n != 0) return sort[i].Descending ? -n : n; }
            return a.Index.CompareTo(b.Index);
        });
        var rows = candidates.Select(x => x.Row).ToArray();
        var baseContext = parent with { TableName = table, Rows = rows, Row = rows.FirstOrDefault() ?? new(), Parent = parent };
        var repeat = inherited.ToList();
        if (band.Header is { } header)
        {
            var head = new Instance(header, baseContext, inherited, true);
            yield return Count(head);
            if (band.RepeatHeader) repeat.Add(head);
        }
        foreach (var item in EmitGroups(band, baseContext, rows, 0, repeat)) yield return item;
        if (band.Footer is { } footer) yield return Count(new(footer, baseContext with { Row = rows.LastOrDefault() ?? new() }, inherited));
    }
    private IEnumerable<Instance> EmitGroups(Band data, EvaluationContext context, IReadOnlyList<DataRow> rows, int depth, IReadOnlyList<Instance> repeat)
    {
        if (depth == data.Groups.Count)
        {
            int index = 0;
            foreach (var row in rows)
            {
                var c = context with { Row = row, RowNumber = ++index };
                yield return Count(new(data, c, repeat, data.KeepWithNext));
                foreach (var child in data.Children) foreach (var instance in Emit(child, c, repeat)) yield return instance;
            }
            yield break;
        }
        var group = data.Groups[depth]; int start = 0;
        while (start < rows.Count)
        {
            var value = Evaluate(group.Expression, context with { Row = rows[start] }, data.Id);
            int end = start + 1;
            while (end < rows.Count && Value.Compare(value, Evaluate(group.Expression, context with { Row = rows[end] }, data.Id)) == 0) end++;
            var subset = rows.Skip(start).Take(end - start).ToArray();
            var c = context with { Row = subset[0], Rows = subset };
            yield return Count(new(group.Header, c, repeat, group.KeepHeaderWithFirstRow));
            foreach (var instance in EmitGroups(data, c, subset, depth + 1, repeat)) yield return instance;
            yield return Count(new(group.Footer, c with { Row = subset[^1] }, repeat));
            start = end;
        }
    }
}
