namespace PoWatch.Client.Services;

/// <summary>
/// One entry of the shared model registry (<c>wwwroot/model-registry.json</c>) — the single source of
/// truth for the available VLMs (rule 1.5), shared with the inference worker. Only the fields the C# UI
/// needs (key + label) are modelled here; the worker reads the id/modelClass/dtype fields from the same
/// file. Extra JSON fields are ignored on deserialization.
/// </summary>
public sealed record ModelRegistryEntry(string Key, string Label);
