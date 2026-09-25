using System.Numerics.Tensors;
using PoWatch.Domain.Models;

namespace PoWatch.Domain.Services;

public sealed record SpaceSummary(
    IReadOnlyList<double> MotionHeat,
    IReadOnlyList<double> PresenceHeat,
    int HottestCell,
    int FavouriteSpotCell,
    IReadOnlyDictionary<FrameEdge, long> Entries,
    IReadOnlyDictionary<FrameEdge, long> Exits,
    FrameEdge BusiestEdge);

/// <summary>Stat family A — where in the frame things happen.</summary>
public static class SpaceStats
{
    public static SpaceSummary Summarize(Rollup total)
    {
        ArgumentNullException.ThrowIfNull(total);

        var (motionHeat, hottest) = Normalise(total.Grid.Span);
        var (presenceHeat, favourite) = Normalise(total.PresenceGrid.Span);

        var busiestEdge = total.Entries.Keys.Union(total.Exits.Keys)
            .Where(edge => edge != FrameEdge.None)
            .OrderByDescending(edge => total.Entries.GetValueOrDefault(edge) + total.Exits.GetValueOrDefault(edge))
            .ThenBy(edge => edge)
            .FirstOrDefault(FrameEdge.None);

        return new SpaceSummary(motionHeat, presenceHeat, hottest, favourite, total.Entries, total.Exits, busiestEdge);
    }

    /// <summary>Scales a grid so its hottest cell is 1; returns that cell's index, or -1 for a blank grid.</summary>
    private static (double[] Heat, int HottestCell) Normalise(ReadOnlySpan<float> grid)
    {
        var heat = new double[SpatialGrid.Cells];
        if (grid.Length != SpatialGrid.Cells) return (heat, -1);

        var hottest = TensorPrimitives.IndexOfMax(grid);
        var max = grid[hottest];
        if (max <= 0) return (heat, -1);

        for (var i = 0; i < heat.Length; i++)
            heat[i] = grid[i] / max;

        return (heat, hottest);
    }
}
