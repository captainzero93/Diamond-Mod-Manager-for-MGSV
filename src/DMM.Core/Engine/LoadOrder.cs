// Load order moves, kept apart from the UI for tests
namespace DMM.Core.Engine;

public static class LoadOrder
{
    /// <summary>
    /// The order with the moving mods (kept in their own order) just above the first of the others, so the others win the
    /// files they share. Unchanged when none of the others is in the order.
    /// </summary>
    public static List<string> PlaceAbove(IReadOnlyList<string> order, IReadOnlySet<string> moving, IReadOnlySet<string> others)
    {
        var rest = order.Where(id => !moving.Contains(id)).ToList();
        int target = rest.FindIndex(others.Contains);
        if (target < 0)
            return order.ToList();
        rest.InsertRange(target, order.Where(moving.Contains));
        return rest;
    }

    /// <summary>The order with the moving mods just below the last of the others, so the moving mods win. Unchanged when they already are.</summary>
    public static List<string> PlaceBelow(IReadOnlyList<string> order, IReadOnlySet<string> moving, IReadOnlySet<string> others)
    {
        var rest = order.Where(id => !moving.Contains(id)).ToList();
        int target = rest.FindLastIndex(others.Contains);
        if (target < 0)
            return order.ToList();
        int firstMoving = order.ToList().FindIndex(moving.Contains);
        int lastOther = order.ToList().FindLastIndex(others.Contains);
        if (firstMoving > lastOther)
            return order.ToList();
        rest.InsertRange(target + 1, order.Where(moving.Contains));
        return rest;
    }
}
