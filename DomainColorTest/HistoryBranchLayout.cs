using System.Windows;

namespace DomainColorTest;

public sealed class BranchPresentation
{
    public List<Guid> Roots { get; set; } = [];
    public HashSet<Guid> Detached { get; set; } = [];
}

internal sealed record HistoryConnection(Guid Parent, Guid Child);

/// <summary>Iterative forest layout. Display roots never rewrite processing provenance.</summary>
internal sealed class HistoryBranchLayout
{
    public Dictionary<Guid, Rect> Bounds { get; } = [];
    public List<Guid> Roots { get; } = [];
    public List<HistoryConnection> Connections { get; } = [];
    public Dictionary<Guid, Guid?> DisplayParents { get; } = [];
    public Dictionary<Guid, Guid> NodeRoots { get; } = [];
    public Dictionary<Guid, (double Top, double Bottom)> RootBounds { get; } = [];

    public static HistoryBranchLayout Build(IReadOnlyList<GenerationEntry> items, BranchPresentation presentation)
    {
        var layout = new HistoryBranchLayout();
        var index = items.ToDictionary(g => g.Id);
        foreach (var item in items)
        {
            item.PresentationHint = "";
            Guid? parent = item.ParentGenerationId;
            if (presentation.Detached.Contains(item.Id))
            {
                item.PresentationHint = parent is { } old ? Localization.Format("Отдельная ветка. Прежний родитель: {0}", index.TryGetValue(old, out var previous) ? previous.Label : old.ToString()) : "";
                parent = null;
            }
            else if (parent is { } missing && !index.ContainsKey(missing))
            {
                item.PresentationHint = Localization.Text("Родитель отсутствует. Результат показан отдельной веткой.");
                parent = null;
            }
            layout.DisplayParents[item.Id] = parent;
        }
        var done = new HashSet<Guid>();
        foreach (var item in items)
        {
            var path = new HashSet<Guid>();
            Guid? id = item.Id;
            while (id is { } current && !done.Contains(current))
            {
                if (!path.Add(current))
                {
                    layout.DisplayParents[current] = null;
                    index[current].PresentationHint = Localization.Text("В истории обнаружен цикл. Связь скрыта только для отображения.");
                    break;
                }
                id = layout.DisplayParents[current];
            }
            done.UnionWith(path);
        }
        var children = items.GroupBy(g => layout.DisplayParents[g.Id] ?? Guid.Empty).ToDictionary(g => g.Key, g => g.ToArray());
        var roots = children.GetValueOrDefault(Guid.Empty) ?? [];
        var order = presentation.Roots.Select((id, q) => (id, q)).DistinctBy(v => v.id).ToDictionary(v => v.id, v => v.q);
        int row = 0;
        foreach (var root in roots.OrderBy(g => order.GetValueOrDefault(g.Id, int.MaxValue)).ThenBy(g => g.CreatedAt))
        {
            layout.Roots.Add(root.Id);
            double top = row * 78 + 4;
            var pending = new Stack<(GenerationEntry Entry, int Column, int Row)>();
            pending.Push((root, 0, row));
            while (pending.TryPop(out var next))
            {
                int at = next.Row < 0 ? row : next.Row;
                row = Math.Max(row, at + 1);
                layout.Bounds[next.Entry.Id] = new Rect(next.Column * 142 + 4, at * 78 + 4, 124, 68);
                layout.NodeRoots[next.Entry.Id] = root.Id;
                if (!children.TryGetValue(next.Entry.Id, out var descendants)) continue;
                for (int q = descendants.Length - 1; q >= 0; q--)
                {
                    layout.Connections.Add(new(next.Entry.Id, descendants[q].Id));
                    pending.Push((descendants[q], next.Column + 1, q == 0 ? at : -1));
                }
            }
            layout.RootBounds[root.Id] = (top, (row - 1) * 78 + 72);
            row++;
        }
        return layout;
    }

    public Guid RootOf(Guid id)
    {
        return NodeRoots.GetValueOrDefault(id, id);
    }

    public void Promote(Guid id, BranchPresentation presentation, int? insertion = null)
    {
        Guid oldRoot = RootOf(id);
        if (insertion is null && oldRoot == id) return;
        var roots = Roots.Where(r => r != id).ToList();
        int position = insertion ?? (roots.IndexOf(oldRoot) + 1);
        roots.Insert(Math.Clamp(position, 0, roots.Count), id);
        presentation.Detached.Add(id);
        presentation.Roots = roots;
    }
}
