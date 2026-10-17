using PoWatch.Shared.Services.Tracking;

namespace PoWatch.Shared.Services.Sensing;

/// <summary>One synthetic moment: what the pixel layer, the detector and (sometimes) the VLM would report.</summary>
public sealed record SyntheticFrame(PixelSample Pixels, IReadOnlyList<Detection> Detections, string? Caption);

/// <summary>
/// A scripted room for demo mode and tests, so the whole pipeline runs with no camera, GPU or
/// model: a recurring person crosses the frame, a dog occasionally trots through, a cat naps on the
/// sofa, a mug sits on the table and the light drifts slowly. Seeded, so every run tells the same story.
/// </summary>
public sealed class SyntheticScene(int seed = 7)
{
    private const int Columns = 16;
    private const int Rows = 9;
    private const double WalkPeriodSeconds = 20;
    private const double WalkSeconds = 12;
    private const double CatPeriodSeconds = 90;
    private const double CatSeconds = 30;
    private const double DogPeriodSeconds = 60;
    private const double DogSeconds = 18;

    private static readonly string[] Captions =
    [
        "A person walks across the room.",
        "Someone carries a mug past the window.",
        "A dog trots through the room.",
        "A cat naps on the sofa.",
        "The room is quiet and empty.",
        "A person glances at the camera while walking by.",
    ];

    // Fixed looks, so the walker who crosses every 20 s comes back as the same regular.
    private static readonly float[] WalkerLook = Look(52, 56, 21);
    private static readonly float[] CatLook = Look(21, 42, 42);
    private static readonly float[] DogLook = Look(18, 36, 46);
    private static readonly float[] MugLook = Look(63, 63, 42);

    private readonly Random _random = new(seed);
    private DateTimeOffset? _startUtc;

    public SyntheticFrame Next(DateTimeOffset atUtc, bool withCaption = false)
    {
        _startUtc ??= atUtc;
        var t = (atUtc - _startUtc.Value).TotalSeconds;
        var detections = new List<Detection>();

        var walkPhase = t % WalkPeriodSeconds;
        var walking = walkPhase < WalkSeconds;
        if (walking)
        {
            var x0 = -0.05 + (walkPhase / WalkSeconds * 1.0);
            detections.Add(new Detection("person", Jitter(0.9), Math.Max(0, x0), 0.28, Math.Min(1, x0 + 0.14), 0.92, WalkerLook));
        }

        var catHere = t % CatPeriodSeconds < CatSeconds;
        if (catHere) detections.Add(new Detection("cat", Jitter(0.85), 0.56, 0.62, 0.70, 0.84, CatLook));
        var dogPhase = t % DogPeriodSeconds;
        var dogHere = dogPhase < DogSeconds;
        if (dogHere)
        {
            var x0 = 0.9 - (dogPhase / DogSeconds * 0.75);
            detections.Add(new Detection("dog", Jitter(0.86), x0, 0.55, Math.Min(1, x0 + 0.18), 0.82, DogLook));
        }
        detections.Add(new Detection("cup", Jitter(0.8), 0.72, 0.52, 0.76, 0.58, MugLook));

        var grid = new float[Columns * Rows];
        foreach (var d in detections.Where(d => d.Label != "cup"))
            Paint(grid, d, d.Label == "person" ? 0.5f : 0.08f);
        for (var i = 0; i < grid.Length; i++) grid[i] = Math.Min(1f, grid[i] + (float)(_random.NextDouble() * 0.01));

        var motion = grid.Average(v => (double)v);
        var luminance = 0.55 + (0.05 * Math.Sin(t / 300));
        int[] palette = walking ? [0x3A4A5C, 0xC8A27A, 0xB03A2E, 0x7F8C8D] : [0x3A4A5C, 0xC8A27A, 0x7F8C8D];

        var caption = withCaption
            ? walking ? Captions[_random.Next(0, 2)] : dogHere ? Captions[2] : catHere ? Captions[3] : Captions[4]
            : null;

        return new SyntheticFrame(new PixelSample(atUtc, motion, luminance, grid, palette), detections, caption);
    }

    private static float[] Look(params int[] bins)
    {
        var histogram = new float[64];
        foreach (var bin in bins) histogram[bin] += 1f / bins.Length;
        return histogram;
    }

    private double Jitter(double score) => Math.Clamp(score + ((_random.NextDouble() - 0.5) * 0.1), 0, 1);

    private static void Paint(float[] grid, Detection box, float value)
    {
        var c0 = Math.Clamp((int)(box.X0 * Columns), 0, Columns - 1);
        var c1 = Math.Clamp((int)(box.X1 * Columns), 0, Columns - 1);
        var r0 = Math.Clamp((int)(box.Y0 * Rows), 0, Rows - 1);
        var r1 = Math.Clamp((int)(box.Y1 * Rows), 0, Rows - 1);
        for (var r = r0; r <= r1; r++)
            for (var c = c0; c <= c1; c++)
                grid[(r * Columns) + c] = Math.Max(grid[(r * Columns) + c], value);
    }
}
