namespace PoWatch.Client.Services;

// Shapes the sensing JS bridges send to .NET. They arrive as JSON text and are parsed with the
// source-generated PoWatchJsonContext, so nothing here depends on reflection surviving the trimmer.

public sealed class PixelSamplePayload
{
    public DateTimeOffset AtUtc { get; init; }
    public double Motion { get; init; }
    public double Luminance { get; init; }
    public List<float> Grid { get; init; } = [];
    public List<int> Palette { get; init; } = [];
}

public sealed class DetectionPayload
{
    public string Label { get; init; } = string.Empty;
    public double Score { get; init; }
    public double X0 { get; init; }
    public double Y0 { get; init; }
    public double X1 { get; init; }
    public double Y1 { get; init; }

    /// <summary>64-bin colour histogram of the box centre, for recognising regulars.</summary>
    public List<float> Signature { get; init; } = [];
}

public sealed class DetectionsPayload
{
    public DateTimeOffset AtUtc { get; init; }
    public double LatencyMs { get; init; }
    public List<DetectionPayload> Detections { get; init; } = [];
}

public sealed class DetectorInfoPayload
{
    public string ModelId { get; init; } = string.Empty;
    public string Device { get; init; } = string.Empty;
    public double LoadMs { get; init; }
}

public sealed class VlmResultPayload
{
    public bool IsAvailable { get; init; }
    public string Status { get; init; } = string.Empty;
    public string Activity { get; init; } = string.Empty;
    public string ClinicalPayload { get; init; } = string.Empty;
}
