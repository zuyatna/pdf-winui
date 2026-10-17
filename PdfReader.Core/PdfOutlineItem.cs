using System.Collections.Generic;

namespace PdfReader.Core;

public sealed record PdfOutlineItem(
    string Title,
    int? PageIndex,
    int Level,
    IReadOnlyList<PdfOutlineItem> Children,
    double? NormalizedX = null,
    double? NormalizedY = null)
{
    public int? PageNumber => PageIndex.HasValue ? PageIndex.Value + 1 : null;

    public static IReadOnlyList<PdfOutlineItem> Flatten(IReadOnlyList<PdfOutlineItem> roots)
    {
        if (roots is null || roots.Count == 0)
        {
            return [];
        }

        var flattened = new List<PdfOutlineItem>();
        FlattenRecursive(roots, flattened);
        return flattened.AsReadOnly();
    }

    private static void FlattenRecursive(IReadOnlyList<PdfOutlineItem> items, List<PdfOutlineItem> destination)
    {
        for (int i = 0; i < items.Count; i++)
        {
            PdfOutlineItem item = items[i];
            destination.Add(item);
            if (item.Children is { Count: > 0 })
            {
                FlattenRecursive(item.Children, destination);
            }
        }
    }
}
