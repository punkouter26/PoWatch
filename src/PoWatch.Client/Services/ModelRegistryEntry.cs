namespace PoWatch.Client.Services;

/// <summary>
/// One entry of the shared model registry (<c>wwwroot/model-registry.json</c>) — the single source of
/// truth for the available VLMs (rule 1.5), shared with the inference worker. Only the fields the C# UI
/// needs are modelled here — the key it sends to the worker, the label the picker shows, and the hub
/// id the System page's self-test names so an operator knows which weights they are about to pull.
/// The worker reads the modelClass/dtype fields from the same file. Extra JSON fields are ignored on
/// deserialization.
/// </summary>
public sealed record ModelRegistryEntry(string Key, string Id, string Label);
